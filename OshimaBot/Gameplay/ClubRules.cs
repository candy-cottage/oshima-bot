namespace OshimaBot.Gameplay;

/// <summary>社团周常任务的推进来源。</summary>
public enum ClubTaskKind
{
    /// <summary>成员捐献金币（/社团捐献）。</summary>
    Donate,
    /// <summary>新成员加入（含审批通过）。</summary>
    Recruit,
    /// <summary>成员提交物资（/社团任务 提交）。</summary>
    Collect
}

/// <param name="Target">目标数量（捐献按金币、招募按人数、征集按件数）。</param>
/// <param name="RewardFunds">完成后一次性进社团基金的数额。</param>
public sealed record ClubTaskSpec(
    string Key, ClubTaskKind Kind, string Name, string Unit, int Target, int RewardFunds, string Hint);

/// <summary>社团商店的商品：用社团基金兑换给成员个人。</summary>
public sealed record ClubStoreGoodSpec(string Name, string CatalogId, long Price, string Description);

/// <summary>
/// 社团玩法规则（旧版 <c>Club</c> 只有成员与捐献，任务/商店/市场是 P3 规划里没落地的部分）。
/// <para>三者串成一条闭环：捐献与市场手续费 → 社团基金 → 商店兑换成个人物资 → 任务目标推进。</para>
/// </summary>
public static class ClubRules
{
    // ---- 周常任务 ----

    /// <summary>每周三个任务，换周自动从 0 计。</summary>
    public static readonly IReadOnlyList<ClubTaskSpec> Tasks =
    [
        new("donate", ClubTaskKind.Donate, "社团捐献", "金币", 5_000, 300,
            "成员用 /社团捐献 投入的金币累计"),
        new("recruit", ClubTaskKind.Recruit, "招募新血", "人", 3, 400,
            "本周新加入的成员数（审批通过与邀请加入都算）"),
        new("collect", ClubTaskKind.Collect, "物资征集", "件", 20, 500,
            "成员用 /社团任务 提交 交进社团的物品件数")
    ];

    public static ClubTaskSpec? FindTask(string key)
        => Tasks.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    // ---- 社团商店 ----

    /// <summary>基金兑换清单：价格按「1 金币 = 1 基金」定，比钻石直接换金币略贵一点作为便利费。</summary>
    public static readonly IReadOnlyList<ClubStoreGoodSpec> Goods =
    [
        new("钻石", "currency:diamonds", 260, "可直接用于抽卡与突破"),
        new("升华之印", "18001", 800, "突破材料（0–1 阶）"),
        new("流光之印", "18002", 1_600, "突破材料（1–2 阶）"),
        new("永恒之印", "18003", 2_400, "突破材料（2–4 阶）"),
        new("原初之印", "18011", 3_200, "突破材料（4–5 阶）"),
        new("时霜药剂", "", 600, "锻造材料")
    ];

    /// <summary>出售给社团商店时，每 1 份分解材料折算多少金币（分解价的一半：1 钻石 ≈ 200 金币）。</summary>
    public const int BuybackCreditsPerMaterial = 100;

    /// <summary>一件物品卖给社团商店能换多少金币（由社团基金支付）。</summary>
    public static long BuybackCredits(int quality, int quantity)
        => GameplayRules.SalvageMaterials(quality, quantity) * BuybackCreditsPerMaterial;

    /// <summary>提交物资时每件折算的基金（征集任务用，略低于卖店价，避免「刷任务」）。</summary>
    public static long CollectFunds(int quality) => Math.Max(1, GameplayRules.SalvageMaterials(quality, 1) / 2);

    // ---- 社团市场 ----

    /// <summary>社团市场手续费进社团基金，比全局市场的 15% 低。</summary>
    public const long MarketFeeRatePercent = 5;

    public const long MinPrice = GameplayRules.MarketMinPrice;
    public const long MaxPrice = GameplayRules.MarketMaxPrice;

    /// <summary>每人同时挂单上限（比全局市场的 5 件更严，社团市场是熟人交易）。</summary>
    public const int MaxListingsPerUser = 3;

    /// <summary>单个社团在售挂单上限，防止列表刷爆。</summary>
    public const int MaxListingsPerClub = 60;

    /// <summary>上架费：<c>标价 × 5%</c>，与全局市场同口径——不取整、不设下限，只在显示时保留两位。</summary>
    public static double MarketFee(long price) => price * MarketFeeRatePercent / 100.0;

    public static string KindName(ClubTaskKind kind) => kind switch
    {
        ClubTaskKind.Donate => "捐献",
        ClubTaskKind.Recruit => "招募",
        _ => "征集"
    };

    /// <summary>品质名沿用 <see cref="GameplayRules.QualityName"/>，商店与市场共用。</summary>
    public static string QualityName(int quality) => GameplayRules.QualityName(quality);

    /// <summary>商品是否为「货币型」（购买后直接进钱包而不是背包）。</summary>
    public static bool IsCurrencyGood(ClubStoreGoodSpec good)
        => good.CatalogId.StartsWith("currency:", StringComparison.Ordinal);
}
