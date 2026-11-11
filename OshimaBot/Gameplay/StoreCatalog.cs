using FunGame.Core.Library.Constant;

namespace OshimaBot.Gameplay;

/// <summary>商店结算用的货币种类（旧版 <c>Goods.Prices</c> 的键）。</summary>
public enum StoreCurrency
{
    /// <summary>免费（旧版价格表为空，靠 <c>Quota</c> 限购）。</summary>
    Free,
    Credits,
    Diamonds,
    ForgePoints,
    HorseRacingPoints,
    CooperativePoints
}

public enum StoreKind
{
    /// <summary>每用户一个，4 件随机商品，每天 4:00 刷新。</summary>
    Daily,
    /// <summary>固定货架的常规商店（后勤部/武器商会/杂货铺/慈善基金会）。</summary>
    Regular,
    /// <summary>积分商店（锻造/赛马/共斗），货币是各自积分。</summary>
    Points
}

/// <param name="CatalogId">v3 内容包里的物品 Id；空串表示内容包没有同名物品，购买时按名称造占位物品。</param>
public sealed record StoreGoodSpec(
    string Name, string CatalogId, StoreCurrency Currency, long Price, int Stock, int Quota);

public sealed record StoreSpec(
    string Key, string Name, string Description, StoreKind Kind, IReadOnlyList<StoreGoodSpec> Goods);

/// <summary>
/// 旧版商店目录（照抄 <c>configs/stores/dokyo.json</c> 的 7 个商店、36 件商品）。
/// <para>`Stock = -1` 表示无限；`Quota = 0` 表示不限购。</para>
/// </summary>
public static class StoreCatalog
{
    public const string DailyKey = "daily";
    public const string LogisticsKey = "dokyo_logistics";
    public const string WeaponsKey = "dokyo_weapons";
    public const string YukiKey = "dokyo_yuki";
    public const string WelfareKey = "dokyo_welfare";
    public const string ForgeKey = "dokyo_forge";
    public const string HorseRacingKey = "dokyo_horseracing";
    public const string CooperativeKey = "dokyo_cooperative";

    /// <summary>旧版每日商店：4 件商品，1/3 概率是魔法卡，其余从抽卡池随机。</summary>
    public const int DailyGoodsCount = 4;

    /// <summary>旧版 <c>CheckDailyStore</c>：按 <c>DateTime.Today.AddHours(4)</c> 刷新。</summary>
    public const int DailyRefreshHour = 4;

    public static readonly IReadOnlyList<StoreSpec> Stores =
    [
        new(LogisticsKey, "探索者协会后勤部",
            "后勤部发布常驻探索许可，以及提供七折优惠的魔法卡礼包，欢迎广大探索者选购！",
            StoreKind.Regular,
            [
                new("探索许可", "", StoreCurrency.Diamonds, 2, 420, 0),
                new("普通魔法卡礼包", "20005", StoreCurrency.Credits, 1_500, -1, 0),
                new("优秀魔法卡礼包", "", StoreCurrency.Credits, 8_000, -1, 0),
                new("稀有魔法卡礼包", "", StoreCurrency.Credits, 20_000, -1, 0),
                new("史诗魔法卡礼包", "", StoreCurrency.Credits, 63_000, -1, 0),
                new("传说魔法卡礼包", "", StoreCurrency.Credits, 131_000, -1, 0),
                new("神话魔法卡礼包", "", StoreCurrency.Credits, 254_000, -1, 0),
                new("不朽魔法卡礼包", "", StoreCurrency.Credits, 470_000, -1, 0)
            ]),

        new(WeaponsKey, "铎京武器商会", "", StoreKind.Regular,
            [
                new("红雪剑·白雨刀", "11574", StoreCurrency.Credits, 120_000, 4, 0),
                new("金箍棒", "11572", StoreCurrency.Credits, 60_000, 4, 0),
                new("攻击之爪 +85", "14006", StoreCurrency.Credits, 100_000, 4, 0),
                new("攻击之爪 +70", "14005", StoreCurrency.Credits, 55_000, 4, 0)
            ]),

        new(YukiKey, "小雪杂货铺", "", StoreKind.Regular,
            [
                new("YukiのCalfSocks", "14510", StoreCurrency.Credits, 28_000, 20, 0),
                new("诺尔希的现代汉语字典", "", StoreCurrency.Credits, 28_000, 20, 0),
                new("金刚石", "14512", StoreCurrency.Credits, 28_000, 20, 0),
                new("路人哥的cos服", "14513", StoreCurrency.Credits, 28_000, 20, 0),
                new("白月光的玉佩", "14514", StoreCurrency.Credits, 28_000, 20, 0),
                new("改名卡", "18010", StoreCurrency.Diamonds, 300, 5, 0),
                new("YukiのCalfSocks-圣诞限定", "14515", StoreCurrency.Credits, 28_000, 10, 0)
            ]),

        new(WelfareKey, "铎京慈善基金会",
            "铎京慈善基金会是大陆上最大的慈善组织，致力于帮助贫困地区和弱势群体。探索者们可以不定期来这里看看有什么福利可以领取！",
            StoreKind.Regular,
            [
                new("探索助力礼包", "", StoreCurrency.Free, 0, -1, 1),
                new("元旦快乐", "20011", StoreCurrency.Free, 0, -1, 1),
                new("圣诞礼包", "20010", StoreCurrency.Free, 0, -1, 1)
            ]),

        new(ForgeKey, "锻造积分商店", "", StoreKind.Points,
            [
                new("大师锻造券", "18014", StoreCurrency.ForgePoints, 400, -1, 0),
                new("时霜药剂", "", StoreCurrency.ForgePoints, 10, -1, 0),
                new("晶化记忆孢子", "", StoreCurrency.ForgePoints, 10, -1, 0),
                new("神经宝石", "", StoreCurrency.ForgePoints, 6, -1, 0),
                new("深渊火钻", "", StoreCurrency.ForgePoints, 2, -1, 0),
                new("时凝液", "", StoreCurrency.ForgePoints, 8, -1, 0)
            ]),

        // 赛马积分换钻石：旧版把「钻石」当成一件商品卖，购买时直接进 Materials。
        new(HorseRacingKey, "赛马积分商店", "", StoreKind.Points,
            [
                new("钻石", "currency:diamonds", StoreCurrency.HorseRacingPoints, 5, -1, 300)
            ]),

        new(CooperativeKey, "共斗积分商店", "", StoreKind.Points,
            [
                new("奖券", "18008", StoreCurrency.CooperativePoints, 3, -1, 300),
                new("十连奖券", "18009", StoreCurrency.CooperativePoints, 25, -1, 100),
                new("流光之印", "18002", StoreCurrency.CooperativePoints, 8, -1, 0),
                new("混沌之核", "18007", StoreCurrency.CooperativePoints, 16, -1, 0),
                new("升华之印", "18001", StoreCurrency.CooperativePoints, 4, -1, 0),
                new("原初之印", "18011", StoreCurrency.CooperativePoints, 16, -1, 0),
                new("永恒之印", "18003", StoreCurrency.CooperativePoints, 12, -1, 0)
            ])
    ];

    public static StoreSpec? Find(string key)
        => Stores.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>把玩家输入的别名（商店1/后勤部/锻造商店…）解析成商店 key。</summary>
    public static string? ResolveAlias(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string normalized = text.Trim();
        return normalized switch
        {
            "1" or "商店1" or "后勤部" or "后勤" => LogisticsKey,
            "2" or "商店2" or "武器商会" or "武器店" => WeaponsKey,
            "3" or "商店3" or "杂货铺" or "杂货店" => YukiKey,
            "4" or "商店4" or "慈善基金会" or "基金会" or "慈善" => WelfareKey,
            "5" or "商店5" or "锻造商店" or "锻造积分商店" => ForgeKey,
            "6" or "商店6" or "赛马商店" or "赛马积分商店" => HorseRacingKey,
            "7" or "商店7" or "共斗商店" or "共斗积分商店" => CooperativeKey,
            _ => Stores.FirstOrDefault(x => x.Name.Contains(normalized, StringComparison.Ordinal))?.Key
        };
    }

    /// <summary>货币 → 玩家身上对应的余额读写。</summary>
    public static double GetBalance(StoreCurrency currency, PlayerAccount account) => currency switch
    {
        StoreCurrency.Credits => account.Credits,
        StoreCurrency.Diamonds => account.Materials,
        StoreCurrency.ForgePoints => account.ForgePoints,
        StoreCurrency.HorseRacingPoints => account.HorseRacingPoints,
        StoreCurrency.CooperativePoints => account.CooperativePoints,
        _ => 0
    };

    public static string CurrencyName(StoreCurrency currency) => currency switch
    {
        StoreCurrency.Credits => "金币",
        StoreCurrency.Diamonds => "钻石",
        StoreCurrency.ForgePoints => "锻造积分",
        StoreCurrency.HorseRacingPoints => "赛马积分",
        StoreCurrency.CooperativePoints => "共斗积分",
        _ => "免费"
    };

    /// <summary>旧版价格条目对应的 ledger 币种标记。</summary>
    public static string LedgerCurrency(StoreCurrency currency) => currency switch
    {
        StoreCurrency.Credits => "credits",
        StoreCurrency.Diamonds => "diamonds",
        StoreCurrency.ForgePoints => "forge_points",
        StoreCurrency.HorseRacingPoints => "horse_points",
        StoreCurrency.CooperativePoints => "coop_points",
        _ => "free"
    };

    /// <summary>品质档 → 旧版 <c>PriceRanges</c> 的价格区间（每日商店随机定价用）。</summary>
    public static (int Min, int Max) PriceRange(QualityType quality) => quality switch
    {
        QualityType.White => (200, 800),
        QualityType.Green => (800, 4_200),
        QualityType.Blue => (4_200, 11_000),
        QualityType.Purple => (11_000, 32_200),
        QualityType.Orange => (32_200, 67_000),
        QualityType.Red => (67_000, 130_000),
        _ => (130_000, 240_000)
    };
}
