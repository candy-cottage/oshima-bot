using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Content.Others;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

public sealed record ClubSummary(
    long Id, string Name, string Prefix, string MasterName, int Members, int MaxMembers,
    bool IsPublic, bool NeedApproval, double Funds);

public sealed record ClubMemberEntry(long UserId, string Display, ClubRole Role, string JoinedOn, double Contribution);

public sealed record ClubApplicantEntry(int Index, long UserId, string Display, string AppliedOn);

public sealed record ClubInviteEntry(int Index, long UserId, string Display, string InvitedOn);

/// <summary>单个社团的完整视图。</summary>
public sealed record ClubDetail(
    long Id, string Name, string Prefix, string Notice, long MasterUserId, string MasterName,
    bool IsPublic, bool NeedApproval, double Funds, int Members, int MaxMembers,
    bool IsMember, bool IsAdmin, bool IsMaster, string CreatedOn);

/// <summary>社团周常任务的一行。</summary>
public sealed record ClubTaskEntry(
    int Index, string Key, string Name, string Unit, int Target, int Progress,
    int RewardFunds, bool Claimed, string Hint)
{
    public bool Done => Progress >= Target;
    public bool Claimable => Done && !Claimed;
}

/// <summary>社团商店的一行（<see cref="Balance"/> 是社团基金，方便直接判断买不买得起）。</summary>
public sealed record ClubStoreEntry(
    int Index, string Name, long Price, string Description, double Balance);

/// <summary>社团商店视图：基金余额 + 货架。</summary>
public sealed record ClubStoreView(double Funds, IReadOnlyList<ClubStoreEntry> Goods);

/// <summary>社团市场的一行。</summary>
public sealed record ClubMarketEntry(
    int Index, long Id, string Name, int Quality, long Price, string Seller, bool Mine);

/// <summary>
/// 社团（旧版 <c>Club</c> + 那一堆 club* 端点）。
/// <para>旧版每个社团是一个 JSON 文件、用户身上记 <c>club</c> 编号；这里改成四张表，
/// 「一个人只能在一个社团」由 <c>ClubMembers.UserId</c> 的唯一索引兜底。</para>
/// </summary>
public interface IClubService
{
    /// <summary>当前社团任务周期（周三 4:00 起算），命令层用它显示「本周」，不再自己取时钟。</summary>
    string CurrentWeek { get; }

    Task<ActionResult> CreateAsync(long userId, string prefix, bool isPublic, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClubSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<ClubDetail?> InfoAsync(long clubId, long viewerId, CancellationToken cancellationToken = default);
    Task<ClubDetail?> MineAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> JoinAsync(long userId, long clubId, CancellationToken cancellationToken = default);
    Task<ActionResult> QuitAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> DisbandAsync(long userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClubMemberEntry>> MembersAsync(long userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClubApplicantEntry>> ApplicationsAsync(long userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClubInviteEntry>> InvitesAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> ApproveAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> RejectAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> InviteAsync(long userId, string target, CancellationToken cancellationToken = default);
    Task<ActionResult> KickAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> TransferAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> UpdateSettingsAsync(long userId, string action, string? value, CancellationToken cancellationToken = default);
    Task<ActionResult> DonateAsync(long userId, long credits, CancellationToken cancellationToken = default);

    // ---- 周常任务 ----
    Task<IReadOnlyList<ClubTaskEntry>> TasksAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> ContributeTaskAsync(long userId, int itemIndex, int quantity, CancellationToken cancellationToken = default);
    Task<ActionResult> ClaimTaskAsync(long userId, int index, CancellationToken cancellationToken = default);

    // ---- 社团商店（基金当货币）----
    Task<ClubStoreView> StoreAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> StoreBuyAsync(long userId, int index, int quantity, CancellationToken cancellationToken = default);
    Task<ActionResult> StoreSellAsync(long userId, int itemIndex, int quantity, CancellationToken cancellationToken = default);

    // ---- 改名审核（平台规范：公开名称先审后生效）----
    Task<IReadOnlyList<RenameRequestEntry>> PendingRenamesAsync(CancellationToken cancellationToken = default);
    Task<ActionResult> ApproveRenameAsync(long reviewerId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> RejectRenameAsync(long reviewerId, int index, CancellationToken cancellationToken = default);

    // ---- 社团市场（社团内部交易，手续费进基金）----
    Task<IReadOnlyList<ClubMarketEntry>> MarketAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> MarketListAsync(long userId, int itemIndex, long price, CancellationToken cancellationToken = default);
    Task<ActionResult> MarketDelistAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> MarketBuyAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> MarketClearAsync(long userId, CancellationToken cancellationToken = default);
}

public sealed partial class ClubService(
    IServiceScopeFactory scopeFactory,
    ILogger<ClubService> logger,
    IGameClock clock,
    IRandomSource random) : IClubService
{
    /// <summary>当前社团任务周期，来自注入的 <see cref="IGameClock"/>。</summary>
    public string CurrentWeek => GameplayRules.ClubWeek(clock.UtcNow);

    /// <summary>旧版没有明确上限，这里给一个防爆的数字（社团市场/任务都按成员数分摊，太大没意义）。</summary>
    public const int MaxMembers = 50;

    public const int MinPrefixLength = 3;
    public const int MaxPrefixLength = 4;

    /// <summary>旧版正则只放字母与特殊字符，但错误提示写的是「英文字母和数字」，这里把数字也放进来。</summary>
    private static readonly Regex PrefixPattern = new(@"^[a-zA-Z0-9\-_=+*%#^~.?!;:'"",]{3,4}$", RegexOptions.Compiled);

    private const int MinNameLength = 2;
    private const int MaxNameLength = 15;

    public Task<ActionResult> CreateAsync(long userId, string prefix, bool isPublic,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            string trimmed = (prefix ?? "").Trim();
            if (!PrefixPattern.IsMatch(trimmed))
            {
                return ActionResult.Fail("社团前缀不合法", "没有创建任何社团",
                    $"{MinPrefixLength}–{MaxPrefixLength} 位，只能是英文字母、数字和 -_=+*%#^~.?!;:'\", 这些符号", "/社团列表");
            }
            if (await db.ClubMembers.AnyAsync(x => x.UserId == userId, ct))
            {
                return ActionResult.Fail("你已经在一个社团里了", "没有创建任何社团",
                    "先 /退出社团，或让社长 /解散社团", "/我的社团");
            }

            string name = await NewNameAsync(db, ct);
            Club club = new()
            {
                Name = name,
                Prefix = trimmed,
                MasterUserId = userId,
                IsPublic = isPublic,
                NeedApproval = false,          // 旧版建团时固定「不需要批准」
                CreatedAtUtc = clock.UtcNow
            };
            db.Clubs.Add(club);
            await db.SaveChangesAsync(ct);

            db.ClubMembers.Add(new ClubMember
            {
                ClubId = club.Id, UserId = userId, Role = ClubRole.Master, JoinedAtUtc = clock.UtcNow
            });
            await db.SaveChangesAsync(ct);

            return ActionResult.Success("🏛️ 社团创建成功", $"[ {Display(club)} ] 编号 {club.Id}",
                [
                    $"- 社长：{await DisplayOfAsync(db, userId, ct)}",
                    $"- 加入规则：{(isPublic ? "公开" : "仅邀请")} / 免批准",
                    $"- 成员上限：{MaxMembers} 人",
                    "- 用 /社团设置 可以改公开状态、开启批准、写公告",
                    "- 用 /社团捐献 往社团基金里投金币"
                ],
                ["/我的社团", "/社团列表", "/社团成员"]);
        }, cancellationToken);

    public Task<IReadOnlyList<ClubSummary>> ListAsync(CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<ClubSummary>>(async (db, ct) =>
        {
            List<Club> clubs = await db.Clubs.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
            if (clubs.Count == 0) return [];

            List<ClubMember> members = await db.ClubMembers.AsNoTracking().ToListAsync(ct);
            Dictionary<long, int> counts = members.GroupBy(x => x.ClubId)
                .ToDictionary(g => g.Key, g => g.Count());
            Dictionary<long, string> names = await DisplaysAsync(db, [.. clubs.Select(x => x.MasterUserId)], ct);

            return [.. clubs.Select(club => new ClubSummary(
                club.Id, club.Name, club.Prefix,
                names.GetValueOrDefault(club.MasterUserId, $"旅人{club.MasterUserId}"),
                counts.GetValueOrDefault(club.Id), MaxMembers,
                club.IsPublic, club.NeedApproval, club.Funds))];
        }, cancellationToken);

    public Task<ClubDetail?> InfoAsync(long clubId, long viewerId, CancellationToken cancellationToken = default)
        => ReadAsync<ClubDetail?>(async (db, ct) =>
        {
            Club? club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == clubId, ct);
            if (club is null) return null;
            return await DetailAsync(db, club, viewerId, ct);
        }, cancellationToken);

    public Task<ClubDetail?> MineAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<ClubDetail?>(async (db, ct) =>
        {
            ClubMember? member = await db.ClubMembers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (member is null) return null;
            Club? club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == member.ClubId, ct);
            return club is null ? null : await DetailAsync(db, club, userId, ct);
        }, cancellationToken);

    public Task<ActionResult> JoinAsync(long userId, long clubId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            if (await db.ClubMembers.AnyAsync(x => x.UserId == userId, ct))
            {
                return ActionResult.Fail("你已经在一个社团里了", "没有加入任何社团",
                    "先 /退出社团 再加别的", "/我的社团");
            }
            Club? club = await db.Clubs.FirstOrDefaultAsync(x => x.Id == clubId, ct);
            if (club is null)
            {
                return ActionResult.Fail($"不存在编号 {clubId} 的社团", "没有加入任何社团",
                    "用 /社团列表 查看现有社团编号", "/社团列表");
            }

            ClubInvite? invite = await db.ClubInvites
                .FirstOrDefaultAsync(x => x.ClubId == clubId && x.UserId == userId, ct);
            if (!club.IsPublic && invite is null)
            {
                return ActionResult.Fail($"社团 [ {club.Name} ] 未公开", "没有加入任何社团",
                    "未公开的社团只能通过邀请加入，找社长或管理要个邀请", "/社团列表");
            }

            // 需要批准且不是被邀请的人 → 进申请队列，等社长/管理处理。
            if (club.NeedApproval && invite is null)
            {
                if (await db.ClubApplications.AnyAsync(x => x.ClubId == clubId && x.UserId == userId, ct))
                {
                    return ActionResult.Fail("你已经提交过申请", "没有重复提交",
                        "等社长或管理批准；用 /查看受邀人 可以看自己有没有被邀请", "/社团列表");
                }
                db.ClubApplications.Add(new ClubApplication { ClubId = clubId, UserId = userId, AppliedAtUtc = clock.UtcNow });
                return ActionResult.Success("📨 已提交加入申请", $"[ {club.Name} ]",
                    ["- 社团开启了「需要批准」，等社长或管理处理", "- 被管理员直接邀请可以跳过这一步"],
                    ["/社团列表", "/我的社团"]);
            }

            db.ClubMembers.Add(new ClubMember
            {
                ClubId = clubId, UserId = userId, Role = ClubRole.Member, JoinedAtUtc = clock.UtcNow
            });
            if (invite is not null) db.ClubInvites.Remove(invite);
            ClubApplication? application = await db.ClubApplications
                .FirstOrDefaultAsync(x => x.ClubId == clubId && x.UserId == userId, ct);
            if (application is not null) db.ClubApplications.Remove(application);
            await AdvanceTaskAsync(db, clubId, ClubTaskKind.Recruit, 1, ct);

            return ActionResult.Success("🏛️ 加入社团成功", $"[ {club.Name} ] 编号 {club.Id}",
                [$"- 社长：{await DisplayOfAsync(db, club.MasterUserId, ct)}", "- 现在可以 /社团捐献、/社团成员"],
                ["/我的社团", "/社团成员", "/社团捐献"]);
        }, cancellationToken);

    public Task<ActionResult> QuitAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            ClubMember? member = await db.ClubMembers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (member is null)
            {
                return ActionResult.Fail("你当前没有加入任何社团", "没有退出任何社团",
                    "用 /社团列表 看看有哪些社团", "/社团列表");
            }
            Club? club = await db.Clubs.FirstOrDefaultAsync(x => x.Id == member.ClubId, ct);
            string name = club?.Name ?? $"#{member.ClubId}";
            if (member.Role == ClubRole.Master)
            {
                return ActionResult.Fail("你是社长", "没有退出社团",
                    "先 /社团转让 @成员 把社长交出去，或者 /解散社团", "/社团成员");
            }

            db.ClubMembers.Remove(member);
            // 离团时在售挂单先退回，否则物品会一直托管在社团市场里。
            await ReleaseListingsAsync(db, member.ClubId, userId, ClubListingStatus.SellerLeft, ct);
            return ActionResult.Success("🚪 已退出社团", $"[ {name} ]",
                ["- 你对社团的捐献记录已经计入社团基金，不会退回"], ["/社团列表", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> DisbandAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            ClubMember? member = await db.ClubMembers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (member is null)
            {
                return ActionResult.Fail("你当前没有加入任何社团", "没有解散任何社团", "先 /创建社团", "/社团列表");
            }
            if (member.Role != ClubRole.Master)
            {
                return ActionResult.Fail("只有社长可以解散社团", "没有解散任何社团",
                    "让社长来操作", "/社团成员");
            }
            Club? club = await db.Clubs.FirstOrDefaultAsync(x => x.Id == member.ClubId, ct);
            if (club is null) return ActionResult.Fail("社团不存在", "没有解散任何社团", "稍后重试", "/社团列表");

            int members = await db.ClubMembers.CountAsync(x => x.ClubId == club.Id, ct);
            List<ClubMarketListing> listings = await db.ClubMarketListings
                .Where(x => x.ClubId == club.Id && x.Status == 0).ToListAsync(ct);
            await ReleaseListingRowsAsync(db, listings, ClubListingStatus.ClubDisbanded, ct);

            db.ClubMembers.RemoveRange(db.ClubMembers.Where(x => x.ClubId == club.Id));
            db.ClubApplications.RemoveRange(db.ClubApplications.Where(x => x.ClubId == club.Id));
            db.ClubInvites.RemoveRange(db.ClubInvites.Where(x => x.ClubId == club.Id));
            db.ClubTaskProgresses.RemoveRange(db.ClubTaskProgresses.Where(x => x.ClubId == club.Id));
            db.Clubs.Remove(club);

            return ActionResult.Success("💥 社团已解散", $"[ {club.Name} ] · {members} 名成员离开",
                ["- 社团基金不退还，申请与邀请一并清空"], ["/社团列表", "/状态"]);
        }, cancellationToken);

    public Task<IReadOnlyList<ClubMemberEntry>> MembersAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<ClubMemberEntry>>(async (db, ct) =>
        {
            ClubMember? mine = await db.ClubMembers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (mine is null) return [];
            List<ClubMember> members = await db.ClubMembers.AsNoTracking()
                .Where(x => x.ClubId == mine.ClubId)
                .OrderByDescending(x => x.Role).ThenBy(x => x.JoinedAtUtc).ToListAsync(ct);
            Dictionary<long, string> names = await DisplaysAsync(db, [.. members.Select(x => x.UserId)], ct);
            return [.. members.Select(member => new ClubMemberEntry(
                member.UserId, names.GetValueOrDefault(member.UserId, $"旅人{member.UserId}"),
                member.Role, DateOnly.FromDateTime(member.JoinedAtUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                member.Contribution))];
        }, cancellationToken);

    public Task<IReadOnlyList<ClubApplicantEntry>> ApplicationsAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<ClubApplicantEntry>>(async (db, ct) =>
        {
            ClubMember? mine = await db.ClubMembers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (mine is null || mine.Role == ClubRole.Member) return [];
            List<ClubApplication> rows = await db.ClubApplications.AsNoTracking()
                .Where(x => x.ClubId == mine.ClubId).OrderBy(x => x.AppliedAtUtc).ToListAsync(ct);
            Dictionary<long, string> names = await DisplaysAsync(db, [.. rows.Select(x => x.UserId)], ct);
            return [.. rows.Select((row, i) => new ClubApplicantEntry(
                i + 1, row.UserId, names.GetValueOrDefault(row.UserId, $"旅人{row.UserId}"),
                DateOnly.FromDateTime(row.AppliedAtUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))];
        }, cancellationToken);

    public Task<IReadOnlyList<ClubInviteEntry>> InvitesAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<ClubInviteEntry>>(async (db, ct) =>
        {
            ClubMember? mine = await db.ClubMembers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (mine is null || mine.Role == ClubRole.Member) return [];
            List<ClubInvite> rows = await db.ClubInvites.AsNoTracking()
                .Where(x => x.ClubId == mine.ClubId).OrderBy(x => x.InvitedAtUtc).ToListAsync(ct);
            Dictionary<long, string> names = await DisplaysAsync(db, [.. rows.Select(x => x.UserId)], ct);
            return [.. rows.Select((row, i) => new ClubInviteEntry(
                i + 1, row.UserId, names.GetValueOrDefault(row.UserId, $"旅人{row.UserId}"),
                DateOnly.FromDateTime(row.InvitedAtUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))];
        }, cancellationToken);

    public Task<ActionResult> ApproveAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember mine, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;

            List<ClubApplication> rows = await db.ClubApplications
                .Where(x => x.ClubId == club.Id).OrderBy(x => x.AppliedAtUtc).ToListAsync(ct);
            if (index <= 0 || index > rows.Count)
            {
                return ActionResult.Fail($"申请人 @{index} 不存在", "没有批准任何人",
                    "用 /查看申请人 拿编号", "/查看申请人");
            }
            ClubApplication application = rows[index - 1];
            int count = await db.ClubMembers.CountAsync(x => x.ClubId == club.Id, ct);
            if (count >= MaxMembers)
            {
                return ActionResult.Fail("社团已满员", "没有批准任何人", $"上限 {MaxMembers} 人", "/社团成员");
            }

            db.ClubMembers.Add(new ClubMember
            {
                ClubId = club.Id, UserId = application.UserId, Role = ClubRole.Member, JoinedAtUtc = clock.UtcNow
            });
            db.ClubApplications.Remove(application);
            await AdvanceTaskAsync(db, club.Id, ClubTaskKind.Recruit, 1, ct);
            return ActionResult.Success("✅ 已批准入团",
                $"{await DisplayOfAsync(db, application.UserId, ct)} → [ {club.Name} ]",
                [$"- 当前成员：{count + 1}/{MaxMembers}"], ["/社团成员", "/查看申请人"]);
        }, cancellationToken);

    public Task<ActionResult> RejectAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember _, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;

            List<ClubApplication> rows = await db.ClubApplications
                .Where(x => x.ClubId == club.Id).OrderBy(x => x.AppliedAtUtc).ToListAsync(ct);
            if (index <= 0 || index > rows.Count)
            {
                return ActionResult.Fail($"申请人 @{index} 不存在", "没有拒绝任何人",
                    "用 /查看申请人 拿编号", "/查看申请人");
            }
            ClubApplication application = rows[index - 1];
            string display = await DisplayOfAsync(db, application.UserId, ct);
            db.ClubApplications.Remove(application);
            return ActionResult.Success("🚫 已拒绝申请", display,
                ["- 对方不会收到通知"], ["/查看申请人", "/社团成员"]);
        }, cancellationToken);

    public Task<ActionResult> InviteAsync(long userId, string target, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember _, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;

            (long targetId, string display)? found = await FindUserAsync(db, target, ct);
            if (found is null)
            {
                return ActionResult.Fail("找不到这个玩家", "没有邀请任何人",
                    $"用 UID / Handle / 昵称指定；你输入的是「{target}」", "/社团成员");
            }
            (long targetId, string display) invitee = found.Value;
            if (await db.ClubMembers.AnyAsync(x => x.UserId == invitee.targetId, ct))
            {
                return ActionResult.Fail("对方已经在社团里了", "没有邀请任何人",
                    $"{invitee.display} 已有社团", "/社团成员");
            }
            if (await db.ClubInvites.AnyAsync(x => x.ClubId == club.Id && x.UserId == invitee.targetId, ct))
            {
                return ActionResult.Fail("已经邀请过了", "没有重复邀请",
                    "等对方 /加入社团 " + club.Id, "/查看受邀人");
            }

            db.ClubInvites.Add(new ClubInvite
            {
                ClubId = club.Id, UserId = invitee.targetId, InvitedAtUtc = clock.UtcNow
            });
            return ActionResult.Success("✉️ 已发出邀请",
                $"{invitee.display} → [ {club.Name} ]",
                [$"- 对方用 /加入社团 {club.Id} 即可加入（可绕过未公开与审批）"], ["/查看受邀人", "/社团成员"]);
        }, cancellationToken);

    public Task<ActionResult> KickAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember mine, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;

            List<ClubMember> members = await db.ClubMembers
                .Where(x => x.ClubId == club.Id)
                .OrderByDescending(x => x.Role).ThenBy(x => x.JoinedAtUtc).ToListAsync(ct);
            if (index <= 0 || index > members.Count)
            {
                return ActionResult.Fail($"成员 @{index} 不存在", "没有踢出任何人",
                    "用 /社团成员 拿编号", "/社团成员");
            }
            ClubMember target = members[index - 1];
            if (target.Role == ClubRole.Master)
            {
                return ActionResult.Fail("不能踢出社长", "没有踢出任何人",
                    "社主要走只能 /解散社团 或 /社团转让", "/社团成员");
            }
            if (target.Role == ClubRole.Admin && mine.Role != ClubRole.Master)
            {
                return ActionResult.Fail("只有社长能踢出管理", "没有踢出任何人",
                    "让社长来操作", "/社团成员");
            }

            string display = await DisplayOfAsync(db, target.UserId, ct);
            db.ClubMembers.Remove(target);
            await ReleaseListingsAsync(db, club.Id, target.UserId, ClubListingStatus.SellerLeft, ct);
            return ActionResult.Success("👢 已踢出社团", display,
                [$"- 剩余成员：{members.Count - 1}/{MaxMembers}"], ["/社团成员", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> TransferAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember mine, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;
            if (mine.Role != ClubRole.Master)
            {
                return ActionResult.Fail("只有社长可以转让", "没有转让社团",
                    "让社长来操作", "/社团成员");
            }

            List<ClubMember> members = await db.ClubMembers
                .Where(x => x.ClubId == club.Id)
                .OrderByDescending(x => x.Role).ThenBy(x => x.JoinedAtUtc).ToListAsync(ct);
            if (index <= 0 || index > members.Count)
            {
                return ActionResult.Fail($"成员 @{index} 不存在", "没有转让社团",
                    "用 /社团成员 拿编号", "/社团成员");
            }
            ClubMember target = members[index - 1];
            if (target.UserId == userId)
            {
                return ActionResult.Fail("不能转让给自己", "没有转让社团", "选别的成员", "/社团成员");
            }

            target.Role = ClubRole.Master;
            mine.Role = ClubRole.Member;
            club.MasterUserId = target.UserId;
            return ActionResult.Success("👑 社长已转让",
                $"{await DisplayOfAsync(db, target.UserId, ct)} 成为 [ {club.Name} ] 的新社长",
                ["- 你现在是普通成员，可以 /退出社团"], ["/社团成员", "/我的社团"]);
        }, cancellationToken);

    public Task<ActionResult> UpdateSettingsAsync(long userId, string action, string? value,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember mine, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;

            string op = (action ?? "").Trim().ToLowerInvariant();
            switch (op)
            {
                case "公开" or "public":
                    club.IsPublic = true;
                    return ActionResult.Success("🔓 社团已公开", $"[ {club.Name} ] 现在任何人都能加入",
                        [], ["/社团信息", "/社团设置 私密"]);
                case "私密" or "private":
                    club.IsPublic = false;
                    return ActionResult.Success("🔒 社团已转为私密", $"[ {club.Name} ] 只能通过邀请加入",
                        [], ["/社团信息", "/社团设置 公开"]);
                case "需批准" or "approval":
                    club.NeedApproval = true;
                    return ActionResult.Success("📋 已开启入团审批", $"[ {club.Name} ] 新申请需要社长/管理批准",
                        [], ["/查看申请人", "/社团设置 免批准"]);
                case "免批准" or "auto":
                    club.NeedApproval = false;
                    return ActionResult.Success("⚡ 已关闭入团审批", $"[ {club.Name} ] 公开社团可直接加入",
                        [], ["/社团信息"]);
                case "公告" or "notice":
                    string notice = (value ?? "").Trim();
                    (bool noticeOk, string noticeWhy) = NamingPolicy.CheckNotice(notice);
                    if (!noticeOk)
                    {
                        return ActionResult.Fail("公告未通过内容校验", "没有修改公告", noticeWhy, "/社团设置 公告");
                    }
                    club.Notice = notice;
                    return ActionResult.Success("📝 公告已更新", string.IsNullOrEmpty(notice) ? "（已清空）" : notice,
                        [], ["/社团信息"]);
                case "名称" or "name":
                    if (mine.Role != ClubRole.Master)
                    {
                        return ActionResult.Fail("只有社长可以改名", "没有提交改名申请", "让社长来操作", "/社团设置");
                    }
                    string name = (value ?? "").Trim();
                    (bool nameOk, string nameWhy) = NamingPolicy.Check(name, MinNameLength, MaxNameLength, "社团名");
                    if (!nameOk)
                    {
                        return ActionResult.Fail("社团名不符合命名规范", "没有提交改名申请", nameWhy, "/社团设置 名称");
                    }
                    if (await db.Clubs.AnyAsync(x => x.Name == name && x.Id != club.Id, ct))
                    {
                        return ActionResult.Fail($"社团名「{name}」已被占用", "没有提交改名申请", "换一个", "/社团设置 名称");
                    }
                    if (await db.RenameRequests.AnyAsync(
                        x => x.TargetType == RenameTargets.Club && x.TargetId == club.Id.ToString(CultureInfo.InvariantCulture)
                             && x.Status == 0, ct))
                    {
                        return ActionResult.Fail("已有一个改名申请在审核中", "没有重复提交",
                            "等管理员处理，或让管理员先拒绝掉旧的", "/我的社团");
                    }
                    // 平台规范：公开可见的名字要过审核，不能由玩家直接生效。
                    db.RenameRequests.Add(new RenameRequest
                    {
                        UserId = userId,
                        TargetType = RenameTargets.Club,
                        TargetId = club.Id.ToString(CultureInfo.InvariantCulture),
                        RequestedName = name,
                        Status = 0,
                        CreatedAtUtc = clock.UtcNow
                    });
                    return ActionResult.Success("📨 改名申请已提交",
                        $"[ {club.Name} ] → 「{name}」",
                        [
                            "- 名称要通过审核才会生效（防冒充官方与站外引流）",
                            "- 审核期间社团名保持原样；被拒绝会保留当前名字",
                            "- 社团前缀与公告不受影响"
                        ], ["/我的社团", "/社团设置"]);
                case "任命" or "admin" or "卸任" or "demote":
                {
                    List<ClubMember> members = await db.ClubMembers
                        .Where(x => x.ClubId == club.Id)
                        .OrderByDescending(x => x.Role).ThenBy(x => x.JoinedAtUtc).ToListAsync(ct);
                    if (!int.TryParse((value ?? "").Trim(), out int index) || index <= 0 || index > members.Count)
                    {
                        return ActionResult.Fail("成员编号无效", "没有改任何人的身份",
                            "用 /社团成员 拿编号：/社团设置 任命 2", "/社团成员");
                    }
                    ClubMember target = members[index - 1];
                    if (target.Role == ClubRole.Master)
                    {
                        return ActionResult.Fail("社长身份不能这样改", "没有改任何人的身份",
                            "转让社长用 /社团转让", "/社团成员");
                    }
                    bool promote = op is "任命" or "admin";
                    if (!promote && mine.Role != ClubRole.Master)
                    {
                        return ActionResult.Fail("只有社长能卸任管理", "没有改任何人的身份", "让社长来操作", "/社团成员");
                    }
                    target.Role = promote ? ClubRole.Admin : ClubRole.Member;
                    return ActionResult.Success(promote ? "🎖️ 已任命管理" : "🎖️ 已卸任管理",
                        await DisplayOfAsync(db, target.UserId, ct),
                        [$"- 当前身份：{(promote ? "管理" : "普通成员")}"], ["/社团成员", "/查看社团管理"]);
                }
                default:
                    return ActionResult.Fail("不认识的设置项", "没有修改任何设置",
                        "可用：公开 / 私密 / 需批准 / 免批准 / 公告 <文本> / 名称 <新名> / 任命 <@编号> / 卸任 <@编号>",
                        "/社团设置");
            }
        }, cancellationToken);

    public Task<ActionResult> DonateAsync(long userId, long credits, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            if (credits <= 0)
            {
                return ActionResult.Fail("捐献数量必须大于 0", "没有捐献任何金币",
                    "用法：/社团捐献 1000", "/社团捐献");
            }
            ClubMember? member = await db.ClubMembers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (member is null)
            {
                return ActionResult.Fail("你当前没有加入任何社团", "没有捐献任何金币",
                    "先 /加入社团 或 /创建社团", "/社团列表");
            }
            Club? club = await db.Clubs.FirstOrDefaultAsync(x => x.Id == member.ClubId, ct);
            if (club is null) return ActionResult.Fail("社团不存在", "没有捐献任何金币", "稍后重试", "/社团列表");

            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            if (account.Credits < credits)
            {
                return ActionResult.Fail("金币不足", "没有捐献任何金币",
                    $"需要 {credits} 金币，当前 {account.Credits:0.##}", "/状态");
            }

            PlayerService.ApplyCurrency(db, account, -credits, "credits", "club_donate", "club",
                club.Id.ToString(CultureInfo.InvariantCulture));
            club.Funds += credits;
            member.Contribution += credits;
            account.UpdatedAtUtc = clock.UtcNow;
            await AdvanceTaskAsync(db, club.Id, ClubTaskKind.Donate, (int)credits, ct);

            return ActionResult.Success("💰 捐献成功",
                $"{credits} 金币 → [ {club.Name} ]",
                [
                    $"- 社团基金：{club.Funds:0.##}",
                    $"- 个人累计捐献：{member.Contribution:0.##}",
                    $"- 当前金币：{account.Credits:0.##}"
                ], ["/我的社团", "/状态"]);
        }, cancellationToken);

    // ---------------- 内部 ----------------

    private async Task<T> ReadAsync<T>(Func<BotDbContext, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return await body(db, ct);
    }

    private async Task<ActionResult> MutateAsync(
        long userId, Func<BotDbContext, CancellationToken, Task<ActionResult>> body, CancellationToken ct)
    {
        if (userId <= 0) return PlayerService.NoIdentity;
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(ct);

            ActionResult result = await body(db, ct);
            if (!result.Ok)
            {
                await transaction.RollbackAsync(ct);
                return result;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActionResult.Fail("社团信息正被其他人修改", "本次操作已回滚", "稍等一秒后重试", "/社团信息");
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "社团写入冲突：UserId={UserId}", userId);
            return ActionResult.Fail("写入冲突", "本次操作已回滚", "你或对方可能已经在别的社团里了", "/社团列表");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "社团操作失败：UserId={UserId}", userId);
            return ActionResult.Fail("社团操作出现异常", "本次操作已回滚", "稍后重试", "/社团列表");
        }
    }

    /// <summary>社长或管理才能做的操作；返回社团与「我」的成员记录，权限不足时给三段式错误。</summary>
    private static async Task<(Club Club, ClubMember Mine, ActionResult? Denied)> RequireAdminAsync(
        BotDbContext db, long userId, CancellationToken ct)
    {
        ClubMember? member = await db.ClubMembers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (member is null)
        {
            return (null!, null!, ActionResult.Fail("你当前没有加入任何社团", "没有执行任何操作",
                "先 /加入社团 或 /创建社团", "/社团列表"));
        }
        Club? club = await db.Clubs.FirstOrDefaultAsync(x => x.Id == member.ClubId, ct);
        if (club is null)
        {
            return (null!, null!, ActionResult.Fail("社团数据缺失", "没有执行任何操作", "稍后重试", "/社团列表"));
        }
        if (member.Role == ClubRole.Member)
        {
            return (club, member, ActionResult.Fail("只有社长或管理可以操作", "没有执行任何操作",
                "找你所在社团的社长", "/社团成员"));
        }
        return (club, member, null);
    }

    private async Task<ClubDetail> DetailAsync(BotDbContext db, Club club, long viewerId, CancellationToken ct)
    {
        int members = await db.ClubMembers.CountAsync(x => x.ClubId == club.Id, ct);
        ClubMember? mine = await db.ClubMembers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClubId == club.Id && x.UserId == viewerId, ct);
        return new ClubDetail(
            club.Id, club.Name, club.Prefix, club.Notice, club.MasterUserId,
            await DisplayOfAsync(db, club.MasterUserId, ct),
            club.IsPublic, club.NeedApproval, club.Funds, members, MaxMembers,
            mine is not null, mine?.Role >= ClubRole.Admin, mine?.Role == ClubRole.Master,
            DateOnly.FromDateTime(club.CreatedAtUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static string Display(Club club) => $"{club.Prefix}·{club.Name}";

    private async Task<string> NewNameAsync(BotDbContext db, CancellationToken ct)
    {
        for (int i = 0; i < 20; i++)
        {
            string name = RandomNames.GenerateRandomChineseName(GameplayRules.NewRandom(random));
            if (!await db.Clubs.AnyAsync(x => x.Name == name, ct)) return name;
        }
        return $"社团{clock.UtcNow:HHmmss}";
    }

    private static async Task<string> DisplayOfAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        User? user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        return user is null ? $"旅人{userId}" : Display(user);
    }

    private static async Task<Dictionary<long, string>> DisplaysAsync(
        BotDbContext db, IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];
        return await db.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => Display(x), ct);
    }

    private static string Display(User user)
        => !string.IsNullOrWhiteSpace(user.Nickname) ? user.Nickname
            : !string.IsNullOrWhiteSpace(user.Handle) ? user.Handle
            : $"旅人{user.Id}";

    private static async Task<(long UserId, string Display)?> FindUserAsync(
        BotDbContext db, string target, CancellationToken ct)
    {
        string key = (target ?? "").Trim().TrimStart('@').Trim();
        if (key.Length == 0) return null;
        if (long.TryParse(key, out long id) && id > 0)
        {
            User? byId = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (byId is not null) return (byId.Id, Display(byId));
            if (await db.CorePlayers.AsNoTracking().AnyAsync(x => x.UserId == id, ct)) return (id, $"旅人{id}");
        }
        User? byName = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Handle == key || x.Nickname == key, ct);
        return byName is null ? null : (byName.Id, Display(byName));
    }
}
