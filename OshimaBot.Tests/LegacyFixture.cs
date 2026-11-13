using System.Text.Json;
using System.Text.Json.Nodes;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Regions;
using OshimaBot.Game.Combat;
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

    /// <summary>
    /// 旧值比较策略。<c>current-locked</c> 可为 null；其余状态必填，否则测试失败。
    /// <para><c>fields</c> 列出必须逐字段比较的顶层键，避免整棵树 <c>DeepEquals</c>
    /// 把「旧版没有的字段」误判成差异。</para>
    /// </summary>
    public LegacyMatchPolicy? LegacyMatchPolicy { get; init; }

    /// <summary>有意差异登记，<c>intentional-divergence</c> 必填。</summary>
    public LegacyDivergence? Divergence { get; init; }

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

/// <summary>旧值与当前值的字段级比较策略。</summary>
public sealed class LegacyMatchPolicy
{
    /// <summary>精确相等，或按绝对误差比较。</summary>
    public const string ModeExact = "exact";
    public const string ModeTolerance = "tolerance";

    public string Mode { get; init; } = ModeExact;

    /// <summary>参与比较的顶层字段名。</summary>
    public IReadOnlyList<string> Fields { get; init; } = [];

    /// <summary>tolerance 模式下的绝对误差，默认 1e-6。</summary>
    public double? Tolerance { get; init; }
}

/// <summary>已登记的有意差异：旧行为、新行为与决策来源。</summary>
public sealed class LegacyDivergence
{
    public required string Reason { get; init; }
    public string? DecidedBy { get; init; }
    public string? DecidedOn { get; init; }

    /// <summary>必须确实不等的字段；任一侧没有该字段也算不等。</summary>
    public IReadOnlyList<string> Fields { get; init; } = [];
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
/// 字段级旧值比较器。
/// <para>只比较 <see cref="LegacyMatchPolicy.Fields"/> 列出的顶层字段：旧版往往没有新版新增的
/// 字段（如 <c>maxListings</c>），整树 <c>DeepEquals</c> 会把「旧版没有」误报成「数值不同」。</para>
/// </summary>
public static class LegacyFieldComparer
{
    private const double DefaultTolerance = 1e-6;

    /// <summary>返回第一处差异的可读描述；全部一致时返回 null。</summary>
    public static string? FirstDifference(JsonObject current, JsonObject legacy, LegacyMatchPolicy policy)
    {
        bool tolerance = string.Equals(policy.Mode, LegacyMatchPolicy.ModeTolerance, StringComparison.Ordinal);
        double allowed = policy.Tolerance ?? DefaultTolerance;

        foreach (string field in policy.Fields)
        {
            JsonNode? left = current.TryGetPropertyValue(field, out JsonNode? l) ? l : null;
            JsonNode? right = legacy.TryGetPropertyValue(field, out JsonNode? r) ? r : null;

            if (!current.ContainsKey(field)) return $"当前值缺少字段 {field}";
            if (!legacy.ContainsKey(field)) return $"旧版值缺少字段 {field}";

            if (left is null && right is null) continue;

            if (tolerance && TryNumber(left, out double a) && TryNumber(right, out double b))
            {
                if (Math.Abs(a - b) > allowed)
                {
                    return $"{field} 超出容差：当前 {a}，旧版 {b}，容差 {allowed}";
                }
                continue;
            }

            if (!JsonNode.DeepEquals(left, right))
            {
                return $"{field} 不一致：当前 {Show(left)}，旧版 {Show(right)}";
            }
        }

        return null;
    }

    /// <summary>该字段在新旧之间是否确实不同（任一侧缺失也算不同）。</summary>
    public static bool Differs(JsonObject current, JsonObject legacy, string field)
    {
        bool inCurrent = current.ContainsKey(field);
        bool inLegacy = legacy.ContainsKey(field);
        if (!inCurrent && !inLegacy) return false;
        if (inCurrent != inLegacy) return true;

        JsonNode? left = current[field];
        JsonNode? right = legacy[field];
        if (left is null && right is null) return false;
        return !JsonNode.DeepEquals(left, right);
    }

    private static string Show(JsonNode? node) => node?.ToJsonString() ?? "null";

    private static bool TryNumber(JsonNode? node, out double value)
    {
        if (node is JsonValue json && json.TryGetValue(out double parsed))
        {
            value = parsed;
            return true;
        }
        value = 0;
        return false;
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
        "forge", "store", "market-fee", "boss", "signin", "reroll",
        "exchange", "salvage-quality", "magic-card", "explore-rules",
        "club-task", "club-store", "club-market", "horse-points", "coop-reward"
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
            "reroll" => Reroll(),
            "exchange" => Exchange(input),
            "salvage-quality" => SalvageQuality(),
            "magic-card" => MagicCard(),
            "explore-rules" => ExploreRulesFixture(),
            "club-task" => ClubTask(),
            "club-store" => ClubStore(),
            "club-market" => ClubMarket(),
            "horse-points" => HorsePoints(),
            "coop-reward" => CoopRewardFixture(),
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
            // 旧版 Next(8) 均匀落到 8 类产出；重复角色折算 50 材料；橙品以上抽到即锁
            ["kindCount"] = 8,
            ["duplicateCharacterRefund"] = GameplayRules.GachaDuplicateCharacterMaterials,
            ["autoLockFrom"] = GameplayRules.QualityName(
                GameplayRules.QualityTierOf(FunGame.Core.Library.Constant.QualityType.Orange)),
            ["qualityThresholds"] = new JsonArray(GameplayRules.GachaQualityWeights
                .Select(w => (JsonNode?)JsonValue.Create(w.Percent)).ToArray())
        };
    }

    private static JsonObject Forge() => new()
    {
        ["costCredits"] = ForgeRules.CostCredits,
        ["defaultMinutes"] = ForgeRules.DefaultMinutes,
        ["maxMinutes"] = ForgeRules.MaxMinutes,
        ["materialNeedyByQuality"] = new JsonObject(ForgeRules.Needy
            .OrderBy(x => x.Value)
            .Select(x => KeyValuePair.Create(x.Key.ToString(), (JsonNode?)JsonValue.Create(x.Value)))),
        ["regionCoefficientByRarity"] = new JsonObject(ForgeRules.Coefficients
            .OrderBy(x => x.Value)
            .Select(x => KeyValuePair.Create(x.Key.ToString(), (JsonNode?)JsonValue.Create(x.Value)))),
        ["generalPointsPerCount"] = ForgeRules.GeneralPointsPerCount,
        ["failPointsPerCount"] = ForgeRules.FailPointsPerCount
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
            // 旧版 fee = amount * 0.15（double，不取整、无下限）；新版同口径，只在显示时保留两位。
            ["minFee"] = 0,
            ["rounding"] = "不取整，仅显示两位",
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
        ["winPoints"] = GameplayRules.BossWinPoints,
        // 旧版 Bosses 是静态字典：全服共享、战损写回、击杀补池。新版 BossPool 同构，别再写成「每日一次抽象挑战」。
        ["sharedPool"] = true,
        ["poolSize"] = BossPool.PoolSize
    };

    /// <summary>
    /// 自建角色重随的取值契约（旧版 <c>OshimaModules/Characters/CustomCharacter.cs</c> 构造函数）：
    /// 生命/魔法/攻击/速度/回复是固定区间，三围与成长按「主属性 15–30、其余合计 31」分配，
    /// 成长值再除以 10 保留两位；钻一次 3 个材料。
    /// </summary>
    private static JsonObject Reroll() => new()
    {
        ["costMaterials"] = GameplayRules.CustomRerollCost,
        ["initialAtk"] = new JsonArray(15, 25),
        ["initialHp"] = new JsonArray(40, 85),
        ["initialMp"] = new JsonArray(20, 55),
        ["initialSpd"] = new JsonArray(220, 290),
        ["initialHr"] = new JsonArray(1, 5),
        ["initialMr"] = new JsonArray(1, 5),
        ["primaryAttributeRoll"] = new JsonArray(1, 3),
        // 重随调用时 primaryAttribute = None：三项从一个 31 的池子里分配，最后一项取「剩余 - 1」，
        // 所以三项合计恒为 30（留 1 点未分配，旧版如此，不补齐）。
        ["traitPool"] = 31,
        ["traitAllocated"] = 30,
        ["growthScale"] = 0.1
    };

    /// <summary>
    /// 钻石兑换金币（旧版 <c>ExchangeCredits</c>）：<c>reduce = max(10, 请求)</c>，
    /// 再向下取整到 10 的倍数，<c>reward = reduce / 10 * 2000</c>。
    /// </summary>
    private static JsonObject Exchange(JsonElement input)
    {
        int[] requests = IntArray(input, "requests");
        return new JsonObject
        {
            ["materialUnit"] = GameplayRules.ExchangeMaterialUnit,
            ["creditsPerUnit"] = GameplayRules.ExchangeCreditsPerUnit,
            ["plans"] = new JsonArray(requests
                .Select(r => (JsonNode?)new JsonArray(
                    JsonValue.Create(GameplayRules.ExchangePlan(r).Materials),
                    JsonValue.Create(GameplayRules.ExchangePlan(r).Credits)))
                .ToArray())
        };
    }

    /// <summary>品质分解返还（旧版 <c>FunGameConstant.DecomposedMaterials</c>，单位是材料/钻石）。</summary>
    private static JsonObject SalvageQuality()
    {
        JsonObject byQuality = [];
        foreach (QualityType quality in Enum.GetValues<QualityType>())
        {
            // 注意：SalvageMaterials 吃的是「品质档位」（White=1 … Gold=7），不是 QualityType 枚举值
            byQuality[quality.ToString()] = GameplayRules.SalvageMaterials(GameplayRules.QualityTierOf(quality), 1);
        }
        return new JsonObject
        {
            ["materialsByQuality"] = byQuality,
            ["materialsForThreeOfEach"] = new JsonObject(
                Enum.GetValues<QualityType>().Select(q =>
                    KeyValuePair.Create(q.ToString(),
                        (JsonNode?)JsonValue.Create(
                            GameplayRules.SalvageMaterials(GameplayRules.QualityTierOf(q), 3)))))
        };
    }

    /// <summary>
    /// 探索规则（旧版 <c>MaxExploreTimes</c> / <c>ExploreResultProbabilities</c> /
    /// <c>GenerateExploreModel</c>）：每日 60 许可、午晚餐各 +30、单次 2 分钟、最多 4 人；
    /// 结果概率按星级调整（General 降、Fight 升）。
    /// <para>概率表摊平成 <c>p{星}_{结果}</c> 这类顶层数值键，才能走数值容差比较
    /// （嵌套对象会退化成整树深比较，浮点尾差会被当成不相等）。</para>
    /// </summary>
    private static JsonObject ExploreRulesFixture()
    {
        JsonObject result = new()
        {
            ["dailyQuota"] = ExploreRules.DailyQuota,
            ["mealBonus"] = ExploreRules.MealBonus,
            ["durationMinutes"] = ExploreRules.DurationMinutes,
            ["maxCharacters"] = ExploreRules.MaxCharacters
        };
        for (int stars = 1; stars <= 5; stars++)
        {
            foreach ((ExploreResult outcome, double probability) in ExploreRules.Probabilities(stars))
            {
                // 概率是加减出来的，0.45-0.35 会得到 0.09999999999999998；保留 4 位小数让输出稳定
                result[$"p{stars}_{outcome}"] = Math.Round(probability, 4);
            }
        }
        return result;
    }

    /// <summary>
    /// 社团周常任务（新版实现；旧版帮助里列了「社团任务列表/做社团任务/社团任务结算」但没有任何实现代码）。
    /// 因此这三行只锁当前值，状态是 <c>current-locked</c>。
    /// </summary>
    private static JsonObject ClubTask()
    {
        JsonArray tasks = [];
        foreach (ClubTaskSpec task in ClubRules.Tasks)
        {
            tasks.Add(new JsonObject
            {
                ["key"] = task.Key,
                ["kind"] = task.Kind.ToString(),
                ["target"] = task.Target,
                ["rewardFunds"] = task.RewardFunds
            });
        }
        return new JsonObject { ["tasks"] = tasks };
    }

    /// <summary>社团商店：基金兑换清单（新版实现，旧版无实现）。</summary>
    private static JsonObject ClubStore()
    {
        JsonArray goods = [];
        foreach (ClubStoreGoodSpec good in ClubRules.Goods)
        {
            goods.Add(new JsonObject
            {
                ["name"] = good.Name,
                ["catalogId"] = good.CatalogId,
                ["price"] = good.Price
            });
        }
        return new JsonObject
        {
            ["goods"] = goods,
            ["buybackCreditsPerMaterial"] = ClubRules.BuybackCreditsPerMaterial
        };
    }

    /// <summary>
    /// 社团市场（新版实现，旧版无实现）。旧版帮助文本写的是「系统手续费 4%，社团 8%」，
    /// 新版简化为「社团基金 5%、系统不抽成」——这一条记在 DIV-018。
    /// </summary>
    private static JsonObject ClubMarket() => new()
    {
        ["feeRatePercent"] = ClubRules.MarketFeeRatePercent,
        ["minPrice"] = ClubRules.MinPrice,
        ["maxPrice"] = ClubRules.MaxPrice,
        ["maxListingsPerUser"] = ClubRules.MaxListingsPerUser,
        ["maxListingsPerClub"] = ClubRules.MaxListingsPerClub,
        ["fee"] = new JsonArray(
            JsonValue.Create(ClubRules.MarketFee(100)),
            JsonValue.Create(ClubRules.MarketFee(1000)),
            JsonValue.Create(ClubRules.MarketFee(25_000)))
    };

    /// <summary>赛马名次积分（旧版 <c>CalculatePoints</c>：第 1 名 10 分，之后 ×0.8 向下取整，最低 1）。</summary>
    private static JsonObject HorsePoints()
    {
        JsonArray points = [];
        for (int rank = 0; rank <= 10; rank++) points.Add(JsonValue.Create(HorseRace.CalculatePoints(rank)));
        return new JsonObject { ["pointsByRank"] = points };
    }

    /// <summary>共斗奖励（旧版：全灭给「等级」钻石，未全灭按击杀比例折算）。</summary>
    private static JsonObject CoopRewardFixture()
    {
        JsonArray rows = [];
        foreach ((int level, int enemies, int killed) in new[]
                 {
                     (10, 4, 4), (10, 4, 3), (10, 4, 0), (25, 3, 1), (0, 4, 4)
                 })
        {
            rows.Add(new JsonArray(
                JsonValue.Create(level), JsonValue.Create(enemies), JsonValue.Create(killed),
                JsonValue.Create(RoomRules.CoopReward(level, enemies, killed))));
        }
        return new JsonObject { ["cases"] = rows };
    }

    /// <summary>魔法卡合成（旧版 <c>ConflateMagicCardPack</c>）：3 张未使用且未上锁的魔法卡换 1 个卡包。</summary>
    private static JsonObject MagicCard() => new()
    {
        ["craftNeeds"] = 3,
        ["packMaxMagics"] = GameplayRules.MagicCardPackMaxMagics,
        ["requiresUnused"] = true,
        ["rejectsLocked"] = true
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
