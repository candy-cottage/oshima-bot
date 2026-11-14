using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 冻结种子回归：把 <see cref="SeedScenario"/> 的每个探针值与
/// <c>Fixtures/Seeded/regression.json</c> 里的快照逐行比对。
/// <para>这是「战斗与玩法数值有没有被顺手改动」的自动检查。别的测试断言性质（区间、上限），
/// 这里断言**具体值**：改了概率表、返还表、手续费口径、赛马积分，都必须显式更新快照。</para>
/// <para>更新快照（只应在**确认改动是有意的**之后执行）：</para>
/// <code>OSHIMA_UPDATE_SEEDS=1 dotnet test --filter SeededRegression</code>
/// </summary>
public sealed class SeededRegressionTests
{
    private const long UserId = 9001;
    private const string UpdateVariable = "OSHIMA_UPDATE_SEEDS";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 夹具在**源码树**里：快照要入库、要能 review 差异，不能只落在 bin 下。
    /// <c>bin/Debug/net10.0</c> 往上三层就是项目目录。
    /// </summary>
    private static readonly string ProjectDirectory =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    private static string FixturePath
        => Path.Combine(ProjectDirectory, SeedScenario.FixtureRelativePath);

    private static bool Updating
        => Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true" or "TRUE";

    // ---------------- 规则层 ----------------

    [Fact]
    public async Task SeededRegression_MatchesFrozenSnapshot()
    {
        SortedDictionary<string, string> actual = SeedScenario.Run();
        actual["gacha.ten-draw"] = await RunTenDrawAsync();
        string actualDigest = Digest(actual);

        if (Updating)
        {
            WriteFixture(actual, actualDigest);
            return;
        }

        SeedFixture fixture = ReadFixture();
        Assert.Equal(SeedScenario.SchemaVersion, fixture.SchemaVersion);

        // 先把「文件内容」和「文件里记的指纹」对上：否则手改某个数字会绕过指纹校验。
        Assert.True(fixture.Digest == Digest(fixture.Probes),
            $"{SeedScenario.FixtureRelativePath} 被手工改动过（文件内容与 digest 不符）。\n" +
            $"确认改动有意后重新生成：{UpdateVariable}=1 dotnet test --filter SeededRegression");

        string diff = DescribeDiff(fixture.Probes, actual);
        Assert.True(diff.Length == 0,
            "冻结种子样本与当前实现不一致。\n" + diff +
            $"\n若这是**有意的数值改动**，更新快照：{UpdateVariable}=1 dotnet test --filter SeededRegression\n" +
            "否则说明改动碰到了不该动的数值（概率表 / 返还表 / 手续费 / 积分曲线）。");
    }

    /// <summary>同一进程内跑两遍必须完全一致——否则说明探针里混进了 <c>Random.Shared</c> 或时间。</summary>
    [Fact]
    public void SeededRegression_IsStableWithinProcess()
    {
        SortedDictionary<string, string> first = SeedScenario.Run();
        SortedDictionary<string, string> second = SeedScenario.Run();
        Assert.Equal(SeedScenario.Canonicalize(first), SeedScenario.Canonicalize(second));
    }

    /// <summary>探针数量兜底：防止有人为了「让快照通过」把探针删掉。</summary>
    [Fact]
    public void SeededRegression_CoversEveryProbe()
    {
        SortedDictionary<string, string> probes = SeedScenario.Run();

        string[] required =
        [
            "gacha.hit-rate", "gacha.kind-mix", "gacha.quality-mix", "gacha.sequence",
            "signin.reward-ladder", "signin.quality-ladder",
            "explore.mix", "explore.yields", "instance.reward",
            "market.fee", "market.money-format", "exchange.plan", "salvage.materials",
            "forge.plan", "forge.product", "horse.race",
            "cost.levelbreak", "cost.skill-upgrade", "cost.normal-attack-upgrade",
            "fortune.stable", "time.store-day"
        ];

        string[] missing = [.. required.Where(id => !probes.ContainsKey(id))];
        Assert.True(missing.Length == 0, $"缺少探针：{string.Join(", ", missing)}");
        Assert.Equal(required.Length, probes.Count);
    }

    // ---------------- 服务层：十连真的走完「掷骰 → 入包 → 定价 → 锁定」 ----------------

    /// <summary>
    /// 十连的端到端数值：同种子下抽到哪些东西、什么品质、回收价多少、有没有锁定、扣了多少材料。
    /// <para>规则层探针盯的是概率表，这一条盯的是「表接没接对」——比如品质映射错一档、
    /// 回收价没按 <c>DefaultItemPrice</c> 写、橙品漏锁，都会让这一行变。</para>
    /// </summary>
    [Fact]
    public async Task GachaTenDraw_MatchesFrozenSnapshot()
    {
        if (Updating) return;   // 全量重建由 SeededRegression_MatchesFrozenSnapshot 统一做，避免两条用例互相覆盖

        const string probeId = "gacha.ten-draw";
        string actual = await RunTenDrawAsync();

        SeedFixture fixture = ReadFixture();
        Assert.True(fixture.Probes.TryGetValue(probeId, out string? expected),
            $"夹具里没有 {probeId}，请重新生成：{UpdateVariable}=1 dotnet test --filter SeededRegression");
        Assert.True(expected == actual,
            $"十连结果与冻结样本不一致。\n  期望：{expected}\n  实际：{actual}\n" +
            $"若改动有意：{UpdateVariable}=1 dotnet test --filter SeededRegression");
    }

    private static async Task<string> RunTenDrawAsync()
    {
        using GachaHarness harness = new(new SeededRandomSource(SeedScenario.BaseSeed + 900));
        IPlayerService players = harness.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await harness.SetMaterialsAsync(UserId, 1000);

        ActionResult result = await players.GachaAsync(UserId, 10, useMaterial: true);
        PlayerSnapshot snapshot = (await players.GetSnapshotAsync(UserId))!;

        using IServiceScope scope = harness.Provider.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        List<PlayerItem> items = await ItemStore.ListAsync(db, UserId, CancellationToken.None);

        string bag = string.Join("|", items.Select(item =>
            $"{item.Name}#{item.Quality}#{item.Record.Price.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}" +
            $"#{(item.Locked ? "L" : "-")}#{item.Quantity}"));

        return $"ok={result.Ok};materials={snapshot.Materials.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)};bag={bag}";
    }

    // ---------------- 夹具读写 ----------------

    private static string Digest(IReadOnlyDictionary<string, string> probes)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SeedScenario.Canonicalize(probes)))).ToLowerInvariant();

    private static void WriteFixture(IReadOnlyDictionary<string, string> probes, string digest)
    {
        SeedFixture fixture = new()
        {
            SchemaVersion = SeedScenario.SchemaVersion,
            BaseSeed = SeedScenario.BaseSeed,
            Note = "冻结种子回归样本：数值变化必须先解释清楚，再重新生成。生成命令见 SeededRegressionTests 的注释。",
            Digest = digest
        };

        foreach ((string id, string value) in probes) fixture.Probes[id] = value;

        Directory.CreateDirectory(Path.GetDirectoryName(FixturePath)!);
        File.WriteAllText(FixturePath, JsonSerializer.Serialize(fixture, Json) + "\n", new UTF8Encoding(false));
    }

    private static SeedFixture ReadFixture()
    {
        Assert.True(File.Exists(FixturePath),
            $"找不到冻结样本 {SeedScenario.FixtureRelativePath}。生成它：" +
            $"{UpdateVariable}=1 dotnet test --filter SeededRegression");

        SeedFixture? fixture = JsonSerializer.Deserialize<SeedFixture>(File.ReadAllText(FixturePath));
        Assert.NotNull(fixture);
        // SortedDictionary 反序列化用的是默认比较器（区域性、大小写不敏感），统一换成 Ordinal。
        fixture.Probes = new SortedDictionary<string, string>(fixture.Probes, StringComparer.Ordinal);
        return fixture;
    }

    /// <summary>逐项 diff：只列真正不一样的行，避免「咦，不是刚生成过吗」的盲目重生成。</summary>
    private static string DescribeDiff(
        IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        StringBuilder builder = new();
        int count = 0;

        foreach ((string id, string expectedValue) in expected.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (actual.TryGetValue(id, out string? actualValue) && actualValue == expectedValue) continue;
            count++;
            builder.AppendLine(actualValue is null
                ? $"  - {id}：夹具里有、实现里没有（探针被删了？）"
                : $"  - {id}\n      期望 {expectedValue}\n      实际 {actualValue}");
        }

        foreach (string id in actual.Keys.Where(id => !expected.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal))
        {
            count++;
            builder.AppendLine($"  - {id}：实现了新探针，夹具里还没有（需要重新生成）");
        }

        return count == 0 ? "" : $"共 {count} 项不一致：\n{builder}";
    }

    private sealed class SeedFixture
    {
        public int SchemaVersion { get; set; }

        public int BaseSeed { get; set; }

        public string Note { get; set; } = "";

        public string Digest { get; set; } = "";

        public SortedDictionary<string, string> Probes { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>十连要用真的库走完整链路，这里给一个不落盘的内存库。</summary>
    private sealed class GachaHarness : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ServiceProvider Provider { get; }

        public GachaHarness(IRandomSource random)
        {
            FunGameRuntime.EnsureInitialized();
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<ApiKeyAuthenticator>();
            services.AddGameplayRuntime(random: random);
            services.AddSingleton<IPlayerService, PlayerService>();
            Provider = services.BuildServiceProvider();

            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task SetMaterialsAsync(long userId, double materials)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await db.CorePlayers.FirstAsync(x => x.UserId == userId);
            wallet.Materials = materials;
            await db.SaveChangesAsync();
        }

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }
}
