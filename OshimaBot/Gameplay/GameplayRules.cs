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

    /// <summary>
    /// 主力小队上限。旧版「设置小队」接受 1–4 个参数（FunGameOrderList 帮助文本与
    /// FunGameController「请先设置1-4名角色」），v3 早期实现硬编码 3，2026-11-13 恢复为 4。
    /// 装备槽位不在这里：装备走 Core 的 6 槽（EquipSlot），旧的三槽字符串常量已删除。
    /// </summary>
    public const int SquadSize = 4;

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

    // 不再提供无参 Today()：业务日期必须走 IGameClock.Today()，
    // 否则假时钟（测试、回放）会和系统时间对不上。这里只保留纯函数 GameDay(DateTime)。

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

    /// <summary>本地时间（Asia/Shanghai）→ UTC；活动窗口这类按本地钟点定义的规则用它。</summary>
    public static DateTime ToUtc(DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), GameTimeZone);

    /// <summary>UTC → 本地时间（Asia/Shanghai）。</summary>
    public static DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), GameTimeZone);

    /// <summary>当前「商店日」的起点（本地 4:00）对应的 UTC 时间；私信频次上限等按它切日。</summary>
    public static DateTime StoreDayStartUtc(DateTime utcNow)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), GameTimeZone);
        DateTime boundary = local.Hour >= 4
            ? local.Date.AddHours(4)
            : local.Date.AddDays(-1).AddHours(4);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(boundary, DateTimeKind.Unspecified), GameTimeZone);
    }

    /// <summary>
    /// 社团周常周期 key（<c>yyyy-Www</c>，ISO 周、周一起算，边界沿用商店日的 4:00）。
    /// <para>与 <see cref="StoreDay"/> 同一套时区与切日口径，避免玩家在凌晨 3 点看到「任务已刷新」。</para>
    /// </summary>
    public static string ClubWeek(DateTime utcNow)
    {
        DateTime day = DateTime.ParseExact(
            StoreDay(utcNow), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{ISOWeek.GetYear(day):0000}-W{ISOWeek.GetWeekOfYear(day):00}";
    }

    /// <summary>
    /// 分解返还的**材料**数量（旧版 <c>FunGameConstant.DecomposedMaterials</c>，按品质固定值）。
    /// <para>1 普通 = 1、2 优秀 = 3、3 稀有 = 8、4 史诗 = 16、5 传说 = 32、6 神话 = 64、7 不朽 = 128；
    /// 旧版返还的是 <c>Inventory.Materials</c>（即「钻石」），不是金币。</para>
    /// <para><b>坑</b>：<paramref name="quality"/> 是「品质档位」1–7（White=1 … Gold=7），
    /// 不是 <see cref="QualityType"/> 枚举值（White=0 … Gold=6）。传枚举值会整体错一档：
    /// Green 只返 1 个而应是 3 个。调用方传 <c>PlayerItem.Quality</c>（已是档位）或
    /// <see cref="QualityTierOf"/> 的结果。</para>
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

    // ---- 旧版对齐：钻石兑换金币（ExchangeCredits）----
    // 旧版：reduce = max(10, 请求值)，再向下取整到 10 的倍数；reward = reduce / 10 * 2000。
    // 帮助文案写的是「1 钻石 = 200 金币」，实际兑换按 10 钻一档结算。
    public const int ExchangeMaterialUnit = 10;
    public const int ExchangeCreditsPerUnit = 2_000;

    // ---- 旧版对齐：魔法卡（UseMagicCard / ConflateMagicCardPack）----
    /// <summary>一个魔法卡包最多容纳的魔法数量。</summary>
    public const int MagicCardPackMaxMagics = 3;

    /// <summary>魔法等级溢出时的补偿：每溢出 1 级补 10 钻石（旧版写了这句，但 diff 恒为 0 的死代码）。</summary>
    public const int MagicCardLevelCompensationMaterials = 10;

    /// <summary>
    /// 钻石 → 金币的兑换方案：先把请求值抬到最低 10、再向下取整到 10 的倍数，然后按 10 钻 = 2000 金币折算。
    /// <para>与旧版 <c>ExchangeCredits</c> 同口径：请求 0/7 → 10，请求 25 → 20，请求 100 → 100。</para>
    /// </summary>
    public static (int Materials, long Credits) ExchangePlan(int requested)
    {
        int reduce = Math.Max(ExchangeMaterialUnit, requested);
        reduce -= reduce % ExchangeMaterialUnit;
        return (reduce, (long)reduce / ExchangeMaterialUnit * ExchangeCreditsPerUnit);
    }

    /// <summary>
    /// 把可注入随机源适配成 <see cref="Random"/>——内容包里的魔法卡生成器（<c>GenerateMagicCard</c>、
    /// <c>ConflateMagicCardPack</c>）签名要求 <see cref="Random"/>，这里用一次抽样做种子，
    /// 保证 <see cref="SeededRandomSource"/> 下仍可复现。
    /// </summary>
    public static Random NewRandom(IRandomSource source) => new(source.Next(int.MaxValue));

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

    /// <summary>
    /// 技能升级需求（照抄旧版 <c>FunGameConstant.SkillLevelUpList</c>，键是**目标等级** 1–6）。
    /// <para>旧表里「角色等级」是前置，「金币/材料/物品」是消耗。</para>
    /// </summary>
    public static UpgradeNeed? SkillUpgradeCost(int targetLevel) => targetLevel switch
    {
        1 => new(1, -1, 5_000, 50, [("技能卷轴", 1)]),
        2 => new(12, -1, 10_000, 100, [("技能卷轴", 2), ("智慧之果", 1)]),
        3 => new(24, -1, 20_000, 150, [("技能卷轴", 4), ("智慧之果", 2), ("奥术符文", 1)]),
        4 => new(36, -1, 40_000, 250, [("技能卷轴", 8), ("智慧之果", 4), ("奥术符文", 3), ("混沌之核", 1)]),
        5 => new(48, -1, 70_000, 400,
            [("技能卷轴", 16), ("智慧之果", 8), ("奥术符文", 6), ("混沌之核", 4), ("法则精粹", 1)]),
        6 => new(60, -1, 110_000, 600,
            [("技能卷轴", 32), ("智慧之果", 16), ("奥术符文", 12), ("混沌之核", 8), ("法则精粹", 5)]),
        _ => null
    };

    /// <summary>
    /// 普攻升级需求（照抄旧版 <c>FunGameConstant.NormalAttackLevelUpList</c>，键是**目标等级** 2–8）。
    /// <para>6 级额外要求「角色突破进度 ≥ 4」。</para>
    /// </summary>
    public static UpgradeNeed? NormalAttackUpgradeCost(int targetLevel) => targetLevel switch
    {
        2 => new(8, -1, 2_000, 30, [("技能卷轴", 1)]),
        3 => new(16, -1, 10_000, 100, [("技能卷轴", 2), ("智慧之果", 1)]),
        4 => new(24, -1, 20_000, 150, [("技能卷轴", 4), ("智慧之果", 2), ("奥术符文", 1)]),
        5 => new(32, -1, 40_000, 250, [("技能卷轴", 8), ("智慧之果", 4), ("奥术符文", 3), ("混沌之核", 1)]),
        6 => new(40, 4, 70_000, 400,
            [("技能卷轴", 16), ("智慧之果", 8), ("奥术符文", 6), ("混沌之核", 4), ("法则精粹", 1)]),
        7 => new(48, -1, 110_000, 600,
            [("技能卷轴", 32), ("智慧之果", 16), ("奥术符文", 12), ("混沌之核", 8), ("法则精粹", 5)]),
        8 => new(56, -1, 160_000, 850,
            [("技能卷轴", 64), ("智慧之果", 32), ("奥术符文", 24), ("混沌之核", 16), ("法则精粹", 10)]),
        _ => null
    };

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
    /// 签到额外物品的品质（旧版 <c>GetSignInResult</c> 里那段「按签到天数调整概率」）。
    /// <para>旧版给每一档概率统一加 <c>min(days * 0.03, 30)</c> 个百分点，再从高品到低品比阈值；
    /// 因为每档加的是同一个常数，等价于把判定阈值整体上移，这里按同一语义实现。</para>
    /// </summary>
    public static int RollSignInQuality(IRandomSource random, int streakDays)
    {
        double q = random.NextDouble() * 100;
        double factor = Math.Min(Math.Max(0, streakDays) * 0.03, 30);
        foreach ((int tier, double percent) in GachaQualityWeights)
        {
            if (q <= percent + factor) return tier;
        }
        return 1;
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

    /// <summary>
    /// 旧版抽卡/签到掉落「橙品以上自动锁定」（<c>if (quality &gt;= QualityType.Orange) item.IsLock = true</c>），
    /// 防止误分解。抽卡与签到共用这一条规则。
    /// </summary>
    public static bool AutoLockOnDraw(QualityType quality) => quality >= QualityType.Orange;

    /// <summary>旧版 <c>ItemCanNotDrawCard</c>：这些类型不进抽卡池。</summary>
    public static bool IsDrawable(Item item) => item.ItemType is not (
        ItemType.Collectible or ItemType.QuestItem or ItemType.GiftBox or ItemType.Others);

    /// <summary>旧版抽卡命中阈值：<c>NextDouble() &gt; 0.8</c> 才出东西，否则「什么也没抽中」。</summary>
    /// <summary>
    /// 物品回收价区间（旧版 <c>FunGameConstant.PriceRanges</c>）：白色 200–800 … 金色 13 万–24 万。
    /// </summary>
    public static readonly IReadOnlyDictionary<QualityType, (int Min, int Max)> ItemPriceRanges =
        new Dictionary<QualityType, (int Min, int Max)>
        {
            [QualityType.White] = (200, 800),
            [QualityType.Green] = (800, 4200),
            [QualityType.Blue] = (4200, 11000),
            [QualityType.Purple] = (11000, 32200),
            [QualityType.Orange] = (32200, 67000),
            [QualityType.Red] = (67000, 130000),
            [QualityType.Gold] = (130000, 240000)
        };

    /// <summary>
    /// 入包时给物品定回收价（旧版 <c>AddItemToUserInventory</c>：<c>Next(min,max) * 0.35</c>）。
    /// <para>这里取区间中值 × 0.35：旧版每件随机浮动，改成中值后同品质物品同价，
    /// 便于对账与回归测试；量级与旧版一致。</para>
    /// </summary>
    public static double DefaultItemPrice(QualityType quality)
        => ItemPriceRanges.TryGetValue(quality, out (int Min, int Max) range)
            ? (range.Min + range.Max) / 2.0 * 0.35
            : 0;

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

    /// <summary>
    /// 商店下单窗口（分钟）。旧版 <c>UserLastVisitStore</c> 的 2 分钟有效期：
    /// 商店用序号指代，容易连着看几家后买错，所以超时要求重新打开。
    /// </summary>
    public const int StoreWindowMinutes = 2;

    // 锻造的数值与配方规则集中在 ForgeRules（按材料所属地区折算），这里不再重复定义。

    /// <summary>自建角色重随一次消耗的钻石（旧版 <c>reduce = 3</c>，扣的是 InGameMaterial）。</summary>
    public const int CustomRerollCost = 3;

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

    // 旧版 FightInstance 的奖励区间（每名出战角色各掷一次，再乘难度）。
    public const int InstanceGoldMin = 550;
    public const int InstanceGoldMax = 1_050;
    public const int InstanceMaterialMin = 3;
    public const int InstanceMaterialMax = 7;
    public const int InstanceExpMin = 570;
    public const int InstanceExpMax = 1_260;

    /// <summary>
    /// 按类型与难度结算奖励（旧版 <c>FightInstance</c>）：**每名出战角色各掷一次**再乘难度，
    /// 所以带满 4 人小队的收益是单人的约 4 倍——这是旧版「多带人多拿」的设计，不是 bug。
    /// </summary>
    public static (long Exp, long Credits, long Diamonds, int Items) InstanceReward(
        InstanceKind kind, int difficulty, int members, IRandomSource random)
    {
        int d = ClampDifficulty(difficulty);
        int count = Math.Max(1, members);
        long roll(int min, int max)
        {
            long total = 0;
            for (int i = 0; i < count; i++) total += random.Next(min, max);
            return total * d;
        }

        return kind switch
        {
            InstanceKind.Gold => (0, roll(InstanceGoldMin, InstanceGoldMax), 0, 0),
            InstanceKind.Material => (0, 0, roll(InstanceMaterialMin, InstanceMaterialMax), 0),
            InstanceKind.Exp => (roll(InstanceExpMin, InstanceExpMax), 0, 0, 0),
            InstanceKind.Region => (100L * d * count, 0, 0, d),
            InstanceKind.Break => (0, 0, 0, d),
            InstanceKind.Skill => (0, 0, 0, d),
            _ => (0, 0, 0, 1)
        };
    }

    /// <summary>结算前给玩家看的区间：单人每档的下限–上限 × 难度，实际按出战人数叠乘。</summary>
    public static string InstanceRewardHint(InstanceKind kind, int difficulty)
    {
        int d = ClampDifficulty(difficulty);
        return kind switch
        {
            InstanceKind.Gold => $"每人 {InstanceGoldMin}-{InstanceGoldMax - 1} 金币 × 难度",
            InstanceKind.Material => $"每人 {InstanceMaterialMin}-{InstanceMaterialMax - 1} 钻石 × 难度",
            InstanceKind.Exp => $"每人 {InstanceExpMin}-{InstanceExpMax - 1} 经验 × 难度",
            InstanceKind.Region => $"{100 * d} 经验 + {d} 件地区材料",
            InstanceKind.Break => $"{d} 件突破材料",
            InstanceKind.Skill => $"{d} 件技能材料",
            _ => "1 张魔法卡"
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

    /// <summary>
    /// 上架费：<c>标价 × 15%</c>，**不取整、不设下限**（2026-11-14 决定）。
    /// <para>标价 1 金币时费用是 0.15，和旧版 <c>fee = amount * 0.15</c> 一样保留小数；
    /// 只在显示时四舍五入到两位。之所以不再 <c>Math.Max(1, ...)</c>：那会让 1 金币的白装
    /// 收 100% 的手续费，低于 7 金币的挂单实际是亏本卖。</para>
    /// </summary>
    public static double MarketFee(long price) => price * MarketFeeRatePercent / 100.0;

    /// <summary>
    /// 金币 / 钻石的展示格式：固定两位小数、四舍五入，**只影响显示**。
    /// <para>内部一律保留 <see cref="double"/> 原值（手续费 19 × 15% = 2.85、
    /// 累加后可能出现 30.000000000000004 这种尾差），不在这里落库取整。</para>
    /// </summary>
    public const string MoneyFormat = "0.00";

    public static string Money(double amount) => amount.ToString(MoneyFormat, CultureInfo.InvariantCulture);

    public static string RankingBoardName(string board) => board.ToLowerInvariant() switch
    {
        "level" or "等级" => "等级榜",
        "points" or "战绩" => "战绩榜",
        "horse" or "赛马" or "赛马积分" => "赛马积分榜",
        "coop" or "共斗" or "共斗积分" => "共斗积分榜",
        "forge" or "锻造" or "锻造积分" => "锻造积分榜",
        _ => "金币榜"
    };

    public static string CatalogVersion => "core-v3";
}

/// <summary>
/// 一条突破材料需求。<see cref="IsMaterial"/> 为 true 时按钱包材料（<c>Materials</c>）结算，
/// 否则按背包里同名物品的数量结算。
/// </summary>
public sealed record BreakNeed(string Name, int Count, bool IsMaterial);

/// <param name="RequiredLevel">角色等级前置。</param>
/// <param name="RequiredBreak">角色突破进度前置；-1 表示无要求。</param>
/// <param name="Credits">消耗金币。</param>
/// <param name="Materials">消耗材料（钻石）。</param>
/// <param name="Items">消耗物品（名字 + 数量）。</param>
public sealed record UpgradeNeed(
    int RequiredLevel, int RequiredBreak, long Credits, int Materials,
    IReadOnlyList<(string Name, int Count)> Items);
