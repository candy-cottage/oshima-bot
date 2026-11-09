using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OshimaBot.Gameplay;

/// <summary>
/// P1 玩法数值与文案的默认规则。集中在一处便于调参，业务层不硬编码数字。
/// 用户未指定数值时按本文件默认值实现（计划 §5.4）。
/// </summary>
public static class GameplayRules
{
    public const long StartingCredits = 1200;
    public const int StartingDiamonds = 30;
    public const int StartingItems = 3;

    public const int GachaCost = 5;
    public const int GachaTenCost = 45;
    public const int MaxGachaPerCall = 10;

    public const int SignInBaseCredits = 100;
    public const int SignInStreakBonus = 20;
    public const int SignInStreakCapDays = 7;
    public const int SignInDiamondEveryDays = 7;
    public const int SignInDiamondAmount = 5;

    public const int LevelsPerBreak = 10;
    public const int BreakCreditsBase = 500;
    public const int SalvageValueBase = 20;
    public const int UseExpBase = 30;

    public const int TrainCostCredits = 50;
    public const int TrainDefaultMinutes = 10;
    public const int TrainMaxMinutes = 120;
    public const int TrainExpBase = 120;
    public const int TrainExpPerLevel = 20;

    public const int QuestRewardCredits = 80;
    public const int QuestRewardDiamonds = 2;

    /// <summary>装备槽位，装备时按序占用第一个空槽。</summary>
    public static readonly IReadOnlyList<string> EquipmentSlots = ["weapon", "armor", "trinket"];

    public static readonly IReadOnlyList<string> FortuneTexts =
    [
        "大吉 · 今天适合孤身深入，意料之外的收获正在等你。",
        "中吉 · 稳扎稳打，旧线索会连成一条路。",
        "小吉 · 有人愿意搭把手，别拒绝。",
        "平 · 雾很厚，但路还在脚下。",
        "小凶 · 别急着做决定，先看看背包。",
        "中凶 · 今天不宜逞强，休整也是一种前进。",
        "大凶 · 收好贵重物品，等雾散了再出门。"
    ];

    public const string DefaultNotice =
        "欢迎来到铎京。旧存档未迁移（MySQL 数据已丢失），所有旅人从今天重新开始；" +
        "每日签到、抽卡、练级与背包已开放，商店与交易将在后续开放。";

    /// <summary>每日任务定义：key → 名称 / 目标次数。进度写在 daily_quests.progress_json。</summary>
    public static readonly IReadOnlyList<(string Key, string Name, int Target)> DailyQuests =
    [
        ("signin", "今日签到", 1),
        ("simulate", "完成一次战斗模拟", 1),
        ("gacha", "进行一次抽卡", 1)
    ];

    private static TimeZoneInfo GameTimeZone
        => TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Shanghai", out TimeZoneInfo? iana)
            ? iana
            : TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");

    /// <summary>游戏日（Asia/Shanghai），签到/任务/运势全部以它为准，避免 UTC 跨日。</summary>
    public static string Today()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GameTimeZone))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static long ExpForLevel(int level)
        => (long)Math.Ceiling(100 * Math.Pow(Math.Max(1, level), 1.5));

    public static int LevelCap(int breakLevel) => LevelsPerBreak * (breakLevel + 1);

    public static long SalvageValue(int quality, int quantity)
        => SalvageValueBase * (long)Math.Max(1, quality) * Math.Max(1, quality) * Math.Max(1, quantity);

    public static int UseExp(int quality, int quantity)
        => UseExpBase * Math.Max(1, quality) * Math.Max(1, quantity);

    public static long BreakCost(int breakLevel) => BreakCreditsBase * (breakLevel + 1L);

    public static long SignInCredits(int streak)
        => SignInBaseCredits + SignInStreakBonus * Math.Min(Math.Max(0, streak), SignInStreakCapDays);

    /// <summary>品质权重：普通 60 / 精良 25 / 稀有 10 / 史诗 4 / 传说 1。</summary>
    public static int RollQuality(Random random)
    {
        int roll = random.Next(100);
        return roll switch
        {
            < 60 => 1,
            < 85 => 2,
            < 95 => 3,
            < 99 => 4,
            _ => 5
        };
    }

    public static string QualityName(int quality) => quality switch
    {
        1 => "普通",
        2 => "精良",
        3 => "稀有",
        4 => "史诗",
        _ => "传说"
    };

    /// <summary>同一用户同一天的运势稳定不变（服务端确定性哈希，不用 Random.Shared）。</summary>
    public static string Fortune(long userId, string date)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{date}"));
        return FortuneTexts[hash[0] % FortuneTexts.Count];
    }

    // ---- P2：经济与战斗 ----

    public const long MarketFeeRatePercent = 5;
    public const long MarketMinPrice = 1;
    public const long MarketMaxPrice = 1_000_000;
    public const int MarketMaxListingsPerUser = 5;

    public const int ForgeCostCredits = 200;
    public const int ForgeMaterialQuantity = 2;
    public const int ForgeDefaultMinutes = 5;
    public const int ForgeOutputQuality = 3;
    public const int ForgeMaxMinutes = 120;

    public const int InstanceCostCredits = 80;
    public const int InstanceDefaultMinutes = 3;
    public const int InstanceRewardExp = 200;
    public const int InstanceRewardCredits = 60;
    public const int InstanceMaxMinutes = 120;

    public const int DuelCostCredits = 30;
    public const int DuelRewardCredits = 80;
    public const int DuelWinPoints = 10;
    public const int DuelLosePoints = 2;

    public const int BossCostCredits = 100;
    public const int BossRewardCredits = 300;
    public const int BossRewardDiamonds = 3;
    public const int BossWinPoints = 25;
    public const int RankingTop = 10;

    /// <summary>托管用户 id：市场上架 / 报价单在成交前的物品归属，不会出现在任何玩家背包。</summary>
    public const long EscrowUserId = -1;

    public static long MarketFee(long price)
        => Math.Max(1, price * MarketFeeRatePercent / 100);

    /// <summary>系统商店的默认货架（懒建，不进 Migration）。</summary>
    public static readonly IReadOnlyList<(string Name, long Price, int Stock)> SeedStoreGoods =
    [
        ("补给包", 120, -1),
        ("精良装备箱", 260, -1),
        ("史诗遗物匣", 500, 20)
    ];

    public static string RankingBoardName(string board) => board switch
    {
        "credits" => "金币榜",
        "level" => "等级榜",
        "points" => "战绩榜",
        _ => "金币榜"
    };

    public static string CatalogVersion => "core-v3";
}
