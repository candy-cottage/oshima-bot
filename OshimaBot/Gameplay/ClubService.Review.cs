using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>审核队列里的一条。</summary>
/// <param name="TargetType">原始类型（<see cref="RenameTargets"/>）。</param>
/// <param name="TargetKind">给人看的类型名，例如「角色名」「社团名」。</param>
public sealed record RenameRequestEntry(
    int Index, long Id, string TargetType, string TargetKind, string TargetName,
    string RequestedName, string Requester, string SubmittedOn);

/// <summary>
/// 改名审核（平台规范要求）：玩家提交的公开名称先进 <c>RenameRequests</c> 队列，
/// 由管理员批准后才生效。目前覆盖**社团名**与**角色名**。
/// <para>自动校验（<see cref="NamingPolicy"/>）只拦机器能判的：长度、不可见字符、冒充官方、
/// 站外引流、刷屏重复；**自动通过不等于审核通过**，人工复核与举报处置不能省。</para>
/// </summary>
public sealed partial class ClubService
{
    public Task<IReadOnlyList<RenameRequestEntry>> PendingRenamesAsync(CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<RenameRequestEntry>>(async (db, ct) =>
        {
            List<RenameRequest> rows = await db.RenameRequests.AsNoTracking()
                .Where(x => x.Status == 0).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
            if (rows.Count == 0) return [];

            Dictionary<long, string> requesters = await DisplaysAsync(
                db, [.. rows.Select(x => x.UserId)], ct);
            Dictionary<long, string> clubs = await ClubNamesAsync(db, rows, ct);
            Dictionary<string, string> characters = await CharacterNamesAsync(db, rows, ct);

            return [.. rows.Select((row, i) => new RenameRequestEntry(
                i + 1, row.Id, row.TargetType, RenameTargets.Display(row.TargetType),
                row.TargetType switch
                {
                    RenameTargets.Club when long.TryParse(row.TargetId, out long clubId) =>
                        clubs.GetValueOrDefault(clubId, $"社团#{row.TargetId}"),
                    RenameTargets.Character => characters.GetValueOrDefault(row.TargetId, "角色已不存在"),
                    _ => row.TargetId
                },
                row.RequestedName,
                requesters.GetValueOrDefault(row.UserId, $"旅人{row.UserId}"),
                row.CreatedAtUtc.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)))];
        }, cancellationToken);

    public Task<ActionResult> ApproveRenameAsync(long reviewerId, int index,
        CancellationToken cancellationToken = default)
        => MutateAsync(reviewerId, async (db, ct) =>
        {
            (RenameRequest request, ActionResult? denied) = await TakeRequestAsync(db, index, ct);
            if (denied is not null) return denied;

            return request.TargetType switch
            {
                RenameTargets.Club => await ApproveClubRenameAsync(db, request, ct),
                RenameTargets.Character => await ApproveCharacterRenameAsync(db, request, ct),
                _ => ActionResult.Fail("这类改名还没有审核实现", "没有修改任何名称",
                    $"目标类型是「{request.TargetType}」", "/改名审核")
            };
        }, cancellationToken);

    public Task<ActionResult> RejectRenameAsync(long reviewerId, int index,
        CancellationToken cancellationToken = default)
        => MutateAsync(reviewerId, async (db, ct) =>
        {
            (RenameRequest request, ActionResult? denied) = await TakeRequestAsync(db, index, ct);
            if (denied is not null) return denied;

            request.Status = 2;
            request.ReviewedAtUtc = clock.UtcNow;
            string kind = RenameTargets.Display(request.TargetType);
            return ActionResult.Success("🚫 改名已拒绝", $"「{request.RequestedName}」",
                [$"- {kind}保持不变", "- 对方可以重新提交别的名字"], ["/改名审核"]);
        }, cancellationToken);

    // ---------------- 分流：社团名 ----------------

    private async Task<ActionResult> ApproveClubRenameAsync(
        BotDbContext db, RenameRequest request, CancellationToken ct)
    {
        if (!long.TryParse(request.TargetId, out long clubId))
        {
            return ActionResult.Fail("申请数据异常", "没有修改任何名称",
                "目标编号不是社团 id，可以直接拒绝", "/改名审核");
        }
        Club? club = await db.Clubs.FirstOrDefaultAsync(x => x.Id == clubId, ct);
        if (club is null)
        {
            return ActionResult.Fail("社团已不存在", "没有修改任何名称",
                "可以把这个申请拒绝掉", "/改名审核");
        }
        // 审核期间可能被别人占用，批准时再查一次。
        if (await db.Clubs.AnyAsync(x => x.Name == request.RequestedName && x.Id != club.Id, ct))
        {
            return ActionResult.Fail($"「{request.RequestedName}」已被占用", "没有修改任何名称",
                "拒绝这个申请，让对方换个名字", "/改名审核");
        }

        string old = club.Name;
        club.Name = request.RequestedName;
        request.Status = 1;
        request.ReviewedAtUtc = clock.UtcNow;

        return ActionResult.Success("✅ 改名已通过", $"[ {club.Prefix}·{old} ] → [ {club.Prefix}·{club.Name} ]",
            ["- 已对全服生效", "- 请在后台留档，便于后续追溯与举报处置"], ["/改名审核", "/社团列表"]);
    }

    // ---------------- 分流：角色名 ----------------

    /// <summary>
    /// 角色名生效：改角色快照，并把 <c>Users.Nickname</c>（交易/社团/私信里的显示名）一起同步。
    /// <para>只有改的是主战角色才动 Nickname；改其它角色名不影响对外显示的身份。</para>
    /// </summary>
    private async Task<ActionResult> ApproveCharacterRenameAsync(
        BotDbContext db, RenameRequest request, CancellationToken ct)
    {
        CorePlayerRecord? wallet = await db.CorePlayers
            .FirstOrDefaultAsync(x => x.UserId == request.UserId, ct);
        List<PlayerCharacter> characters = await CharacterStore.ListAsync(
            db, request.UserId, wallet?.MainCharacterGuid, ct);
        PlayerCharacter? target = characters.FirstOrDefault(x => x.Guid == request.TargetId);
        if (target is null)
        {
            return ActionResult.Fail("角色已不存在", "没有修改任何名称",
                "可以把这个申请拒绝掉（对方可能已经把角色处理掉了）", "/改名审核");
        }

        string old = target.Name;
        target.Name = request.RequestedName;
        CharacterStore.Sync(target);

        bool isMain = wallet is not null
            && string.Equals(wallet.MainCharacterGuid, target.Guid, StringComparison.Ordinal);
        if (isMain)
        {
            User? user = await db.Users.FirstOrDefaultAsync(x => x.Id == request.UserId, ct);
            if (user is not null) user.Nickname = target.Name;
        }

        request.Status = 1;
        request.ReviewedAtUtc = clock.UtcNow;

        List<string> lines =
        [
            $"- 已对全服生效（{(isMain ? "主战角色" : "角色")}）",
            isMain ? "- 交易、社团、私信里显示的名字已同步" : "- 非主战角色，对外显示名不变",
            "- 请在后台留档，便于后续追溯与举报处置"
        ];
        return ActionResult.Success("✅ 改名已通过", $"{old} → 「{target.Name}」",
            lines, ["/改名审核", "/排行榜 等级"]);
    }

    // ---------------- 工具 ----------------

    private static async Task<Dictionary<long, string>> ClubNamesAsync(
        BotDbContext db, List<RenameRequest> rows, CancellationToken ct)
    {
        List<long> clubIds = [.. rows
            .Where(x => x.TargetType == RenameTargets.Club && long.TryParse(x.TargetId, out _))
            .Select(x => long.Parse(x.TargetId, CultureInfo.InvariantCulture))];
        if (clubIds.Count == 0) return [];
        return await db.Clubs.AsNoTracking().Where(x => clubIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => $"{x.Prefix}·{x.Name}", ct);
    }

    /// <summary>把待审的角色请求按用户分组，各自读一次角色列表拿当前名字。</summary>
    private static async Task<Dictionary<string, string>> CharacterNamesAsync(
        BotDbContext db, List<RenameRequest> rows, CancellationToken ct)
    {
        Dictionary<string, string> result = [];
        List<long> userIds = [.. rows
            .Where(x => x.TargetType == RenameTargets.Character)
            .Select(x => x.UserId).Distinct()];
        foreach (long userId in userIds)
        {
            foreach (PlayerCharacter character in await CharacterStore.ListAsync(db, userId, null, ct))
            {
                result[character.Guid] = character.Name;
            }
        }
        return result;
    }

    /// <summary>按队列序号取出待审申请；序号无效或已被别人处理时给三段式错误。</summary>
    private static async Task<(RenameRequest Request, ActionResult? Denied)> TakeRequestAsync(
        BotDbContext db, int index, CancellationToken ct)
    {
        List<RenameRequest> rows = await db.RenameRequests
            .Where(x => x.Status == 0).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
        if (index <= 0 || index > rows.Count)
        {
            return (null!, ActionResult.Fail($"审核申请 @{index} 不存在", "没有处理任何申请",
                rows.Count == 0 ? "当前没有待审申请" : $"当前待审 {rows.Count} 条，用 /改名审核 查看", "/改名审核"));
        }
        return (rows[index - 1], null);
    }
}
