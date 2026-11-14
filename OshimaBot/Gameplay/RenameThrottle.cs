using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>
/// 改名提交的频率限制（2026-11-15 补）。
/// <para>背景：审核队列此前只有「同一目标只能有一份待审」这一条，**被拒绝后可以立刻再提**，
/// 于是「拒绝 → 重提 → 再拒绝」可以无限循环，管理员被刷得很难受。平台侧的举报与风控不会替我们兜这层。</para>
/// <para>两条限制叠在一起（不是玩法，纯粹为了让队列不被刷爆）：</para>
/// <list type="number">
/// <item>**同一目标被拒后冷却 <see cref="GameplayRules.RenameRejectCooldownHours"/> 小时**——
///       冷却期内换个名字也没用，避免「拒一次换一个再来」；</item>
/// <item>**每人每 <see cref="GameplayRules.RenameQuotaWindowHours"/> 小时最多提交
///       <see cref="GameplayRules.RenameQuotaPerWindow"/> 次**（角色名与社团名合计），
///       防止拿多个社团或反复建号绕过第一条。</item>
/// </list>
/// <para>批准不受限：那是管理员动作，不是玩家请求。</para>
/// </summary>
internal static class RenameThrottle
{
    /// <summary>允许提交时返回 null；被拦时返回可直接回给玩家的 <see cref="ActionResult"/>。</summary>
    public static async Task<ActionResult?> CheckAsync(
        BotDbContext db, long userId, string targetType, string targetId, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        string kind = RenameTargets.Display(targetType);
        string[] actions = ["/我的改名", "/角色"];

        // 1. 同一目标刚被拒：冷却期内一律不许再提。
        RenameRequest? rejected = await db.RenameRequests.AsNoTracking()
            .Where(x => x.TargetType == targetType && x.TargetId == targetId && x.Status == 2
                        && x.ReviewedAtUtc != null)
            .OrderByDescending(x => x.ReviewedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (rejected?.ReviewedAtUtc is DateTime reviewedAt)
        {
            DateTime readyAt = reviewedAt.AddHours(GameplayRules.RenameRejectCooldownHours);
            if (nowUtc < readyAt)
            {
                DateTime local = TimeZoneInfo.ConvertTimeFromUtc(readyAt, TimeZoneInfo.Local);
                return new ActionResult(false,
                    $"改名太频繁：这个{kind}刚被拒绝",
                    "没有提交改名申请",
                    [
                        $"- 被拒绝后要等 {GameplayRules.RenameRejectCooldownHours} 小时才能再提交",
                        $"- 还要等 {Format(readyAt - nowUtc)}（{local:MM-dd HH:mm} 之后可再试）",
                        "- 换个名字不能跳过冷却，想清楚再提"
                    ],
                    actions);
            }
        }

        // 2. 跨目标的窗口配额：最近这段时间里已经提交了几次。
        DateTime since = nowUtc.AddHours(-GameplayRules.RenameQuotaWindowHours);
        int recent = await db.RenameRequests.AsNoTracking()
            .CountAsync(x => x.UserId == userId && x.CreatedAtUtc > since, cancellationToken);

        if (recent >= GameplayRules.RenameQuotaPerWindow)
        {
            return new ActionResult(false,
                $"改名太频繁：{GameplayRules.RenameQuotaWindowHours} 小时内最多 {GameplayRules.RenameQuotaPerWindow} 次",
                "没有提交改名申请",
                [
                    $"- 你最近 {GameplayRules.RenameQuotaWindowHours} 小时已提交 {recent} 次（角色名与社团名合计）",
                    "- 等窗口过了再试；管理员没处理完就重复提，只会把队列堆住"
                ],
                actions);
        }

        return null;
    }

    private static string Format(TimeSpan wait)
        => wait.TotalHours >= 1
            ? $"{(int)wait.TotalHours} 小时 {wait.Minutes} 分钟"
            : $"{Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} 分钟";
}
