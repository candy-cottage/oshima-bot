using System.Globalization;
using System.Text;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Regions;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;

namespace OshimaBot.Tests;

/// <summary>
/// 冻结种子回归场景：把「同一个种子必须算出同一组数」的玩法数值全部摊平成一行行文本，
/// 由 <see cref="SeededRegressionTests"/> 与 <c>Fixtures/Seeded/regression.json</c> 逐行比对。
/// <para>和现有测试的分工：其余测试断言**性质**（区间、上限、等价样例），这里断言**具体值**。
/// 性质测试放得过「暴击倍率从 1.5 改成 1.6」这种改动，冻结样本会当场指出来。</para>
/// <para>每个探针**自己**新建 <see cref="SeededRandomSource"/>，互不共享随机流：
/// 改其中一个探针的抽样次数不会让其它探针整体错位，失败信息才能指到真正变了的那一项。</para>
/// </summary>
internal static class SeedScenario
{
    /// <summary>基准种子取迁移开工日（2026-11-13），便于回忆这批数字是哪天的口径。</summary>
    public const int BaseSeed = 20261113;

    /// <summary>夹具路径（相对测试输出目录）。</summary>
    public const string FixtureRelativePath = "Fixtures/Seeded/regression.json";

    /// <summary>夹具格式版本：字段含义变了才 +1，好让旧夹具报「格式过旧」而不是逐行对不上。</summary>
    public const int SchemaVersion = 1;

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>跑完所有探针，返回 <c>探针 id → 值</c>（按 id 排序，保证序列化稳定）。</summary>
    public static SortedDictionary<string, string> Run()
    {
        FunGameRuntime.EnsureInitialized();

        SortedDictionary<string, string> probes = new(StringComparer.Ordinal);
        void Probe(string id, Func<string> body)
        {
            string value = body();
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"探针 {id} 返回了空值");
            probes[id] = value;
        }

        // ---- 抽卡：命中率 / 产出类型 / 品质档 ----
        Probe("gacha.hit-rate", () =>
        {
            SeededRandomSource random = new(BaseSeed);
            int hits = 0;
            for (int i = 0; i < 4000; i++)
            {
                if (GameplayRules.RollGachaHit(random)) hits++;
            }
            return Inv($"{hits}/4000");
        });

        Probe("gacha.kind-mix", () =>
        {
            SeededRandomSource random = new(BaseSeed + 1);
            int[] counts = new int[GameplayRules.GachaKindCount];
            for (int i = 0; i < 4000; i++) counts[(int)GameplayRules.RollGachaKind(random)]++;
            return string.Join("/", counts);
        });

        Probe("gacha.quality-mix", () =>
        {
            SeededRandomSource random = new(BaseSeed + 2);
            int[] counts = new int[8];
            for (int i = 0; i < 4000; i++) counts[GameplayRules.RollGachaQuality(random)]++;
            return string.Join("/", counts.Skip(1));
        });

        // 逐抽序列比分布更敏感：分布对得上、顺序错了（比如先掷品质再掷类型）也能被抓到。
        Probe("gacha.sequence", () =>
        {
            SeededRandomSource random = new(BaseSeed + 3);
            List<string> draws = [];
            for (int i = 0; i < 24; i++)
            {
                if (!GameplayRules.RollGachaHit(random))
                {
                    draws.Add("-");
                    continue;
                }
                int kind = (int)GameplayRules.RollGachaKind(random);
                int tier = GameplayRules.RollGachaQuality(random);
                draws.Add(Inv($"{kind}{tier}"));
            }
            return string.Join(" ", draws);
        });

        // ---- 签到：连续天数阶梯 + 天数加成的品质倾向 ----
        Probe("signin.reward-ladder", () =>
        {
            SeededRandomSource random = new(BaseSeed + 4);
            List<string> parts = [];
            for (int streak = 0; streak < 14; streak++)
            {
                (long credits, int materials) = GameplayRules.SignInReward(random, streak);
                parts.Add(Inv($"{streak}={credits}/{materials}"));
            }
            return string.Join(",", parts);
        });

        Probe("signin.quality-ladder", () =>
        {
            List<string> parts = [];
            foreach (int streak in new[] { 0, 7, 14, 30 })
            {
                SeededRandomSource random = new(BaseSeed + 100 + streak);
                int[] counts = new int[8];
                for (int i = 0; i < 2000; i++) counts[GameplayRules.RollSignInQuality(random, streak)]++;
                parts.Add(Inv($"{streak}:{string.Join("/", counts.Skip(1))}"));
            }
            return string.Join(",", parts);
        });

        // ---- 探索：不同星级的产出分布 + 收益公式 ----
        Probe("explore.mix", () =>
        {
            List<string> parts = [];
            for (int stars = 1; stars <= 5; stars++)
            {
                SeededRandomSource random = new(BaseSeed + 200 + stars);
                Dictionary<ExploreResult, int> counts = [];
                for (int i = 0; i < 1000; i++)
                {
                    ExploreResult result = ExploreRules.Roll(random, stars);
                    counts[result] = counts.GetValueOrDefault(result) + 1;
                }
                parts.Add(Inv($"{stars}:{counts.GetValueOrDefault(ExploreResult.General)}/{counts.GetValueOrDefault(ExploreResult.Nothing)}/{counts.GetValueOrDefault(ExploreResult.Fight)}/{counts.GetValueOrDefault(ExploreResult.Earned)}/{counts.GetValueOrDefault(ExploreResult.Event)}"));
            }
            return string.Join(",", parts);
        });

        Probe("explore.yields", () =>
        {
            List<string> parts = [];
            for (int stars = 1; stars <= 5; stars++)
            {
                for (int characters = 1; characters <= 4; characters++)
                {
                    parts.Add(Inv($"{stars}x{characters}={ExploreRules.Credits(stars, characters)}/{ExploreRules.Materials(stars, characters)}/{ExploreRules.BattleMaterials(stars, characters)}/{ExploreRules.EnemyCount(stars)}/{ExploreRules.Cost(characters, stars)}"));
                }
            }
            return string.Join(",", parts);
        });

        // ---- 秘境：7 类 × 5 难度 × 单人/满编 ----
        Probe("instance.reward", () =>
        {
            List<string> parts = [];
            foreach ((GameplayRules.InstanceKind kind, string name) in GameplayRules.InstanceKinds)
            {
                for (int difficulty = 1; difficulty <= 5; difficulty++)
                {
                    foreach (int members in new[] { 1, 4 })
                    {
                        SeededRandomSource random = new(BaseSeed + 300 + (int)kind * 10 + difficulty * 2 + (members == 1 ? 0 : 1));
                        (long exp, long credits, long diamonds, int items) =
                            GameplayRules.InstanceReward(kind, difficulty, members, random);
                        parts.Add(Inv($"{name}/{difficulty}/{(members == 1 ? "单" : "满")}={exp},{credits},{diamonds},{items}"));
                    }
                }
            }
            return string.Join(";", parts);
        });

        // ---- 市场：手续费与展示格式 ----
        Probe("market.fee", () =>
        {
            long[] prices = [1, 7, 19, 100, 9999, 1_000_000];
            return string.Join(",", prices.Select(price => Inv($"{price}={GameplayRules.MarketFee(price)}")));
        });

        Probe("market.money-format", () =>
        {
            double[] amounts = [0, 0.15, 2.85, 30.000_000_000_000_004, 1_000_000];
            return string.Join(",", amounts.Select(amount => Inv($"{GameplayRules.Money(amount)}")));
        });

        Probe("exchange.plan", () =>
        {
            int[] requested = [0, 7, 10, 25, 100, 105];
            return string.Join(",", requested.Select(value =>
            {
                (int materials, long credits) = GameplayRules.ExchangePlan(value);
                return Inv($"{value}→{materials}/{credits}");
            }));
        });

        Probe("salvage.materials", () =>
        {
            List<string> parts = [];
            for (int tier = 1; tier <= 7; tier++)
            {
                parts.Add(Inv($"{tier}x1={GameplayRules.SalvageMaterials(tier, 1)}") + "," +
                    Inv($"{tier}x3={GameplayRules.SalvageMaterials(tier, 3)}"));
            }
            return string.Join(",", parts);
        });

        // ---- 锻造：单一产地 / 混合产地 / 材料不足 ----
        Probe("forge.plan", () =>
        {
            static string Describe(ForgePlan plan) => plan.Ok
                ? Inv($"ok,{plan.EffectiveCount},{plan.Quality},{plan.RegionName},{plan.GeneralPoints:0.##},{plan.SuccessPoints:0.##},{plan.FailPoints:0.##}")
                : Inv($"fail,{plan.EffectiveCount},{plan.RegionId}");

            List<ForgeMaterial> single =
            [
                new("赤铁矿", 40, 1, "银辉城", RarityType.OneStar)
            ];
            List<ForgeMaterial> mixed =
            [
                new("赤铁矿", 40, 1, "银辉城", RarityType.OneStar),
                new("霜晶", 60, 2, "霜落峰", RarityType.ThreeStar)
            ];
            List<ForgeMaterial> short_ =
            [
                new("赤铁矿", 20, 1, "银辉城", RarityType.OneStar)
            ];

            return string.Join("|",
                Describe(ForgeRules.Plan(single, 0.5)),
                Describe(ForgeRules.Plan(mixed, 0.10)),
                Describe(ForgeRules.Plan(mixed, 0.85)),
                Describe(ForgeRules.Plan(short_, 0.5)));
        });

        // ---- 锻造产物：装备池按种子挑 ----
        Probe("forge.product", () =>
        {
            long regionId = FunGameService.Regions.OfType<OshimaRegion>().First().Id;
            List<string> parts = [];
            foreach (QualityType quality in new[]
            {
                QualityType.White, QualityType.Green, QualityType.Blue,
                QualityType.Purple, QualityType.Orange, QualityType.Red
            })
            {
                SeededRandomSource random = new(BaseSeed + 500 + (int)quality);
                FunGame.Core.Entity.Item? product = ForgeRules.Product(regionId, quality, new RandomSourceAdapter(random));
                parts.Add($"{quality}={(product is null ? "null" : product.Name)}");
            }
            return string.Join(",", parts);
        });

        // ---- 赛马：同种子必须复现同一场比赛 ----
        Probe("horse.race", () =>
        {
            (long UserId, string Name)[] entrants = [(1, "甲"), (2, "乙"), (3, "丙"), (4, "丁")];
            List<string> parts = [];
            foreach (int seed in new[] { 7, 2026, 20261114 })
            {
                HorseRaceOutcome outcome = HorseRace.Run(entrants, new SeededRandomSource(seed));
                string ranking = string.Join("/", outcome.Ranking
                    .OrderBy(entry => entry.Rank).ThenBy(entry => entry.UserId)
                    .Select(entry => Inv($"{entry.UserId}:{entry.Rank}:{entry.Points}:{entry.Position}:{entry.HP}")));
                parts.Add($"{seed}=track{outcome.TrackLength},{(outcome.Finished ? "fin" : "unfin")},{ranking}");
            }
            return string.Join("|", parts);
        });

        // ---- 突破 / 升级的消耗表（纯常量表，锁住「有人顺手调了价」） ----
        Probe("cost.levelbreak", () =>
        {
            List<string> parts = [];
            for (int level = 0; level <= 6; level++)
            {
                IReadOnlyList<BreakNeed> needs = GameplayRules.LevelBreakNeedy(level);
                parts.Add($"{level}:{string.Join("+", needs.Select(need => $"{need.Name}x{need.Count}"))}");
            }
            return string.Join("|", parts);
        });

        Probe("cost.skill-upgrade", () =>
        {
            List<string> parts = [];
            for (int level = 1; level <= 7; level++)
            {
                UpgradeNeed? need = GameplayRules.SkillUpgradeCost(level);
                parts.Add(need is null
                    ? $"{level}:null"
                    : Inv($"{level}:{need.RequiredLevel}/{need.RequiredBreak}/{need.Credits}/{need.Materials}/{string.Join("+", need.Items.Select(item => $"{item.Name}x{item.Count}"))}"));
            }
            return string.Join("|", parts);
        });

        Probe("cost.normal-attack-upgrade", () =>
        {
            List<string> parts = [];
            for (int level = 2; level <= 9; level++)
            {
                UpgradeNeed? need = GameplayRules.NormalAttackUpgradeCost(level);
                parts.Add(need is null
                    ? $"{level}:null"
                    : Inv($"{level}:{need.RequiredLevel}/{need.RequiredBreak}/{need.Credits}/{need.Materials}/{string.Join("+", need.Items.Select(item => $"{item.Name}x{item.Count}"))}"));
            }
            return string.Join("|", parts);
        });

        // ---- 运势：确定性哈希，同人同日必须同一条 ----
        Probe("fortune.stable", () =>
        {
            List<string> parts = [];
            foreach (long userId in new long[] { 1, 7, 42 })
            {
                string text = GameplayRules.Fortune(userId, "2026-11-14");
                parts.Add($"{userId}={GameplayRules.FortuneTexts.ToList().IndexOf(text)}");
            }
            return string.Join(",", parts);
        });

        // ---- 商店日切点：凌晨 4:00 之前算前一天（旧版 Today.AddHours(4)）----
        Probe("time.store-day", () =>
        {
            DateTime[] moments =
            [
                new(2026, 11, 13, 19, 59, 0, DateTimeKind.Utc),   // 本地 11-14 03:59 → 仍算 11-13
                new(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc),    // 本地 11-14 04:00 → 翻页
                new(2026, 11, 14, 3, 59, 0, DateTimeKind.Utc),    // 本地 11:59 → 11-14
                new(2026, 11, 14, 20, 0, 0, DateTimeKind.Utc),    // 本地 11-15 04:00 → 11-15
                new(2026, 12, 31, 19, 30, 0, DateTimeKind.Utc)    // 跨年：本地 2027-01-01 03:30 → 12-31
            ];
            return string.Join(",", moments.Select(moment =>
                Inv($"{moment:yyyy-MM-ddTHH:mmZ}={GameplayRules.StoreDay(moment)}")));
        });

        return probes;
    }

    /// <summary>把探针结果摊平成可哈希的文本（一行一个 <c>id\t值</c>）。</summary>
    public static string Canonicalize(IReadOnlyDictionary<string, string> probes)
        => string.Join("\n", probes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}\t{pair.Value}"));
}
