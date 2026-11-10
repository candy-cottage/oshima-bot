using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>P2 经济仓储：跨用户交易、托管、双边账本、定时结算与排行榜。</summary>
public sealed class EconomyTests
{
    private const long Seller = 42;
    private const long Buyer = 43;

    [Fact]
    public async Task Store_LazySeedsAndBuyWritesBalancedLedger()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        IReadOnlyList<StoreEntry> goods = await economy.GetStoreAsync(Seller);
        Assert.Equal(GameplayRules.SeedStoreGoods.Count, goods.Count);
        Assert.Equal(1, goods[0].Index);

        ActionResult buy = await economy.BuyAsync(Seller, 1, 2);
        Assert.True(buy.Ok, buy.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        PlayerState state = await dbContext.PlayerStates.FirstAsync(x => x.UserId == Seller);
        LedgerEntry entry = await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "store_buy");
        Assert.Equal(-goods[0].Price * 2, entry.Delta);
        Assert.Equal(state.Credits, entry.BalanceAfter);
        // 同名同品质物品会堆叠成一行，数量累加。
        Assert.Equal(2, (await dbContext.ItemInstances.SingleAsync(
            x => x.UserId == Seller && x.CatalogId == "store")).Quantity);
    }

    [Fact]
    public async Task Store_InsufficientCreditsRollsBackEverything()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await economy.GetStoreAsync(Seller);
        await db.Get<IPlayerService>().GetSnapshotAsync(Seller); // 懒建 player_state，便于设置余额
        await SetCreditsAsync(db, Seller, 0);

        ActionResult result = await economy.BuyAsync(Seller, 1, 1);

        Assert.False(result.Ok);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Empty(await dbContext.LedgerEntries.Where(x => x.Reason == "store_buy").ToListAsync());
        Assert.Equal(0, await dbContext.ItemInstances.CountAsync(x => x.UserId == Seller && x.CatalogId == "store"));
        Assert.Equal(GameplayRules.SeedStoreGoods[0].Stock,
            (await dbContext.StoreGoods.OrderBy(x => x.Id).FirstAsync()).Stock);
    }

    [Fact]
    public async Task Market_CrossUserTradeHasTwoBalancedLedgerEntries()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller); // 建档 + 初始 3 件物品

        ActionResult listed = await economy.ListOnMarketAsync(Seller, 1, 200);
        Assert.True(listed.Ok, listed.Title);
        Assert.Equal(GameplayRules.StartingItems - 1, (await players.GetBagAsync(Seller)).Count); // 已托管

        IReadOnlyList<MarketEntry> market = await economy.GetMarketAsync(Buyer);
        Assert.Single(market);
        Assert.False(market[0].IsMine);
        Assert.Equal(GameplayRules.MarketFee(200), market[0].Fee);

        ActionResult bought = await economy.BuyMarketAsync(Buyer, 1);
        Assert.True(bought.Ok, bought.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        PlayerState sellerState = await dbContext.PlayerStates.FirstAsync(x => x.UserId == Seller);
        PlayerState buyerState = await dbContext.PlayerStates.FirstAsync(x => x.UserId == Buyer);
        Assert.Equal(sellerState.Credits,
            (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_sale")).BalanceAfter);
        Assert.Equal(buyerState.Credits,
            (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_buy")).BalanceAfter);
        Assert.Equal(200, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_sale")).Delta);
        Assert.Equal(-200, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_buy")).Delta);
        Assert.Equal(1, (await dbContext.MarketListings.FirstAsync()).Status);
        long tradedItemId = (await dbContext.MarketListings.FirstAsync()).ItemInstanceId;
        Assert.Equal(Buyer, (await dbContext.ItemInstances.FirstAsync(x => x.Id == tradedItemId)).UserId);
        Assert.Empty(await economy.GetMarketAsync(Buyer));
    }

    [Fact]
    public async Task Market_CannotBuyOwnListingAndDelistReturnsItem()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);

        Assert.True((await economy.ListOnMarketAsync(Seller, 1, 150)).Ok);
        Assert.False((await economy.BuyMarketAsync(Seller, 1)).Ok);
        Assert.True((await economy.DelistAsync(Seller, 1)).Ok);
        Assert.Equal(GameplayRules.StartingItems, (await players.GetBagAsync(Seller)).Count);
        Assert.Empty(await economy.GetMarketAsync(Seller));

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(2, (await dbContext.MarketListings.FirstAsync()).Status);
    }

    [Fact]
    public async Task Offer_AcceptAndCancelLifecycle()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);

        Assert.True((await economy.CreateOfferAsync(Seller, 1, 300)).Ok);
        Assert.Single(await economy.GetOffersAsync(Buyer));
        Assert.False((await economy.AcceptOfferAsync(Seller, 1)).Ok); // 不能接受自己的报价

        ActionResult accepted = await economy.AcceptOfferAsync(Buyer, 1);
        Assert.True(accepted.Ok, accepted.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(-300, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "offer_buy")).Delta);
        Assert.Equal(300, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "offer_sale")).Delta);
        Assert.Equal(1, (await dbContext.Trades.FirstAsync()).Status);
        Assert.Equal(Buyer, (await dbContext.Trades.FirstAsync()).BuyerUserId);

        Assert.True((await economy.CreateOfferAsync(Seller, 1, 100)).Ok);
        Assert.True((await economy.CancelOfferAsync(Seller, 1)).Ok);
        Assert.Equal(2, (await dbContext.Trades.OrderBy(x => x.Id).LastAsync()).Status);
    }

    [Fact]
    public async Task ForgeAndInstance_RequireExpiryThenDeliverRewards()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await AddMaterialAsync(db, Seller, 3); // @4（初始 3 件之后）

        Assert.True((await economy.ForgeAsync(Seller, 4, 1)).Ok);
        Assert.False((await economy.ClaimForgeAsync(Seller)).Ok);
        await ExpireAsync(db, "forge");
        Assert.True((await economy.ClaimForgeAsync(Seller)).Ok);
        Assert.Contains(await players.GetBagAsync(Seller), item => item.Name.Contains("锻造"));

        Assert.True((await economy.InstanceAsync(Seller, "精英")).Ok);
        Assert.False((await economy.ClaimInstanceAsync(Seller)).Ok);
        await ExpireAsync(db, "instance");
        Assert.True((await economy.ClaimInstanceAsync(Seller)).Ok);
        Assert.Contains(await players.GetBagAsync(Seller), item => item.Name.Contains("秘境掉落"));

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.True((await dbContext.PlayerStates.FirstAsync(x => x.UserId == Seller)).Exp
                    >= GameplayRules.InstanceRewardExp);
        Assert.Single(await dbContext.LedgerEntries.Where(x => x.Reason == "instance_reward").ToListAsync());
    }

    [Fact]
    public async Task Boss_OncePerGameDayAndRankingReflectsPoints()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        Assert.True((await economy.BossAsync(Seller)).Ok);
        Assert.False((await economy.BossAsync(Seller)).Ok);
        Assert.True((await economy.DuelAsync(Seller)).Ok);

        IReadOnlyList<RankEntry> points = await economy.GetRankingAsync("战绩");
        IReadOnlyList<RankEntry> credits = await economy.GetRankingAsync("credits");
        Assert.Single(points);
        Assert.True(points[0].Value >= GameplayRules.BossWinPoints);
        Assert.NotEmpty(credits);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Single(await dbContext.AppConfigs.Where(x => x.Key.StartsWith("boss:")).ToListAsync());
        Assert.Equal(2, await dbContext.LedgerEntries.CountAsync(x => x.Reason == "boss_reward"));
    }

    [Fact]
    public async Task Duel_RunsRealBattleInsteadOfDiceRoll()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Seller);   // 建档并发放初始角色

        ActionResult result = await economy.DuelAsync(Seller);

        Assert.True(result.Ok, result.Title);
        Assert.Contains(result.Lines, line => line.Contains("回合"));
        Assert.Contains(result.Lines, line => line.Contains("HP"));
        Assert.Contains(result.Lines, line => line.Contains("战斗摘要"));
    }

    [Fact]
    public async Task NoIdentity_NeverWritesEconomyData()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        Assert.False((await economy.BuyAsync(0, 1, 1)).Ok);
        Assert.False((await economy.ListOnMarketAsync(0, 1, 100)).Ok);
        Assert.False((await economy.BossAsync(0)).Ok);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(0, await dbContext.MarketListings.CountAsync());
        Assert.Equal(0, await dbContext.Trades.CountAsync());
    }

    private static async Task SetCreditsAsync(EconomyDatabase db, long userId, long credits)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        PlayerState state = await context.PlayerStates.FirstAsync(x => x.UserId == userId);
        state.Credits = credits;
        await context.SaveChangesAsync();
    }

    private static async Task AddMaterialAsync(EconomyDatabase db, long userId, int quantity)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        context.ItemInstances.Add(new ItemInstance
        {
            UserId = userId, CatalogId = "test", Quality = 1, Quantity = quantity,
            PayloadJson = "{\"name\":\"测试材料\"}"
        });
        await context.SaveChangesAsync();
    }

    private static async Task ExpireAsync(EconomyDatabase db, string kind)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        if (kind == "forge")
        {
            ForgingJob job = await context.ForgingJobs.FirstAsync();
            job.EndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
        }
        else
        {
            ExplorationRun run = await context.ExplorationRuns.FirstAsync();
            run.EndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
        }
        await context.SaveChangesAsync();
    }

    private sealed class EconomyDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public EconomyDatabase()
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
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IEconomyService, EconomyService>();
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
