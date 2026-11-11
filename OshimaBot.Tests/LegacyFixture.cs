using System.Text.Json;
using System.Text.Json.Nodes;
using FunGame.Core.Library.Constant;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;

namespace OshimaBot.Tests;

/// <summary>
/// M0 行为等价 Golden Fixture。
/// <para>设计要点：<c>expected.current</c> 锁定当前实现的实际数值，用于防回归；
/// <c>expected.legacy</c> 存旧版数值，为 null 表示尚未从旧版 Oracle 提取。
/// 两者都填且不一致时，必须在矩阵里登记为 <c>intentional-divergence</c>。</para>
/// </summary>
public sealed class LegacyFixture
{
    public required string Id { get; init; }
    public required string BehaviorId { get; init; }
    public required string Command { get; init; }
    public required string Probe { get; init; }
    public required string Status { get; init; }
    public required JsonElement Input { get; init; }
    public required LegacyExpectation Expected { get; init; }
    public required LegacySource Source { get; init; }
    public string? LegacyBaseline { get; init; }
    public string? CatalogVersion { get; init; }
    public int? Seed { get; init; }
    public string? Now { get; init; }
}

public sealed class LegacyExpectation
{
    public JsonNode? Current { get; init; }
    public JsonNode? Legacy { get; init; }
}

public sealed class LegacySource
{
    public required string File { get; init; }
    public string? Method { get; init; }
    public string? Note { get; init; }
}

public static class LegacyFixtureLoader
{
    public const string StatusCurrentLocked = "current-locked";
    public const string StatusLegacyVerified = "legacy-verified";
    public const string StatusIntentionalDivergence = "intentional-divergence";

    public static readonly IReadOnlySet<string> KnownStatuses = new HashSet<string>
    {
        StatusCurrentLocked, StatusLegacyVerified, StatusIntentionalDivergence
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string FixtureDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Legacy");

    /// <summary>加载全部 fixture，按 Id 排序保证测试稳定。</summary>
    public static IReadOnlyList<LegacyFixture> LoadAll()
    {
        string dir = FixtureDirectory;
        if (!Directory.Exists(dir)) return [];

        List<LegacyFixture> result = [];
        foreach (string file in Directory.EnumerateFiles(dir, "*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            LegacyFixture? fixture = JsonSerializer.Deserialize<LegacyFixture>(File.ReadAllText(file), Options);
            if (fixture is null) throw new InvalidOperationException($"无法解析 fixture：{file}");
            result.Add(fixture);
        }
        return result.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// 探针：把领域规则的实际输出转成 JSON，与 fixture 的 <c>expected.current</c> 逐字段比较。
/// 显式 switch（不做反射扫描），新增探针必须同时改这里与 <see cref="KnownProbes"/>。
/// </summary>
public static class LegacyFixtureProbes
{
    public static readonly IReadOnlySet<string> KnownProbes = new HashSet<string>
    {
        "levelup", "break", "train", "register", "gacha",
        "forge", "store", "market-fee", "boss", "signin"
    };

    public static JsonNode Run(string probe, JsonElement input)
    {
        // 内容包常量（InGameMaterial 等）由模块在启动时赋值，探针必须与生产一致的取值。
        FunGameRuntime.EnsureInitialized();

        return probe switch
        {
            "levelup" => LevelUp(input),
            "break" => Break(input),
            "train" => Train(input),
            "register" => Register(),
            "gacha" => Gacha(input),
            "forge" => Forge(),
            "store" => Store(),
            "market-fee" => MarketFee(input),
            "boss" => Boss(),
            "signin" => SignIn(input),
            _ => throw new NotSupportedException($"未注册的探针：{probe}")
        };
    }

    private static int[] IntArray(JsonElement input, string name)
        => input.TryGetProperty(name, out JsonElement value)
            ? value.EnumerateArray().Select(x => x.GetInt32()).ToArray()
            : [];

    private static JsonObject LevelUp(JsonElement input)
    {
        int[] levels = IntArray(input, "levels");
        return new JsonObject
        {
            ["expForLevel"] = new JsonArray(levels.Select(l => (JsonNode?)JsonValue.Create(
                General.GameplayEquilibriumConstant.EXPUpperLimit.TryGetValue(l, out double need) ? need : 0)).ToArray())
        };
    }

    private static JsonObject Break(JsonElement input)
    {
        int[] breaks = IntArray(input, "breakLevels");
        return new JsonObject
        {
            ["levelCap"] = new JsonArray(breaks.Select(b => (JsonNode?)JsonValue.Create(
                GameplayRules.NextBreakLevel(b) ?? General.GameplayEquilibriumConstant.MaxLevel)).ToArray()),
            // 当前 b 阶「下一次突破」需要什么（Core 的 LevelBreak 从 -1 起算，突破到 b+1 阶取 b+1 档）。
            ["breakNeedy"] = new JsonArray(breaks.Select(b => (JsonNode?)JsonValue.Create(
                string.Join(",", GameplayRules.LevelBreakNeedy(b + 1).Select(x => $"{x.Name}×{x.Count}")))).ToArray())
        };
    }

    private static JsonObject Train(JsonElement input)
    {
        // 旧版练级只看时长，与等级无关；输入是分钟数。
        int[] minutes = input.TryGetProperty("minutes", out JsonElement m)
            ? m.EnumerateArray().Select(x => x.GetInt32()).ToArray()
            : IntArray(input, "minutes");
        return new JsonObject
        {
            ["costCredits"] = GameplayRules.TrainCostCredits,
            ["defaultMinutes"] = GameplayRules.TrainDefaultMinutes,
            ["maxMinutes"] = GameplayRules.TrainMaxMinutes,
            ["expPerMinute"] = GameplayRules.TrainExpPerMinute,
            // 旧版 GetTrainingInfo：总经验 = min(分钟数, 2880) × 2，与角色等级无关
            ["expByMinutes"] = new JsonArray(minutes
                .Select(m => (JsonNode?)JsonValue.Create(
                    GameplayRules.TrainExpPerMinute * (long)Math.Min(m, GameplayRules.TrainMaxMinutes)))
                .ToArray())
        };
    }

    private static JsonObject Register() => new()
    {
        ["credits"] = GameplayRules.StartingCredits,
        ["diamonds"] = GameplayRules.StartingDiamonds,
        ["items"] = GameplayRules.StartingItems
    };

    private static JsonObject Gacha(JsonElement input)
    {
        int[] counts = IntArray(input, "counts");
        return new JsonObject
        {
            ["singleCost"] = GameplayRules.GachaCost,
            ["tenCost"] = GameplayRules.GachaTenCost,
            ["maxPerCall"] = GameplayRules.MaxGachaPerCall,
            // 与 PlayerService.GachaAsync 的计费分支保持一致
            ["costByCount"] = new JsonArray(counts
                .Select(c => (JsonNode?)JsonValue.Create(c == 10 ? GameplayRules.GachaTenCost : GameplayRules.GachaCost * c))
                .ToArray()),
            ["singleCostCredits"] = GameplayRules.GachaCostCredits,
            ["tenCostCredits"] = GameplayRules.GachaTenCostCredits,
            // 命中率 20%（旧版 dice > 0.8）；用整数百分比避免浮点尾数
            ["hitChancePercent"] = 20,
            // 旧版无保底：十连可以全空
            ["pityQuality"] = null,
            ["qualityThresholds"] = new JsonArray(GameplayRules.GachaQualityWeights
                .Select(w => (JsonNode?)JsonValue.Create(w.Percent)).ToArray())
        };
    }

    private static JsonObject Forge() => new()
    {
        ["costCredits"] = GameplayRules.ForgeCostCredits,
        ["materialQuantity"] = GameplayRules.ForgeMaterialQuantity,
        ["defaultMinutes"] = GameplayRules.ForgeDefaultMinutes,
        ["maxMinutes"] = GameplayRules.ForgeMaxMinutes,
        ["outputQuality"] = GameplayRules.ForgeOutputQuality
    };

    private static JsonObject Store()
    {
        // 旧版 7 个固定商店的价格表 + 每日商店的件数与刷新时刻
        JsonObject stores = [];
        foreach (StoreSpec spec in StoreCatalog.Stores)
        {
            stores[spec.Key] = new JsonArray(spec.Goods
                .Select(g => (JsonNode?)JsonValue.Create(g.Price)).ToArray());
        }
        return new JsonObject
        {
            ["stores"] = stores,
            ["dailyCount"] = StoreCatalog.DailyGoodsCount,
            ["dailyRefreshHour"] = StoreCatalog.DailyRefreshHour
        };
    }

    private static JsonObject MarketFee(JsonElement input)
    {
        long[] prices = input.TryGetProperty("prices", out JsonElement value)
            ? value.EnumerateArray().Select(x => x.GetInt64()).ToArray()
            : [];
        return new JsonObject
        {
            ["feeRatePercent"] = GameplayRules.MarketFeeRatePercent,
            ["minPrice"] = GameplayRules.MarketMinPrice,
            ["maxPrice"] = GameplayRules.MarketMaxPrice,
            ["maxListings"] = GameplayRules.MarketMaxListingsPerUser,
            ["fee"] = new JsonArray(prices.Select(p => (JsonNode?)JsonValue.Create(GameplayRules.MarketFee(p))).ToArray())
        };
    }

    private static JsonObject Boss() => new()
    {
        ["costCredits"] = GameplayRules.BossCostCredits,
        ["rewardCredits"] = GameplayRules.BossRewardCredits,
        // 旧版击杀奖励 = Materials += boss.Level（Boss 池等级 15/30/60）
        ["killMaterialEqualsLevel"] = true,
        ["bossLevels"] = new JsonArray(
            JsonValue.Create(15), JsonValue.Create(30), JsonValue.Create(60)),
        ["winPoints"] = GameplayRules.BossWinPoints
    };

    private static JsonObject SignIn(JsonElement input)
    {
        // 输入是「签到后的连续天数」（从 1 开始）；实现里扣 1 后进 SignInReward。
        // 用「永远取下限」的随机源，把随机区间压成确定值，便于锁定。
        int[] streaks = IntArray(input, "streaks");
        MinRandomSource random = new();
        List<long> credits = [];
        List<int> materials = [];
        foreach (int streak in streaks)
        {
            (long c, int m) = GameplayRules.SignInReward(random, streak - 1);
            credits.Add(c);
            materials.Add(m);
        }
        return new JsonObject
        {
            ["creditsByStreak"] = new JsonArray(credits.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
            ["materialsByStreak"] = new JsonArray(materials.Select(m => (JsonNode?)JsonValue.Create(m)).ToArray()),
            ["creditsRange"] = new JsonArray(
                JsonValue.Create(GameplayRules.SignInCreditsMin),
                JsonValue.Create(GameplayRules.SignInCreditsMax - 1)),
            ["materialRange"] = new JsonArray(
                JsonValue.Create(GameplayRules.SignInMaterialMin),
                JsonValue.Create(GameplayRules.SignInMaterialMax - 1))
        };
    }

    /// <summary>永远返回区间下限的随机源，用于把随机奖励压成确定值。</summary>
    private sealed class MinRandomSource : IRandomSource
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => 0;
    }
}
