namespace OshimaBot.Gameplay;

/// <summary>活动窗口的三种形态。旧版从内容包读 <c>Activity.StartTime/EndTime</c>，
/// v3 内容包还没有活动数据（升级时没带地区/活动包），所以窗口规则先写在代码里。</summary>
public enum ActivityWindow
{
    /// <summary>长期开放，没有起止时间。</summary>
    Permanent,
    /// <summary>每周六 04:00 至周一 04:00（本地时间）。</summary>
    Weekend,
    /// <summary>每年 12-20 04:00 至次年 01-05 04:00。</summary>
    WinterFestival
}

/// <summary>活动状态，与 Core <c>ActivityState</c> 的对外语义一致。</summary>
public enum ActivityPhase
{
    /// <summary>还早（开始前 16 小时之外）——Core 用 16 小时作为「即将开始」的预告窗口。</summary>
    Future,
    /// <summary>即将开始（开始前 16 小时内）。</summary>
    Upcoming,
    InProgress,
    Ended
}

/// <summary>任务成本：金币 / 钻石 / 背包物品。</summary>
public enum ActivityCostKind
{
    Credits,
    Diamonds,
    Items
}

/// <param name="Target">最多可以做几次；做满即可领奖（旧版一个任务只能领一次）。</param>
public sealed record ActivityRewardItem(string Name, string CatalogId, int Count);

public sealed record ActivityQuestSpec(
    string Key, string Name, string Description, ActivityCostKind CostKind, long CostAmount,
    int Target, long RewardCredits, int RewardDiamonds, IReadOnlyList<ActivityRewardItem> RewardItems);

public sealed record ActivitySpec(
    string Key, string Name, string Description, ActivityWindow Window,
    IReadOnlyList<ActivityQuestSpec> Quests);

/// <summary>
/// 活动目录（旧版从内容包读 <c>FunGameService.Activities</c>；v3 内容包暂无活动，先按运营需要写在代码里）。
/// <para>一个活动的玩法是「提交资源 → 攒进度 → 一次性领奖」：旧版 <c>PerformEvent</c> 做任务、
/// <c>ClaimEventPrize</c> 领奖，这里一一对应。</para>
/// </summary>
public static class ActivityCatalog
{
    public static readonly IReadOnlyList<ActivitySpec> Activities =
    [
        new("trial", "旅人试炼", "铎京对新旅人的长期试炼：交物资、交金币、交钻石，换印记与奖券。",
            ActivityWindow.Permanent,
            [
                new("supply", "物资征调", "上交 3 件背包物品", ActivityCostKind.Items, 3, 1,
                    3_000, 5, []),
                new("coin", "以币换印", "每次上交 15000 金币，最多 2 次", ActivityCostKind.Credits, 15_000, 2,
                    0, 0, [new("升华之印", "18001", 3)]),
                new("gem", "钻石换券", "每次上交 20 钻石，最多 2 次", ActivityCostKind.Diamonds, 20, 2,
                    0, 0, [new("十连奖券", "18009", 1)])
            ]),

        new("weekend", "周末疾行", "周末赛道开放，赛前来点准备，赢了还有观赛助威的彩头。",
            ActivityWindow.Weekend,
            [
                new("prep", "赛前准备", "每次上交 2000 金币，最多 3 次", ActivityCostKind.Credits, 2_000, 3,
                    0, 8, []),
                new("cheer", "观赛助威", "每次上交 1 件背包物品，最多 2 次", ActivityCostKind.Items, 1, 2,
                    1_500, 0, [])
            ]),

        new("winter", "双旦活动", "圣诞与元旦期间限时开放：献礼换奖券与改名卡。",
            ActivityWindow.WinterFestival,
            [
                new("gift", "节日献礼", "上交 10 钻石", ActivityCostKind.Diamonds, 10, 1,
                    5_888, 0, [new("十连奖券", "18009", 1)]),
                new("snow", "雪夜同行", "每次上交 1 件背包物品，最多 3 次", ActivityCostKind.Items, 1, 3,
                    0, 0, [new("改名卡", "18010", 1)])
            ])
    ];

    public static ActivitySpec? Find(string key)
        => Activities.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>按 1 基序号取活动（指令层用序号，旧版用活动 Id）。</summary>
    public static ActivitySpec? ByIndex(int index)
        => index <= 0 || index > Activities.Count ? null : Activities[index - 1];

    /// <summary>活动窗口换算成 UTC；<see cref="ActivityWindow.Permanent"/> 返回全 null。</summary>
    public static (DateTime? StartUtc, DateTime? EndUtc) Window(ActivityWindow window, DateTime utcNow)
    {
        DateTime local = GameplayRules.ToLocal(utcNow);
        switch (window)
        {
            case ActivityWindow.Weekend:
            {
                // 周六 04:00 → 周一 04:00；周四看就是「本周六」，周日看就是「已开始」。
                int offset = ((int)DayOfWeek.Saturday - (int)local.DayOfWeek + 7) % 7;
                DateTime start = local.Date.AddDays(offset).AddHours(4);
                return (GameplayRules.ToUtc(start), GameplayRules.ToUtc(start.AddDays(2)));
            }
            case ActivityWindow.WinterFestival:
            {
                DateTime start = new(local.Year, 12, 20, 4, 0, 0);
                DateTime end = new(local.Year + 1, 1, 5, 4, 0, 0);
                if (local.Month == 1 && local.Day <= 5)
                {
                    start = new DateTime(local.Year - 1, 12, 20, 4, 0, 0);
                    end = new DateTime(local.Year, 1, 5, 4, 0, 0);
                }
                return (GameplayRules.ToUtc(start), GameplayRules.ToUtc(end));
            }
            default:
                return (null, null);
        }
    }

    public static ActivityPhase Phase((DateTime? StartUtc, DateTime? EndUtc) window, DateTime utcNow)
    {
        if (window.StartUtc is null && window.EndUtc is null) return ActivityPhase.InProgress;
        if (window.StartUtc is { } start && utcNow < start.AddHours(-16)) return ActivityPhase.Future;
        if (window.StartUtc is { } soon && utcNow < soon) return ActivityPhase.Upcoming;
        if (window.EndUtc is { } end && utcNow >= end) return ActivityPhase.Ended;
        return ActivityPhase.InProgress;
    }

    public static string PhaseName(ActivityPhase phase) => phase switch
    {
        ActivityPhase.Future => "未开放",
        ActivityPhase.Upcoming => "即将开始",
        ActivityPhase.InProgress => "进行中",
        _ => "已结束"
    };

    public static string CostText(ActivityQuestSpec quest) => quest.CostKind switch
    {
        ActivityCostKind.Credits => $"{quest.CostAmount} 金币",
        ActivityCostKind.Diamonds => $"{quest.CostAmount} 钻石",
        _ => $"{quest.CostAmount} 件物品"
    };

    public static string RewardText(ActivityQuestSpec quest)
    {
        List<string> parts = [];
        if (quest.RewardCredits > 0) parts.Add($"{quest.RewardCredits} 金币");
        if (quest.RewardDiamonds > 0) parts.Add($"{quest.RewardDiamonds} 钻石");
        parts.AddRange(quest.RewardItems.Select(item => $"{item.Name} ×{item.Count}"));
        return parts.Count == 0 ? "（无）" : string.Join(" + ", parts);
    }
}
