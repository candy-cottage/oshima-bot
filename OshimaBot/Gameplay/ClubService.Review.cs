using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>审核队列里的一条（目前只有社团改名会进来，未来角色名/公会公告可以复用）。</summary>
public sealed record RenameRequestEntry(
    int Index, long Id, string TargetType, string TargetName, string RequestedName,
    string Requester, string SubmittedOn);

/// <summary>
/// 改名审核（平台规范要求）：玩家提交的公开名称先进 <c>RenameRequests</c> 队列，
/// 由管理员在后台批准后才生效。
/// <para>自动校验（<see cref="NamingPolicy"/>）只拦机器能判的：长度、不可见字符、冒充官方、
/// 站外引流、刷屏重复；**人工复核与举报处置不能省**。</para>
/// </summary>
public sealed partial class ClubService
{
    public Task<IReadOnlyList<RenameRequestEntry>> PendingRenamesAsync(CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<RenameRequestEntry>>(async (db, ct) =>
        {
            List<RenameRequest> rows = await db.RenameRequests.AsNoTracking()
                .Where(x => x.Status == 0).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
            if (rows.Count == 0) return [];

            Dictionary<long, string> names = await DisplaysAsync(db, [.. rows.Select(x => x.UserId)], ct);
            List<long> clubIds = [.. rows
                .Where(x => x.TargetType == "club" && long.TryParse(x.TargetId, out _))
                .Select(x => long.Parse(x.TargetId, CultureInfo.InvariantCulture))];
            Dictionary<long, string> clubs = clubIds.Count == 0
                ? []
                : await db.Clubs.AsNoTracking().Where(x => clubIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => $"{x.Prefix}·{x.Name}", ct);

            return [.. rows.Select((row, i) => new RenameRequestEntry(
                i + 1, row.Id, row.TargetType,
                row.TargetType == "club" && long.TryParse(row.TargetId, out long clubId)
                    ? clubs.GetValueOrDefault(clubId, $"社团#{row.TargetId}")
                    : row.TargetId,
                row.RequestedName,
                names.GetValueOrDefault(row.UserId, $"旅人{row.UserId}"),
                DateOnly.FromDateTime(row.CreatedAtUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))];
        }, cancellationToken);

    public Task<ActionResult> ApproveRenameAsync(long reviewerId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(reviewerId, async (db, ct) =>
        {
            (RenameRequest request, ActionResult? denied) = await TakeRequestAsync(db, index, ct);
            if (denied is not null) return denied;

            if (request.TargetType != "club" || !long.TryParse(request.TargetId, out long clubId))
            {
                return ActionResult.Fail("这类改名还没有审核实现", "没有修改任何名称",
                    $"目标类型是「{request.TargetType}」", "/改名审核");
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
        }, cancellationToken);

    public Task<ActionResult> RejectRenameAsync(long reviewerId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(reviewerId, async (db, ct) =>
        {
            (RenameRequest request, ActionResult? denied) = await TakeRequestAsync(db, index, ct);
            if (denied is not null) return denied;

            request.Status = 2;
            request.ReviewedAtUtc = clock.UtcNow;
            return ActionResult.Success("🚫 改名已拒绝", $"「{request.RequestedName}」",
                ["- 社团名保持不变", "- 对方可以重新提交别的名字"], ["/改名审核"]);
        }, cancellationToken);

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
