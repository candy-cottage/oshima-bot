using System.Globalization;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using System.Security.Cryptography;
using System.Text;

namespace OshimaBot.Gameplay;

/// <summary>
/// P1 玩法数值与文案的默认规则。集中在一处便于调参，业务层不硬编码数字。
/// 用户未指定数值时按本文件默认值实现（计划 §5.4）。
/// </summary>
public static class GameplayRules
{
    // ---- 旧版对齐：起始资源 ----
    // FunGameController.CreateSaved 只设 Inventory.Credits = 5000，Materials 与 Items 都是默认的 0。
    public const long StartingCredits = 5000;
    public const int StartingDiamonds = 0;
    public const int StartingItems = 0;

    // ---- 旧版对齐：抽卡 ----
    // FunGameConstant.DrawCardReduce = 1000（金币/次）、DrawCardReduce_Material = 5（钻石/次）；
    // 十连在旧版是按 10 倍直接乘，没有折扣。
    public const int GachaCostCredits = 1000;
    public const int GachaTenCostCredits = 10000;
    public const int GachaCost = 5;
    public const int GachaTenCost = 50;
    public const int MaxGachaPerCall = 10;

    /// <summary>
    /// 旧版 <c>FunGameConstant.DrawCardProbabilities</c>（百分数，总和 100）。
    /// 命中后按 `NextDouble() * 100` 依次比对，从高品到低品取第一个 ≤ 概率的档。
    /// </summary>
    /// <remarks>
    /// **顺序是从高品到低品**，与旧版 <c>OrderByDescending(o =&gt; (int)o)</c> 一致：
    /// 旧版拿 <c>NextDouble() * 100</c> 逐个比较 <c>q &lt;= 该档概率</c>，命中即止。
    /// 因为阈值是递减的，实际分布并非常量本身（白装约 80%），这里原样保留这层语义。
    /// </remarks>
    public static readonly IReadOnlyList<(int Tier, double Percent)> GachaQualityWeights =
    [
        (6, 3.06),    // Red    0 – 3.06
        (5, 6.33),    // Orange 3.06 – 6.33
        (4, 8.25),    // Purple 6.33 – 8.25
        (3, 13.48),   // Blue   8.25 – 13.48
        (2, 19.35),   // Green  13.48 – 19.35
        (1, 49.53)    // White  19.35 – 100（含超出 49.53 的尾部）
    ];

    // ---- 旧版对齐：签到（FunGameService.GetSignInResult）----
    // 金币 = Next(1000, 3000) + 10 × 已连续天数；材料 = Next(5, 15) + 已连续天数 / 7。
    public const int SignInCreditsMin = 1000;
    public const int SignInCreditsMax = 3000;
    public const int SignInCreditsPerDay = 10;
    public const int SignInMaterialMin = 5;
    public const int SignInMaterialMax = 15;

    // ---- 旧版对齐：分解返还（FunGameConstant.DecomposedMaterials，按品质固定值）----
    public const int TrainCostCredits = 0;
    public const int TrainDefaultMinutes = 10;
    public const int TrainMaxMinutes = 2880;      // 旧版 dailyTrainingMinutes
    public const int TrainExpPerMinute = 2;       // 旧版 experiencePerMinute

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
    public static string Today() => GameDay(DateTime.UtcNow);

    /// <summary>
    /// 商店日 key：旧版每日商店按本地 <c>Today.AddHours(4)</c> 刷新，
    /// 所以凌晨 4 点前仍算前一天。返回 <c>yyyy-MM-dd</c>。
    /// </summary>
    public static string StoreDay(DateTime utcNow)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), GameTimeZone);
        DateTime boundary = local.Hour >= 4
            ? local.Date.AddHours(4)
            : local.Date.AddDays(-1).AddHours(4);
        return DateOnly.FromDateTime(boundary).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>把 UTC 时间换算成游戏日字符串；纯函数，便于配假时钟测试。</summary>
    public static string GameDay(DateTime utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, GameTimeZone))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// 分解返还的**材料**数量（旧版 <c>FunGameConstant.DecomposedMaterials</c>，按品质固定值）。
    /// <para>1 普通 = 1、2 优秀 = 3、3 稀有 = 8、4 史诗 = 16、5 传说 = 32、6 神话 = 64、7 不朽 = 128；
    /// 旧版返还的是 <c>Inventory.Materials</c>（即「钻石」），不是金币。</para>
    /// </summary>
    public static long SalvageMaterials(int quality, int quantity) => quality switch
    {
        <= 1 => 1L * Math.Max(1, quantity),
        2 => 3L * Math.Max(1, quantity),
        3 => 8L * Math.Max(1, quantity),
        4 => 16L * Math.Max(1, quantity),
        5 => 32L * Math.Max(1, quantity),
        6 => 64L * Math.Max(1, quantity),
        _ => 128L * Math.Max(1, quantity)
    };

    /// <summary>
    /// 突破材料需求（照搬旧版 <c>FunGameConstant.LevelBreakNeedyList</c>）。
    /// <para>参数是**突破后的阶数**，与旧版 <c>LevelBreakNeedyList[LevelBreak + 1]</c> 一致。
    /// Core 的 <c>Character.LevelBreak</c> 默认是 <c>-1</c>（还没突破过），所以第一次突破是 <c>-1 → 0</c>，
    /// 取的是 <b>0 档</b>——这一档不是死数据。</para>
    /// </summary>
    public static IReadOnlyList<BreakNeed> LevelBreakNeedy(int breakLevel) => breakLevel switch
    {
        0 => [Material(40), Item("升华之印", 2)],
        1 => [Material(80), Item("升华之印", 4), Item("流光之印", 2)],
        2 => [Material(160), Item("升华之印", 8), Item("流光之印", 4), Item("永恒之印", 3)],
        3 => [Material(320), Item("升华之印", 16), Item("流光之印", 8), Item("永恒之印", 6), Item("原初之印", 5)],
        4 => [Material(640), Item("升华之印", 32), Item("流光之印", 16), Item("永恒之印", 12),
              Item("原初之印", 10), Item("创生之印", 5)],
        5 => [Material(1280), Item("升华之印", 64), Item("流光之印", 32), Item("永恒之印", 24),
              Item("原初之印", 20), Item("创生之印", 10)],
        _ => []
    };

    /// <summary>材料名取自运行时内容包常量（<c>InGameMaterial</c> 目前是「钻石」）。</summary>
    private static BreakNeed Material(int count)
        => new(General.GameplayEquilibriumConstant.InGameMaterial, count, true);

    private static BreakNeed Item(string name, int count) => new(name, count, false);

    /// <summary>下一次突破所需等级（取自 Core 的 <c>LevelBreakList</c>）；已无更高阶数时返回 null。</summary>
    public static int? NextBreakLevel(int levelBreak)
    {
        int[] breaks = [.. General.GameplayEquilibriumConstant.LevelBreakList];
        int next = levelBreak + 1;
        return next < breaks.Length ? breaks[next] : null;
    }

    /// <summary>
    /// 突破进度文案，口径同旧版「{LevelBreak + 1}/{总阶段数}」。
    /// Core 的 <c>LevelBreak</c> 从 -1 起算，所以未突破过显示 <c>0/6</c>，全部完成显示 <c>6/6</c>。
    /// </summary>
    public static string BreakProgress(int levelBreak)
    {
        int total = General.GameplayEquilibriumConstant.LevelBreakList.Count;
        return $"{Math.Clamp(levelBreak + 1, 0, total)}/{total}";
    }

    /// <summary>
    /// 签到奖励（旧版 <c>FunGameService.GetSignInResult</c>）。
    /// <para>金币 = <c>Next(1000, 3000) + 10 × 已连续天数</c>；材料 = <c>Next(5, 15) + 已连续天数 / 7</c>。
    /// 参数是**签到前**的连续天数（旧版用 <c>days</c> 计算、显示时 <c>days + 1</c>）。</para>
    /// </summary>
    public static (long Credits, int Materials) SignInReward(IRandomSource random, int previousStreak)
    {
        int days = Math.Max(0, previousStreak);
        int credits = random.Next(SignInCreditsMin, SignInCreditsMax) + SignInCreditsPerDay * days;
        int materials = random.Next(SignInMaterialMin, SignInMaterialMax) + days / 7;
        return (credits, materials);
    }

    /// <summary>
    /// 旧版抽卡品质判定：<c>q = NextDouble() * 100</c>，按 <see cref="GachaQualityWeights"/>
    /// 从高品到低品逐个比较 <c>q &lt;= 该档阈值</c>，命中即止；都不命中落到普通。
    /// </summary>
    public static int RollGachaQuality(IRandomSource random)
    {
        double q = random.NextDouble() * 100;
        foreach ((int tier, double percent) in GachaQualityWeights)
        {
            if (q <= percent) return tier;
        }
        return 1;
    }

    /// <summary>
    /// 旧版抽卡产出类型：<c>Random.Shared.Next(8)</c> 均匀落在 8 类上
    /// （0 魔法卡包 / 1 武器 / 2 防具 / 3 鞋子 / 4 饰品 / 5 角色 / 6 魔法卡 / 7 随机物品）。
    /// </summary>
    public enum GachaKind
    {
        MagicCardPack,
        Weapon,
        Armor,
        Shoes,
        Accessory,
        Character,
        MagicCard,
        AnyItem
    }

    public static GachaKind RollGachaKind(IRandomSource random) => (GachaKind)random.Next(8);

    /// <summary>旧版抽到已拥有的角色时，折算成 50 材料。</summary>
    public const int GachaDuplicateCharacterMaterials = 50;

    /// <summary>旧版 <c>ItemCanNotDrawCard</c>：这些类型不进抽卡池。</summary>
    public static bool IsDrawable(Item item) => item.ItemType is not (
        ItemType.Collectible or ItemType.QuestItem or ItemType.GiftBox or ItemType.Others);

    /// <summary>旧版抽卡命中阈值：<c>NextDouble() &gt; 0.8</c> 才出东西，否则「什么也没抽中」。</summary>
    public const double GachaHitThreshold = 0.8;

    public static bool RollGachaHit(IRandomSource random) => random.NextDouble() > GachaHitThreshold;

    /// <summary>
    /// 玩法品质档（1–5）→ Core 的 <see cref="QualityType"/>。
    /// 1 普通 / 2 优秀 / 3 稀有 / 4 史诗 / 5 传说（对应 White/Green/Blue/Purple/Orange）；
    /// 6、7 留作神话/不朽，正常掉落不会产生。
    /// </summary>
    public static QualityType QualityTypeOf(int tier) => tier switch
    {
        <= 1 => QualityType.White,
        2 => QualityType.Green,
        3 => QualityType.Blue,
        4 => QualityType.Purple,
        5 => QualityType.Orange,
        6 => QualityType.Red,
        _ => QualityType.Gold
    };

    /// <summary>Core 的 <see cref="QualityType"/> → 玩法品质档（1–7）。</summary>
    public static int QualityTierOf(QualityType type) => type switch
    {
        QualityType.White => 1,
        QualityType.Green => 2,
        QualityType.Blue => 3,
        QualityType.Purple => 4,
        QualityType.Orange => 5,
        QualityType.Red => 6,
        _ => 7
    };

    /// <summary>品质名一律取 Core 的 <c>ItemSet.GetQualityTypeName</c>，不再硬编码。</summary>
    public static string QualityName(int quality) => ItemSet.GetQualityTypeName(QualityTypeOf(quality));

    /// <summary>同一用户同一天的运势稳定不变（服务端确定性哈希，不用 Random.Shared）。</summary>
    public static string Fortune(long userId, string date)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{date}"));
        return FortuneTexts[hash[0] % FortuneTexts.Count];
    }

    // ---- P2：经济与战斗 ----

    public const long MarketFeeRatePercent = 15;   // 旧版 MarketSellItem：fee = amount * 0.15
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

    /// <summary>旧版 7 类秘境。</summary>
    public enum InstanceKind
    {
        Gold,
        Material,
        Exp,
        Region,
        Break,
        Skill,
        MagicCard
    }

    public static readonly IReadOnlyList<(InstanceKind Kind, string Name)> InstanceKinds =
    [
        (InstanceKind.Gold, "金币"),
        (InstanceKind.Material, "钻石"),
        (InstanceKind.Exp, "经验"),
        (InstanceKind.Region, "地区"),
        (InstanceKind.Break, "突破"),
        (InstanceKind.Skill, "技能"),
        (InstanceKind.MagicCard, "魔法卡")
    ];

    public static int ClampDifficulty(int difficulty) => Math.Clamp(difficulty, 1, 5);

    /// <summary>旧版：敌人数量 = 难度 + 1（2~6）。</summary>
    public static int InstanceEnemyCount(int difficulty) => ClampDifficulty(difficulty) + 1;

    /// <summary>旧版：敌人等级 = 难度 × 8（8~40）。</summary>
    public static int InstanceEnemyLevel(int difficulty) => ClampDifficulty(difficulty) * 8;

    public static string InstanceKindName(InstanceKind kind)
        => InstanceKinds.First(x => x.Kind == kind).Name;

    public static InstanceKind ParseInstanceKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return InstanceKind.Gold;
        string normalized = text.Trim();
        foreach ((InstanceKind kind, string name) in InstanceKinds)
        {
            if (normalized.Contains(name, StringComparison.Ordinal)) return kind;
        }
        return normalized switch
        {
            "gold" or "credits" => InstanceKind.Gold,
            "material" or "diamond" or "diamonds" => InstanceKind.Material,
            "exp" or "experience" => InstanceKind.Exp,
            "region" => InstanceKind.Region,
            "break" => InstanceKind.Break,
            "skill" => InstanceKind.Skill,
            "magic" or "card" => InstanceKind.MagicCard,
            _ => InstanceKind.Gold
        };
    }

    /// <summary>难度：数字 1-5，或 普通/精英/噩梦/深渊/地狱（1/3/4/5/5）。</summary>
    public static int ParseInstanceDifficulty(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1;
        string normalized = text.Trim();
        if (int.TryParse(normalized, out int value)) return ClampDifficulty(value);
        return normalized switch
        {
            "普通" or "简单" => 1,
            "困难" or "精英" => 3,
            "噩梦" => 4,
            "深渊" or "地狱" => 5,
            _ => 1
        };
    }

    /// <summary>
    /// 按类型与难度产出：经验 / 金币 / 物品件数。
    /// <para>基准取自旧版 <c>FightInstance</c>：金币约 550–1050、钻石 3–7、经验 570–1260，均随难度线性放大。</para>
    /// </summary>
    public static (long Exp, long Credits, long Diamonds, int Items) InstanceReward(InstanceKind kind, int difficulty)
    {
        int d = ClampDifficulty(difficulty);
        return kind switch
        {
            InstanceKind.Gold => (0, 800L * d, 0, 0),
            InstanceKind.Material => (0, 0, 5L * d, 0),
            InstanceKind.Exp => (900L * d, 0, 0, 0),
            InstanceKind.Region => (100L * d, 0, 0, d),
            InstanceKind.Break => (0, 0, 0, d),
            InstanceKind.Skill => (0, 0, 0, d),
            _ => (0, 0, 0, 1)
        };
    }

    public const int DuelCostCredits = 0;      // 旧版 FightCustom 不收费用
    public const int DuelRewardCredits = 0;    // 旧版决斗不发奖励
    public const int DuelWinPoints = 10;
    public const int DuelLosePoints = 2;

    public const int BossCostCredits = 0;      // 旧版 FightBoss 无入场费
    public const int BossRewardCredits = 0;    // 旧版击杀只给 Materials += Level，没有金币
    public const int BossRewardDiamonds = 0;   // 实际奖励按 Boss 等级发放（15/30/60），此常量已不参与计算
    public const int BossWinPoints = 25;
    public const int RankingTop = 10;

    /// <summary>托管用户 id：市场上架 / 报价单在成交前的物品归属，不会出现在任何玩家背包。</summary>
    public const long EscrowUserId = -1;

    public static long MarketFee(long price)
        => Math.Max(1, price * MarketFeeRatePercent / 100);

    public static string RankingBoardName(string board) => board.ToLowerInvariant() switch
    {
        "level" or "等级" => "等级榜",
        "points" or "战绩" => "战绩榜",
        _ => "金币榜"
    };

    public static string CatalogVersion => "core-v3";
}

/// <summary>
/// 一条突破材料需求。<see cref="IsMaterial"/> 为 true 时按钱包材料（<c>Materials</c>）结算，
/// 否则按背包里同名物品的数量结算。
/// </summary>
public sealed record BreakNeed(string Name, int Count, bool IsMaterial);
