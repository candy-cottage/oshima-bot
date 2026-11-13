using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace OshimaBot.Tests;

/// <summary>
/// M0 行为等价装置的看门测试。
/// <para>三类状态分别强制不同的契约：</para>
/// <list type="bullet">
/// <item><c>current-locked</c>：只锁定当前实现的值，旧值待确认，仅报告。</item>
/// <item><c>legacy-verified</c>：必须逐字段比较新旧值，不一致即失败。</item>
/// <item><c>intentional-divergence</c>：必须登记原因，且申报的差异字段必须确实不同；
/// 差异一旦被抹平（例如把旧值改回来），测试失败并要求升级状态。</item>
/// </list>
/// </summary>
public class LegacyFixtureTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<LegacyFixture> Fixtures = LegacyFixtureLoader.LoadAll();

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static TheoryData<string> FixtureIds
    {
        get
        {
            TheoryData<string> data = [];
            foreach (LegacyFixture fixture in Fixtures) data.Add(fixture.Id);
            return data;
        }
    }

    private static LegacyFixture Get(string id) => Fixtures.Single(x => x.Id == id);

    private static JsonObject CurrentOf(LegacyFixture fixture)
        => fixture.Expected.Current as JsonObject
           ?? throw new Xunit.Sdk.XunitException($"{fixture.Id} 的 expected.current 必须是对象");

    private static JsonObject LegacyOf(LegacyFixture fixture)
        => fixture.Expected.Legacy as JsonObject
           ?? throw new Xunit.Sdk.XunitException($"{fixture.Id} 的 expected.legacy 必须是对象（不能为 null 或字面量）");

    [Fact]
    public void Loader_FindsFixtures()
    {
        Assert.NotEmpty(Fixtures);
        Assert.True(Fixtures.Count >= 10, $"关键数值 fixture 至少 10 个，当前 {Fixtures.Count}");
    }

    [Fact]
    public void Fixtures_HaveUniqueIds()
    {
        string[] duplicates = Fixtures.GroupBy(x => x.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Fixtures_UseKnownProbesAndStatuses()
    {
        foreach (LegacyFixture fixture in Fixtures)
        {
            Assert.Contains(fixture.Probe, LegacyFixtureProbes.KnownProbes);
            Assert.Contains(fixture.Status, LegacyFixtureLoader.KnownStatuses);
            Assert.False(string.IsNullOrWhiteSpace(fixture.BehaviorId), $"{fixture.Id} 缺少行为 ID");
            Assert.False(string.IsNullOrWhiteSpace(fixture.Command), $"{fixture.Id} 缺少旧命令名");
        }
    }

    [Fact]
    public void Fixtures_RecordSourceAndBaseline()
    {
        foreach (LegacyFixture fixture in Fixtures)
        {
            Assert.False(string.IsNullOrWhiteSpace(fixture.LegacyBaseline), $"{fixture.Id} 缺少旧版基线指纹");
            Assert.False(string.IsNullOrWhiteSpace(fixture.Source.File), $"{fixture.Id} 缺少旧实现路径");
            Assert.NotNull(fixture.Expected.Current);
        }
    }

    /// <summary>状态契约：非 current-locked 必须给出比较策略；有意差异必须给出原因与差异字段。</summary>
    [Fact]
    public void Fixtures_StatusContractIsComplete()
    {
        List<string> problems = [];

        foreach (LegacyFixture fixture in Fixtures)
        {
            if (fixture.Status != LegacyFixtureLoader.StatusCurrentLocked)
            {
                if (fixture.Expected.Legacy is null) problems.Add($"{fixture.Id}：{fixture.Status} 必须有 expected.legacy");
                if (fixture.LegacyMatchPolicy is null) problems.Add($"{fixture.Id}：{fixture.Status} 必须有 legacyMatchPolicy");
                else if (fixture.LegacyMatchPolicy.Fields.Count == 0) problems.Add($"{fixture.Id}：legacyMatchPolicy.fields 不能为空");
                else if (!string.Equals(fixture.LegacyMatchPolicy.Mode, LegacyMatchPolicy.ModeExact, StringComparison.Ordinal)
                         && !string.Equals(fixture.LegacyMatchPolicy.Mode, LegacyMatchPolicy.ModeTolerance, StringComparison.Ordinal))
                {
                    problems.Add($"{fixture.Id}：未知的 legacyMatchPolicy.mode={fixture.LegacyMatchPolicy.Mode}");
                }
            }

            if (fixture.Status == LegacyFixtureLoader.StatusIntentionalDivergence)
            {
                if (fixture.Divergence is null) problems.Add($"{fixture.Id}：有意差异缺少 divergence 登记");
                else
                {
                    if (string.IsNullOrWhiteSpace(fixture.Divergence.Reason)) problems.Add($"{fixture.Id}：divergence.reason 不能为空");
                    if (fixture.Divergence.Fields.Count == 0) problems.Add($"{fixture.Id}：divergence.fields 不能为空");
                }
            }
            else if (fixture.Status == LegacyFixtureLoader.StatusLegacyVerified && fixture.Divergence is not null)
            {
                problems.Add($"{fixture.Id}：legacy-verified 不应保留 divergence 登记，请改回 intentional-divergence 或删除");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Fixtures_CoverKeyDomains()
    {
        string[] probes = Fixtures.Select(x => x.Probe).Distinct().ToArray();
        // M0 验收：升级、突破、抽卡、商店、市场、锻造、Boss、训练、签到、存档 都要有样本
        string[] required =
        [
            "levelup", "break", "gacha", "store", "market-fee",
            "forge", "boss", "train", "signin", "register"
        ];
        string[] missing = required.Except(probes).ToArray();
        Assert.Empty(missing);
    }

    [Theory]
    [MemberData(nameof(FixtureIds))]
    public void Fixture_CurrentValuesMatchImplementation(string id)
    {
        LegacyFixture fixture = Get(id);
        JsonNode actual = LegacyFixtureProbes.Run(fixture.Probe, fixture.Input);

        if (!JsonNode.DeepEquals(fixture.Expected.Current, actual))
        {
            string expected = fixture.Expected.Current?.ToJsonString(Indented) ?? "null";
            Assert.Fail($"{fixture.Id}（{fixture.Command}）当前实现与锁定值不一致。\n" +
                        $"期望：\n{expected}\n实际：\n{actual.ToJsonString(Indented)}\n" +
                        "若本次改动是有意的数值调整，请同步更新 fixture 并同时更新 expected.legacy，否则会丢失旧版对照。");
        }
    }

    /// <summary>legacy-verified：新旧值必须逐字段一致，否则失败。</summary>
    [Theory]
    [MemberData(nameof(FixtureIds))]
    public void Fixture_LegacyValuesMatchCurrent(string id)
    {
        LegacyFixture fixture = Get(id);
        if (fixture.Status != LegacyFixtureLoader.StatusLegacyVerified) return;

        LegacyMatchPolicy policy = fixture.LegacyMatchPolicy!;
        string? difference = LegacyFieldComparer.FirstDifference(CurrentOf(fixture), LegacyOf(fixture), policy);
        Assert.True(difference is null,
            $"{fixture.Id}（{fixture.Command}）标为 legacy-verified，但旧值与当前值不一致：{difference}\n" +
            $"比较字段：{string.Join(", ", policy.Fields)}（mode={policy.Mode}）\n" +
            "确认后者：修正实现使两者一致；或改判 intentional-divergence 并写明原因。");
    }

    /// <summary>intentional-divergence：申报的共享字段必须一致，申报的差异字段必须确实不同。</summary>
    [Theory]
    [MemberData(nameof(FixtureIds))]
    public void Fixture_IntentionalDivergenceIsStillReal(string id)
    {
        LegacyFixture fixture = Get(id);
        if (fixture.Status != LegacyFixtureLoader.StatusIntentionalDivergence) return;

        JsonObject current = CurrentOf(fixture);
        JsonObject legacy = LegacyOf(fixture);
        LegacyDivergence divergence = fixture.Divergence!;

        // 1. 未申报为差异的字段必须与旧版一致，避免「整块都算差异」的偷懒登记。
        string? shared = LegacyFieldComparer.FirstDifference(current, legacy, fixture.LegacyMatchPolicy!);
        Assert.True(shared is null, $"{fixture.Id}：申报为一致的字段出现差异：{shared}");

        // 2. 每个申报差异的字段必须确实不同。
        string[] vanished = divergence.Fields
            .Where(f => !LegacyFieldComparer.Differs(current, legacy, f))
            .ToArray();
        Assert.True(vanished.Length == 0,
            $"{fixture.Id}：以下字段已不再有差异，说明差异已被抹平，应升级为 legacy-verified：{string.Join(", ", vanished)}");
    }

    [Fact]
    public void Fixtures_ReportLegacyCoverage()
    {
        int total = Fixtures.Count;
        int verified = Fixtures.Count(x => x.Status == LegacyFixtureLoader.StatusLegacyVerified);
        int divergent = Fixtures.Count(x => x.Status == LegacyFixtureLoader.StatusIntentionalDivergence);
        int locked = Fixtures.Count(x => x.Status == LegacyFixtureLoader.StatusCurrentLocked);

        output.WriteLine($"legacy-verified {verified} / intentional-divergence {divergent} / current-locked {locked}（共 {total}）");
        foreach (LegacyFixture fixture in Fixtures.Where(x => x.Status == LegacyFixtureLoader.StatusCurrentLocked))
        {
            output.WriteLine($"  待确认旧值：{fixture.Id} ({fixture.Command}) ← {fixture.Source.File}");
        }

        // 只报告，不失败：R0 之后新增 fixture 允许先 current-locked。
        Assert.Equal(total, Fixtures.Select(x => x.Id).Distinct().Count());
    }
}
