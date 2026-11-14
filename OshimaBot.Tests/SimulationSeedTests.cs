using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using OshimaBot.Game.Services;
using Xunit;
using Xunit.Abstractions;

namespace OshimaBot.Tests;

/// <summary>
/// 战斗模拟的种子回归——**能冻的冻死，冻不住的划量级**。
/// <para>背景（2026-11-15 实测）：<c>SeedOverride</c> 并不能让整局逐字复现。同一进程连跑 4 局，
/// 消息条数 44 / 59 / 54 / 49；三个独立进程 43 / 50 / 56；关掉周期空投仍是 36 / 33 / 44。
/// 分歧出现在第 60 回合附近的**局内决策**，而决策在引擎侧（<c>AIController</c> 并行评估、
/// 候选装 <c>ConcurrentBag</c>），本仓改不了。</para>
/// <para>于是分两层处理，各自冻自己冻得住的：</para>
/// <list type="number">
/// <item><b>开局逐字冻结</b>：选人 + 首次空投 + 前几个回合是纯种子驱动的，三次运行**逐字相同**，
///       所以把开局第 1 块消息的 SHA-256 冻起来。它能抓住「空投规则 / 装备池 / 开局等级」被改动；</item>
/// <item><b>量级带宽</b>：整局的每回合伤害与击杀数只保证落在冻结的区间内。
///       带宽是**手写**的，不是自动测出来的——自动测出来的带宽等于没有门禁。</item>
/// </list>
/// <para>重新冻结开局摘要：<c>OSHIMA_UPDATE_SEEDS=1 dotnet test --filter SimulationSeed</c>。
/// 带宽不自动改，要改请连同理由一起写进提交信息。</para>
/// </summary>
[Collection(SimulationStateCollection.Name)]
public sealed class SimulationSeedTests(ITestOutputHelper output)
{
    private const int Seed = 20261114;
    private const string UpdateVariable = "OSHIMA_UPDATE_SEEDS";
    private const string FixtureRelativePath = "Fixtures/Seeded/simulation.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string ProjectDirectory =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    private static string FixturePath => Path.Combine(ProjectDirectory, FixtureRelativePath);

    private static bool Updating
        => Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true" or "TRUE";

    static SimulationSeedTests() => FunGameRuntime.EnsureInitialized();

    /// <summary>种子被采纳、并写进战报首行；开局那一块逐字与冻结摘要一致。</summary>
    [Fact]
    public async Task Opening_IsReproducibleFromTheSeed()
    {
        SimulationRun run = await RunAsync();
        string opening = run.Messages[0];

        Assert.Equal(Seed, run.Seed);
        Assert.Contains($"[种子] 本局模拟使用随机种子 {Seed}", opening);
        Assert.Contains("空投", opening);

        string digest = Digest(opening);
        SimulationFixture fixture = ReadFixture();

        if (Updating)
        {
            fixture.Seed = Seed;
            fixture.OpeningDigest = digest;
            fixture.OpeningHead = Head(opening);
            WriteFixture(fixture);
            return;
        }

        Assert.True(fixture.OpeningDigest == digest,
            $"""
             开局内容与冻结摘要不一致。
               期望 {fixture.OpeningDigest}
               实际 {digest}
               期望开头：{fixture.OpeningHead.Replace("\n", " ⏎ ")}
               实际开头：{Head(opening).Replace("\n", " ⏎ ")}
             若空投规则 / 装备池 / 开局等级是有意改的，重新冻结：{UpdateVariable}=1 dotnet test --filter SimulationSeed
             否则说明有东西在开局阶段用了非种子随机（Random.Shared / DateTime）。
             """);
    }

    /// <summary>
    /// 对局还能正常打完：击杀数与回合数落在冻结区间内。
    /// <para><b>为什么不锁伤害</b>：同一份开局下，整局总伤害实测在 234k–416k 之间（1.8 倍），
    /// 每回合伤害 509–986（1.9 倍）——局内决策不可复现，伤害就不是个能冻结的量。
    /// 只有「该有人死、该结束」这件事是稳的（10 次采样击杀 18–19），所以只锁这个。</para>
    /// <para>能挡的是结构性坏掉：伤害/技能全部失效 → 没人死 → 回合数飙到上限；
    /// 或者一开局就团灭。挡不住「暴击倍率 1.5 → 1.6」这种微调，那要先把决策随机流钉死。</para>
    /// </summary>
    [Fact]
    public async Task Match_StillReachesADecisiveEnd()
    {
        SimulationFixture fixture = ReadFixture();

        for (int i = 0; i < 2; i++)
        {
            SimulationRun run = await RunAsync();
            (_, int kills, int rounds) = Summarize(run);
            output.WriteLine($"run{i}: kills={kills} rounds={rounds} messages={run.Messages.Count}");

            Assert.True(kills >= fixture.KillsMin && kills <= fixture.KillsMax,
                $"击杀数 {kills} 落在 [{fixture.KillsMin}, {fixture.KillsMax}] 之外（本局 {rounds} 回合、{run.Messages.Count} 条消息）。" +
                "要么有人再也打不死（伤害/技能失效），要么死得太快——先看伤害公式有没有被改坏。");
            Assert.True(rounds >= fixture.RoundsMin && rounds <= fixture.RoundsMax,
                $"回合数 {rounds} 落在 [{fixture.RoundsMin}, {fixture.RoundsMax}] 之外。" +
                "回合数飙到上限通常意味着「没人能死」，跌到极低意味着一开始就团灭。");
        }
    }

    // ---------------- 跑一局 ----------------

    private readonly record struct SimulationRun(List<string> Messages, int Seed);

    private static async Task<SimulationRun> RunAsync()
    {
        SimulationRunContext context = new() { SeedOverride = Seed };
        SimulationOptions options = new() { IsWeb = true, IsTeam = false, PrintOut = false };
        List<string> messages = await FunGameSimulation.StartSimulationGame(options, context);
        return new SimulationRun(messages, context.Seed);
    }

    /// <summary>从战报里取总伤害、总击杀与回合数（排行榜段落带这些数字，不另开接口）。</summary>
    private static (double Damage, int Kills, int Rounds) Summarize(SimulationRun run)
    {
        string joined = string.Join("\n", run.Messages);
        double damage = Regex.Matches(joined, @"总计伤害：([\d.]+)")
            .Sum(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        int kills = Regex.Matches(joined, @"击杀数：(\d+)").Sum(m => int.Parse(m.Groups[1].Value));
        int rounds = Regex.Matches(joined, @"=== Round (\d+) ===")
            .Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();
        return (damage, kills, rounds);
    }

    private static string Digest(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>摘要对不上时给人看的前三行，不然只看到一个哈希没法判断改了啥。</summary>
    private static string Head(string text)
        => string.Join("\n", text.Replace("\r", "").Split('\n').Take(3));

    // ---------------- 夹具 ----------------

    private static SimulationFixture ReadFixture()
    {
        if (!File.Exists(FixturePath))
        {
            Assert.True(Updating,
                $"找不到 {FixtureRelativePath}。生成它：{UpdateVariable}=1 dotnet test --filter SimulationSeed");
            return new SimulationFixture();
        }

        SimulationFixture? fixture = JsonSerializer.Deserialize<SimulationFixture>(File.ReadAllText(FixturePath));
        Assert.NotNull(fixture);
        return fixture;
    }

    private static void WriteFixture(SimulationFixture fixture)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FixturePath)!);
        File.WriteAllText(FixturePath, JsonSerializer.Serialize(fixture, Json) + "\n", new UTF8Encoding(false));
    }

    /// <summary>
    /// 冻结的模拟样本：开局摘要自动更新；区间**手改**（自动测出来的区间等于没有门禁）。
    /// <para>初始区间依据 2026-11-15 十余次同种子实测：击杀 18–19、回合 384–733，
    /// 各留一倍余量给调度差异；伤害因为波动高达 1.8 倍，干脆不锁。</para>
    /// </summary>
    private sealed class SimulationFixture
    {
        public int SchemaVersion { get; set; } = 1;

        public int Seed { get; set; } = SimulationSeedTests.Seed;

        public string OpeningDigest { get; set; } = "";

        public string OpeningHead { get; set; } = "";

        public int KillsMin { get; set; } = 14;

        public int KillsMax { get; set; } = 24;

        public int RoundsMin { get; set; } = 200;

        public int RoundsMax { get; set; } = 1200;

        public string Note { get; set; } =
            "整局无法逐字复现（局内决策在引擎侧不可复现，见 REMAINING §六）：" +
            "开局摘要自动冻结，整局只锁量级带宽，带宽手改并写理由。";
    }
}
