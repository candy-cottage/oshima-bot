using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 抽卡的 L1/L2 等价：命中率、8 类产出、品质阈值、重复角色折算与「橙品以上自动锁定」。
/// <para>旧版实现见 <c>OshimaServers/Service/FunGameService.cs</c> 的 <c>DrawCards</c> /
/// <c>GetDrawCardResult</c>：每次独立掷 <c>NextDouble() &gt; 0.8</c>（20%）才出东西，
/// 十连无保底；命中后 <c>Next(8)</c> 均匀选产出类型，品质按 <c>DrawCardProbabilities</c> 阈值判定。</para>
/// </summary>
public class GachaLegacyTests
{
    private const long UserId = 7;

    /// <summary>分布回归：固定种子下命中率、产出类型与品质档位都要落在旧版公式附近。</summary>
    [Fact]
    public void Gacha_DistributionMatchesLegacyProbabilities()
    {
        const int samples = 20000;
        SeededRandomSource random = new(20261113);

        int hits = 0;
        int[] kinds = new int[8];
        int[] qualities = new int[8];
        for (int i = 0; i < samples; i++)
        {
            if (GameplayRules.RollGachaHit(random)) hits++;
            kinds[(int)GameplayRules.RollGachaKind(random)]++;
            qualities[GameplayRules.RollGachaQuality(random)]++;
        }

        // 旧版：dice > 0.8 才中奖 → 20%
        Assert.InRange((double)hits / samples, 0.19, 0.21);

        // 旧版：Next(8) 均匀落到 8 类产出 → 每类 12.5%
        for (int kind = 0; kind < kinds.Length; kind++)
        {
            Assert.InRange((double)kinds[kind] / samples, 0.115, 0.135);
        }

        // 旧版阈值 3.06 / 6.33 / 8.25 / 13.48 / 19.35 / 49.53（累计）
        Assert.InRange((double)qualities[6] / samples, 0.026, 0.036);      // Red    0–3.06
        Assert.InRange((double)qualities[5] / samples, 0.028, 0.038);      // Orange 3.06–6.33
        // 阈值是累计上限：White 拿到 19.35～100 的全部区间（旧版同样如此，尾部落到默认档）
        Assert.InRange((double)qualities[1] / samples, 0.790, 0.820);
    }

    /// <summary>旧版抽到第 2 个同名角色时 <c>Materials += 50</c>，不重复入库。</summary>
    [Fact]
    public async Task Gacha_DuplicateCharacterRefundsFiftyDiamonds()
    {
        // 每次抽卡取四个随机数：命中判定、品质、产出类型、角色下标
        using GachaDatabase db = new(new ScriptedSequenceRandom(
            doubles: [0.9, 0.9, 0.9, 0.9],       // 两次都命中，品质落到 White（0.9*100 > 49.53）
            ints: [5, 0, 5, 0]));                 // 产出类型 = 5 角色，取第 0 号角色
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SetDiamondsAsync(db, 100);
        PlayerSnapshot before = (await players.GetSnapshotAsync(UserId))!;

        Assert.True((await players.GachaAsync(UserId, 1, useMaterial: true)).Ok);
        PlayerSnapshot first = (await players.GetSnapshotAsync(UserId))!;
        Assert.Equal(before.CharacterCount + 1, first.CharacterCount);   // 第一次真的入库
        Assert.Equal(before.Materials - GameplayRules.GachaCost, first.Materials);

        Assert.True((await players.GachaAsync(UserId, 1, useMaterial: true)).Ok);
        PlayerSnapshot second = (await players.GetSnapshotAsync(UserId))!;

        Assert.Equal(first.CharacterCount, second.CharacterCount);       // 第二次不再新增角色
        Assert.Equal(first.Materials - GameplayRules.GachaCost + GameplayRules.GachaDuplicateCharacterMaterials,
            second.Materials);
    }

    /// <summary>旧版：抽到的物品品质 ≥ 橙，直接置 <c>IsLock = true</c>，不允许误分解。</summary>
    [Fact]
    public async Task Gacha_LocksOrangeAndAbove()
    {
        // 命中 + Red 品质 + 魔法卡（内容包里没有模板，按品质当场生成，品质确定）
        using GachaDatabase db = new(new ScriptedSequenceRandom(
            doubles: [0.9, 0.01],
            ints: [6, 42]));   // 产出类型 = 数字6（魔法卡），42 是生成卡时的随机种子
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SetDiamondsAsync(db, 100);

        Assert.True((await players.GachaAsync(UserId, 1, useMaterial: true)).Ok);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        OshimaBot.Data.Core.CoreItemRecord record = await context.CoreItems.SingleAsync();
        Assert.True(record.Quality >= (int)FunGame.Core.Library.Constant.QualityType.Orange);
        Assert.True(record.IsLock, "橙品以上抽到就该锁定（旧版防止误分解）");
    }

    /// <summary>锁定规则本身：White/Green/Blue/Purple 不锁，Orange/Red/Gold 锁。</summary>
    [Theory]
    [InlineData(FunGame.Core.Library.Constant.QualityType.White, false)]
    [InlineData(FunGame.Core.Library.Constant.QualityType.Green, false)]
    [InlineData(FunGame.Core.Library.Constant.QualityType.Blue, false)]
    [InlineData(FunGame.Core.Library.Constant.QualityType.Purple, false)]
    [InlineData(FunGame.Core.Library.Constant.QualityType.Orange, true)]
    [InlineData(FunGame.Core.Library.Constant.QualityType.Red, true)]
    [InlineData(FunGame.Core.Library.Constant.QualityType.Gold, true)]
    public void AutoLock_StartsAtOrange(FunGame.Core.Library.Constant.QualityType quality, bool locked)
        => Assert.Equal(locked, GameplayRules.AutoLockOnDraw(quality));

    // ---------------- 夹具 ----------------

    private static async Task SetDiamondsAsync(GachaDatabase db, double diamonds)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        OshimaBot.Data.Core.CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == UserId);
        wallet.Materials = diamonds;
        await context.SaveChangesAsync();
    }

    private sealed class GachaDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public GachaDatabase(IRandomSource random)
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
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

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
