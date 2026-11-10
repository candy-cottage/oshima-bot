using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace OshimaBot.Tests;

/// <summary>
/// M0 行为等价装置的看门测试。
/// <para>定位：锁定<b>当前实现</b>的数值，保证后续重构不静默改数值；
/// 旧版数值由 <c>expected.legacy</c> 承载，为 null 表示待从旧版 Oracle 提取。
/// 因此本组测试全绿只代表「当前值未被无意改动」，不代表「已与旧版等价」。</para>
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
        LegacyFixture fixture = Fixtures.Single(x => x.Id == id);
        JsonNode actual = LegacyFixtureProbes.Run(fixture.Probe, fixture.Input);

        if (!JsonNode.DeepEquals(fixture.Expected.Current, actual))
        {
            string expected = fixture.Expected.Current?.ToJsonString(Indented) ?? "null";
            Assert.Fail($"{fixture.Id}（{fixture.Command}）当前实现与锁定值不一致。\n" +
                        $"期望：\n{expected}\n实际：\n{actual.ToJsonString(Indented)}\n" +
                        "若本次改动是有意的数值调整，请同步更新 fixture 并在文档中记录原因。");
        }
    }

    [Fact]
    public void Fixtures_ReportLegacyCoverage()
    {
        int verified = Fixtures.Count(x => x.Expected.Legacy is not null);
        int total = Fixtures.Count;
        output.WriteLine($"旧版数值已回填：{verified}/{total}");

        foreach (LegacyFixture fixture in Fixtures.Where(x => x.Expected.Legacy is null))
        {
            output.WriteLine($"  待补旧版值：{fixture.Id} ({fixture.Command}) ← {fixture.Source.File}");
        }

        // 只报告，不失败：M1 才把 legacy 值填进去。
        Assert.Equal(total, Fixtures.Select(x => x.Id).Distinct().Count());
    }
}
