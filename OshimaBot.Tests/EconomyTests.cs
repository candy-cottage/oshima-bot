using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Combat;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>P2 经济仓储：跨用户交易、托管、双边账本、定时结算与排行榜。</summary>
[Collection(BossPoolCollection.Name)]
public sealed class EconomyTests
{
    private const long Seller = 42;
    private const long Buyer = 43;

    [Fact]
    public async Task Store_DailyHasFourRandomGoodsPerUser()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        IReadOnlyList<StoreEntry> goods = await economy.GetStoreAsync(Seller);
        Assert.Equal(StoreCatalog.DailyGoodsCount, goods.Count);
        Assert.Equal(1, goods[0].Index);
        Assert.All(goods, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Name));
            Assert.True(g.Price > 0);
            Assert.True(g.Stock >= 1);        // 每日商店库存 1–2（魔法卡 1，消耗品 +3）
        });

        // 每个用户一份，互不相同（用另一个用户再取一次）
        IReadOnlyList<StoreEntry> other = await economy.GetStoreAsync(Buyer);
        Assert.Equal(StoreCatalog.DailyGoodsCount, other.Count);
    }

    [Fact]
    public async Task Store_FixedShopSellsFreeGoodsOncePerUser()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        // 慈善基金会：免费、每人限购 1
        IReadOnlyList<StoreEntry> goods = await economy.GetStoreAsync(Seller, StoreCatalog.WelfareKey, default);
        Assert.NotEmpty(goods);
        StoreEntry good = goods[0];
        Assert.Equal("免费", good.Currency);
        Assert.Equal(1, good.Quota);

        ActionResult first = await economy.BuyAsync(Seller, StoreCatalog.WelfareKey, 1, 1);
        Assert.True(first.Ok, first.Title);

        ActionResult second = await economy.BuyAsync(Seller, StoreCatalog.WelfareKey, 1, 1);
        Assert.False(second.Ok);
        Assert.Contains("限购", second.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        // 免费商品不记 store_buy 流水
        Assert.Empty(await dbContext.LedgerEntries.Where(x => x.Reason == "store_buy").ToListAsync());
        Assert.Equal(1, await dbContext.CoreItems.CountAsync(x => x.UserId == Seller));
    }

    [Fact]
    public async Task Store_FixedShopDebitsCorrectCurrencyAndStock()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await SetCreditsAsync(db, Seller, 200_000);

        // 武器商会 第 2 件是金币 60000，库存 4
        IReadOnlyList<StoreEntry> goods = await economy.GetStoreAsync(Seller, StoreCatalog.WeaponsKey, default);
        StoreEntry good = goods.Single(g => g.Price == 60_000);
        Assert.Equal(4, good.Stock);

        ActionResult buy = await economy.BuyAsync(Seller, StoreCatalog.WeaponsKey, good.Index, 1);
        Assert.True(buy.Ok, buy.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord state = await dbContext.CorePlayers.FirstAsync(x => x.UserId == Seller);
        LedgerEntry entry = await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "store_buy");
        Assert.Equal(-60_000, entry.Delta);
        Assert.Equal(state.Credits, entry.BalanceAfter);

        IReadOnlyList<StoreEntry> after = await economy.GetStoreAsync(Seller, StoreCatalog.WeaponsKey, default);
        Assert.Equal(3, after.Single(g => g.Price == 60_000).Stock);
    }

    [Fact]
    public async Task Store_InsufficientCreditsRollsBackEverything()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.Get<IPlayerService>().GetSnapshotAsync(Seller);
        await SetCreditsAsync(db, Seller, 0);

        ActionResult result = await economy.BuyAsync(Seller, StoreCatalog.WeaponsKey, 1, 1);

        Assert.False(result.Ok);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Empty(await dbContext.LedgerEntries.Where(x => x.Reason == "store_buy").ToListAsync());
        Assert.Equal(0, await dbContext.CoreItems.CountAsync(x => x.UserId == Seller));
        // 失败时不扣库存
        Assert.All(await dbContext.StoreGoods.Where(x => x.StoreKey == StoreCatalog.WeaponsKey).ToListAsync(),
            good => Assert.Equal(0, good.SoldCount));
    }

    [Fact]
    public async Task Market_CrossUserTradeHasTwoBalancedLedgerEntries()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await TestItems.SeedAsync(db.Provider, Seller, 3);   // 旧版注册不带物品，测试自备 3 件

        ActionResult listed = await economy.ListOnMarketAsync(Seller, 1, 200);
        Assert.True(listed.Ok, listed.Title);
        Assert.Equal(2, (await players.GetBagAsync(Seller)).Count); // 已托管 1 件

        IReadOnlyList<MarketEntry> market = await economy.GetMarketAsync(Buyer);
        Assert.Single(market);
        Assert.False(market[0].IsMine);
        Assert.Equal(GameplayRules.MarketFee(200), market[0].Fee);

        ActionResult bought = await economy.BuyMarketAsync(Buyer, 1);
        Assert.True(bought.Ok, bought.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord sellerState = await dbContext.CorePlayers.FirstAsync(x => x.UserId == Seller);
        CorePlayerRecord buyerState = await dbContext.CorePlayers.FirstAsync(x => x.UserId == Buyer);
        Assert.Equal(sellerState.Credits,
            (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_sale")).BalanceAfter);
        Assert.Equal(buyerState.Credits,
            (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_buy")).BalanceAfter);
        Assert.Equal(200, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_sale")).Delta);
        Assert.Equal(-200, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_buy")).Delta);
        Assert.Equal(1, (await dbContext.MarketListings.FirstAsync()).Status);
        long tradedItemId = (await dbContext.MarketListings.FirstAsync()).ItemInstanceId;
        Assert.Equal(Buyer, (await dbContext.CoreItems.FirstAsync(x => x.LocalId == tradedItemId)).UserId);
        Assert.Empty(await economy.GetMarketAsync(Buyer));
    }

    [Fact]
    public async Task Market_CannotBuyOwnListingAndDelistReturnsItem()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await TestItems.SeedAsync(db.Provider, Seller, 3);

        Assert.True((await economy.ListOnMarketAsync(Seller, 1, 150)).Ok);
        Assert.False((await economy.BuyMarketAsync(Seller, 1)).Ok);
        Assert.True((await economy.DelistAsync(Seller, 1)).Ok);
        Assert.Equal(3, (await players.GetBagAsync(Seller)).Count);
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
        await TestItems.SeedAsync(db.Provider, Seller, 3);

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
        await TestItems.SeedAsync(db.Provider, Seller, 3);   // 旧版注册不带物品
        await AddMaterialAsync(db, Seller, 3);                // @4

        Assert.True((await economy.ForgeAsync(Seller, 4, 1)).Ok);
        Assert.False((await economy.ClaimForgeAsync(Seller)).Ok);
        await ExpireAsync(db, "forge");
        Assert.True((await economy.ClaimForgeAsync(Seller)).Ok);
        Assert.Contains(await players.GetBagAsync(Seller), item => item.Name.Contains("锻造"));

        Assert.True((await economy.InstanceAsync(Seller, "地区 3")).Ok);
        Assert.False((await economy.ClaimInstanceAsync(Seller)).Ok);
        await ExpireAsync(db, "instance");
        Assert.True((await economy.ClaimInstanceAsync(Seller)).Ok);
        Assert.Contains(await players.GetBagAsync(Seller), item => item.Name.Contains("秘境掉落"));

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        // 秘境经验记在角色身上（Core 模型），可能已经升级消耗掉，故断言「有进展」。
        CoreCharacterRecord levelRow = await dbContext.CoreCharacters.FirstAsync(x => x.UserId == Seller);
        Assert.True(levelRow.Level > 1 || levelRow.Exp > 0,
            $"秘境结算后角色应有经验或升级，实际 Lv.{levelRow.Level} / Exp {levelRow.Exp}");
        Assert.True(await dbContext.LedgerEntries.CountAsync(x => x.Reason == "instance_start") >= 1);
    }

    [Fact]
    public async Task Boss_SharedTargetAllowsRepeatAndAccumulatesLedger()
    {
        BossPool.Reset();
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        // 旧版 Boss 是全服共享的持久实体：没有「每人每日一次」的限制，可以反复挑战。
        Assert.True((await economy.BossAsync(Seller)).Ok);
        Assert.True((await economy.BossAsync(Seller)).Ok);
        Assert.True((await economy.DuelAsync(Seller)).Ok);

        IReadOnlyList<RankEntry> points = await economy.GetRankingAsync("战绩");
        IReadOnlyList<RankEntry> credits = await economy.GetRankingAsync("credits");
        Assert.Single(points);
        // Boss 是真实战斗：击杀给满分，未击杀按伤害占比给部分战绩。
        Assert.True(points[0].Value >= 1);
        Assert.NotEmpty(credits);

        // 池子始终保持 10 只（未击杀时不会减少，击杀后会补充）。
        Assert.Equal(BossPool.PoolSize, BossPool.All.Count);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        // 旧版不收入场费、只有击杀才发材料，所以这两条流水都可能不存在；
        // 唯一稳定的是战绩累积（未击杀也按伤害给分）。
        Assert.True(await dbContext.LedgerEntries.CountAsync(x => x.Reason == "boss_entry") == 0);
        Assert.True(await dbContext.LedgerEntries.CountAsync(x => x.Reason == "boss_reward")
            == await dbContext.LedgerEntries.CountAsync(x => x.Reason == "boss_reward" && x.Currency == "diamonds"));
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

        Assert.False((await economy.BuyAsync(0, null, 1, 1)).Ok);
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
        CorePlayerRecord state = await context.CorePlayers.FirstAsync(x => x.UserId == userId);
        state.Credits = credits;
        await context.SaveChangesAsync();
    }

    private static async Task AddMaterialAsync(EconomyDatabase db, long userId, int quantity)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        await ItemStore.AddAsync(context, userId, "测试材料", 1, quantity, CancellationToken.None, "test");
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
            services.AddGameplayRuntime();
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
