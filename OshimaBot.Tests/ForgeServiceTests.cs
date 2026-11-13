using FunGame.Core.Library.Constant;
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
/// 锻造（旧版 ForgeItem_Create / _Master / _Cancel / _Simulate / _Complete）。
/// <para>核心口径：材料必须是野外地区作物；单一地区不乘系数、混合地区按「数量 × 星级系数」折算；
/// 品质按有效用量的门槛判定；积分 = 通用 + 成功/失败两段。</para>
/// </summary>
public sealed class ForgeServiceTests
{
    private const long Player = 42;
    private const string SilverAlloy = "星银合金";     // 银辉城（5 星）材料
    private const string EmberOre = "深渊火钻";         // 永燃坩埚（1 星）材料

    [Fact]
    public void Plan_SingleRegionKeepsRawCount()
    {
        // 单一地区：不乘系数（旧版口径），40 件 → 40 有效用量 → 普通
        ForgePlan plan = ForgeRules.Plan([new ForgeMaterial(SilverAlloy, 40, 1, "银辉城", RarityType.FiveStar)], 0.5);
        Assert.True(plan.Ok);
        Assert.Equal(40, plan.EffectiveCount);
        Assert.Equal(QualityType.White, plan.Quality);
        Assert.Equal(1, plan.RegionId);
    }

    [Fact]
    public void Plan_MultiRegionUsesDifficultyCoefficient()
    {
        // 混合：1 星 40 件（×0.5 = 20）+ 5 星 40 件（×1.0 = 40）= 60 → 优秀
        ForgeMaterial[] materials =
        [
            new(EmberOre, 40, 3, "永燃坩埚", RarityType.OneStar),
            new(SilverAlloy, 40, 1, "银辉城", RarityType.FiveStar)
        ];
        ForgePlan plan = ForgeRules.Plan(materials, 0.5);
        Assert.True(plan.Ok);
        Assert.Equal(60, plan.EffectiveCount);
        Assert.Equal(QualityType.Green, plan.Quality);
        Assert.Equal(2, plan.RegionShares.Count);

        // roll 决定产地：0.1 落在低贡献的一侧
        Assert.Equal(3, ForgeRules.Plan(materials, 0.1).RegionId);
        Assert.Equal(1, ForgeRules.Plan(materials, 0.9).RegionId);
    }

    /// <summary>
    /// 品质门槛与积分（旧版 <c>ForgeNeedy</c> 30/50/80/120/170/230）：
    /// 有效用量不足 30 直接拒绝；够哪一档出哪一档；通用积分 = 用量 × 0.3，
    /// 失败积分 = 用量 × 0.2，成功积分 = 用量 − 该品质门槛。
    /// </summary>
    [Theory]
    [InlineData(10, QualityType.White, false)]
    [InlineData(30, QualityType.White, true)]
    [InlineData(49, QualityType.White, true)]
    [InlineData(50, QualityType.Green, true)]
    [InlineData(80, QualityType.Blue, true)]
    [InlineData(120, QualityType.Purple, true)]
    [InlineData(170, QualityType.Orange, true)]
    [InlineData(230, QualityType.Red, true)]
    [InlineData(500, QualityType.Red, true)]
    public void Plan_QualityThresholdsAndPointsMatchLegacy(int count, QualityType expected, bool ok)
    {
        ForgePlan plan = ForgeRules.Plan(
            [new ForgeMaterial(SilverAlloy, count, 1, "银辉城", RarityType.FiveStar)], 0.5);

        Assert.Equal(ok, plan.Ok);
        if (!ok) return;

        Assert.Equal(expected, plan.Quality);
        Assert.Equal(count, plan.EffectiveCount);
        Assert.Equal(count * ForgeRules.GeneralPointsPerCount, plan.GeneralPoints, 6);
        Assert.Equal(count * ForgeRules.FailPointsPerCount, plan.FailPoints, 6);
        Assert.Equal(count - ForgeRules.Needy[expected], plan.SuccessPoints, 6);
    }

    [Fact]
    public void Plan_RejectsBelowMinimumAndNonRegion()
    {
        ForgePlan tooFew = ForgeRules.Plan([new ForgeMaterial(SilverAlloy, 10, 1, "银辉城", RarityType.FiveStar)], 0.5);
        Assert.False(tooFew.Ok);
        Assert.Contains("不足最低要求", tooFew.Message);

        ForgePlan nowhere = ForgeRules.Plan([new ForgeMaterial("随便什么", 99, 0, "铎京城", RarityType.OneStar)], 0.5);
        Assert.False(nowhere.Ok);
        Assert.Contains("不属于任何野外地区", nowhere.Message);
    }

    [Fact]
    public async Task Start_RequiresRegionMaterial()
    {
        using ForgeDatabase db = new();
        IForgeService forge = db.Get<IForgeService>();
        await db.CreatePlayerAsync(Player);
        await db.AddItemAsync(Player, "普通石头", 40);      // 不是地区材料

        ActionResult denied = await forge.StartAsync(Player, [(1, 40)], 5);
        Assert.False(denied.Ok);
        Assert.Contains("不是锻造材料", denied.Title);
        Assert.Equal(40, (await db.ItemsAsync(Player)).Sum(x => x.Quantity));   // 没扣
    }

    [Fact]
    public async Task Start_Claim_DeliversProductAndPoints()
    {
        using ForgeDatabase db = new();
        IForgeService forge = db.Get<IForgeService>();
        await db.CreatePlayerAsync(Player);
        await db.AddItemAsync(Player, SilverAlloy, 40);
        double credits = await db.CreditsAsync(Player);

        Assert.True((await forge.StartAsync(Player, [(1, 40)], 5)).Ok);
        Assert.Equal(credits - ForgeRules.CostCredits, await db.CreditsAsync(Player), 3);
        Assert.Empty(await db.ItemsAsync(Player));          // 材料已投入

        ForgeInfo running = await forge.InfoAsync(Player);
        Assert.True(running.Running);
        Assert.Contains(SilverAlloy, running.Recipe);
        Assert.False((await forge.ClaimAsync(Player)).Ok);   // 还没到时间

        db.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.True((await forge.InfoAsync(Player)).Ready);
        ActionResult claimed = await forge.ClaimAsync(Player);
        Assert.True(claimed.Ok, claimed.Title);

        // 40 件 → 普通品质，产物进背包；积分 = 12（通用）+ 10（成功 40-30）
        Assert.Contains(await db.ItemsAsync(Player), item => item.Quality <= 1);
        Assert.Equal(22, await db.ForgePointsAsync(Player), 3);
        Assert.False((await forge.InfoAsync(Player)).Running);
    }

    [Fact]
    public async Task Cancel_RefundsMaterialsAndCredits()
    {
        using ForgeDatabase db = new();
        IForgeService forge = db.Get<IForgeService>();
        await db.CreatePlayerAsync(Player);
        await db.AddItemAsync(Player, SilverAlloy, 40);
        double credits = await db.CreditsAsync(Player);

        Assert.True((await forge.StartAsync(Player, [(1, 40)], 5)).Ok);
        ActionResult cancelled = await forge.CancelAsync(Player);
        Assert.True(cancelled.Ok, cancelled.Title);

        Assert.Equal(credits, await db.CreditsAsync(Player), 3);
        Assert.Equal(40, (await db.ItemsAsync(Player)).Sum(x => x.Quantity));
        Assert.False((await forge.InfoAsync(Player)).Running);
    }

    [Fact]
    public async Task Simulate_DoesNotConsume()
    {
        using ForgeDatabase db = new();
        IForgeService forge = db.Get<IForgeService>();
        await db.CreatePlayerAsync(Player);
        await db.AddItemAsync(Player, SilverAlloy, 40);

        ActionResult simulated = await forge.SimulateAsync(Player, [(1, 40)]);
        Assert.True(simulated.Ok, simulated.Title);
        Assert.Equal(40, (await db.ItemsAsync(Player)).Sum(x => x.Quantity));
        Assert.False((await forge.InfoAsync(Player)).Running);
    }

    [Fact]
    public async Task MasterForge_ConsumesTicketToForceTarget()
    {
        using ForgeDatabase db = new();
        IForgeService forge = db.Get<IForgeService>();
        await db.CreatePlayerAsync(Player);
        await db.AddItemAsync(Player, SilverAlloy, 40);
        await db.AddItemAsync(Player, ForgeService.MasterTicketName, 1);

        Assert.True((await forge.StartAsync(Player, [(1, 40)], 5)).Ok);
        // 目标设成另一个地区 + 更高品质，与自然结果必然不符
        Assert.True((await forge.SetMasterAsync(Player, 3, "史诗")).Ok);

        db.Clock.Advance(TimeSpan.FromMinutes(6));
        ActionResult claimed = await forge.ClaimAsync(Player);
        Assert.True(claimed.Ok, claimed.Title);
        Assert.Contains("大师锻造券生效", string.Join(" ", claimed.Lines));

        // 券被消耗，产物换成史诗（tier 4）
        List<PlayerItem> bag = await db.ItemsAsync(Player);
        Assert.DoesNotContain(bag, item => item.Name == ForgeService.MasterTicketName);
        Assert.Contains(bag, item => item.Quality >= 4);
    }

    private sealed class ForgeDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public FakeClock Clock { get; }

        public ForgeDatabase()
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            Clock = new FakeClock(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<ApiKeyAuthenticator>();
            services.AddGameplayRuntime(Clock);
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IForgeService, ForgeService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task CreatePlayerAsync(long userId)
            => await Get<IPlayerService>().GetSnapshotAsync(userId);

        public async Task AddItemAsync(long userId, string name, int quantity)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await ItemStore.AddAsync(context, userId, name, 1, quantity, CancellationToken.None, "test");
            await context.SaveChangesAsync();
        }

        public async Task<List<PlayerItem>> ItemsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return await ItemStore.ListAsync(context, userId, CancellationToken.None);
        }

        public async Task<double> CreditsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId);
            return wallet.Credits;
        }

        public async Task<double> ForgePointsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId);
            return wallet.ForgePoints;
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
