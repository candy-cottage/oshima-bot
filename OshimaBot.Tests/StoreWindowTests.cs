using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FunGame.Core.Library.Constant;
using OshimaBot.Data;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 商店的下单窗口与商品详情（旧版「商店查看」+ 2 分钟 <c>UserLastVisitStore</c>）。
/// <para>窗口存在的理由：商店用序号指代，连着看几家后很容易买错，所以超时要求重新打开。</para>
/// </summary>
public sealed class StoreWindowTests
{
    private const long Player = 42;

    [Fact]
    public async Task Buy_RequiresAFreshlyOpenedStore()
    {
        using StoreDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreatePlayerAsync(Player);

        // 从没打开过商店：直接下单被拦
        ActionResult cold = await economy.BuyAsync(Player, null, 1, 1);
        Assert.False(cold.Ok);
        Assert.Contains("窗口已过期", cold.Title);

        // 打开慈善基金会（免费、限购 1）后可以下单
        IReadOnlyList<StoreEntry> goods = await economy.GetStoreAsync(Player, StoreCatalog.WelfareKey);
        Assert.NotEmpty(goods);
        ActionResult ok = await economy.BuyAsync(Player, null, 1, 1);
        Assert.True(ok.Ok, ok.Title);

        // 超过 2 分钟窗口：要求重新打开
        db.Clock.Advance(TimeSpan.FromMinutes(GameplayRules.StoreWindowMinutes + 1));
        ActionResult expired = await economy.BuyAsync(Player, null, 1, 1);
        Assert.False(expired.Ok);
        Assert.Contains("窗口已过期", expired.Title);

        // 重新打开就能继续买（换一家不受限购影响的店）
        await economy.GetStoreAsync(Player, StoreCatalog.LogisticsKey);
        await economy.GetStoreAsync(Player, StoreCatalog.LogisticsKey);
        Assert.Equal(StoreCatalog.LogisticsKey, await db.LastStoreKeyAsync(Player));
    }

    [Fact]
    public async Task Detail_ShowsGoodsStockQuotaAndWindow()
    {
        using StoreDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreatePlayerAsync(Player);

        await economy.GetStoreAsync(Player, StoreCatalog.WelfareKey);
        StoreDetail? detail = await economy.GetStoreDetailAsync(Player, 1);

        Assert.NotNull(detail);
        Assert.Equal(1, detail!.Index);
        Assert.Equal(StoreCatalog.WelfareKey, detail.StoreKey);
        Assert.Equal("铎京慈善基金会", detail.StoreName);
        Assert.False(string.IsNullOrWhiteSpace(detail.Name));
        Assert.Equal("免费", detail.Currency);
        Assert.True(detail.WindowEndsAtUtc > db.Clock.UtcNow);

        // 编号越界返回 null，命令层据此给「商品不存在」
        Assert.Null(await economy.GetStoreDetailAsync(Player, 99));
        Assert.Null(await economy.GetStoreDetailAsync(Player, 0));
    }

    [Fact]
    public async Task Sell_PaysRecyclePriceAndSkipsLockedItems()
    {
        using StoreDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreatePlayerAsync(Player);

        // 入包时就带上回收价（优质 = (800+4200)/2 × 0.35 = 875）
        await db.AddItemAsync(Player, "优质矿石", quality: 2, quantity: 2);
        await db.AddItemAsync(Player, "上锁矿石", quality: 2, quantity: 1);
        await db.SetLockAsync(Player, "上锁矿石");

        double before = await db.CreditsAsync(Player);
        ActionResult sold = await economy.SellToStoreAsync(Player, [1]);

        Assert.True(sold.Ok, sold.Title);
        Assert.Equal(before + GameplayRules.DefaultItemPrice(QualityType.Green), await db.CreditsAsync(Player), 3);
        Assert.Equal(1, (await db.ItemsAsync(Player)).Count(x => x.Name == "优质矿石"));  // 卖了一件

        // 锁定的卖不掉
        ActionResult locked = await economy.SellToStoreAsync(Player, [2]);
        Assert.False(locked.Ok);
        Assert.Contains("锁定", locked.Lines[0]);

        // 越界编号也不会崩
        Assert.False((await economy.SellToStoreAsync(Player, [99])).Ok);
        Assert.False((await economy.SellToStoreAsync(Player, [])).Ok);
    }

    private sealed class StoreDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public FakeClock Clock { get; }

        public StoreDatabase()
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
            services.AddSingleton<IEconomyService, EconomyService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public Task CreatePlayerAsync(long userId) => Get<IPlayerService>().GetSnapshotAsync(userId);


        public async Task AddItemAsync(long userId, string name, int quality, int quantity)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await ItemStore.AddAsync(context, userId, name, quality, quantity, CancellationToken.None, "test");
            await context.SaveChangesAsync();
        }

        public async Task<List<PlayerItem>> ItemsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return await ItemStore.ListAsync(context, userId, CancellationToken.None);
        }

        public async Task SetLockAsync(long userId, string name)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            List<PlayerItem> items = await ItemStore.ListAsync(context, userId, CancellationToken.None);
            PlayerItem item = items.First(x => x.Name == name);
            item.Locked = true;
            ItemStore.Sync(item);
            await context.SaveChangesAsync();
        }

        public async Task<double> CreditsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return (await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId)).Credits;
        }

        public async Task<string> LastStoreKeyAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return (await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId)).LastStoreKey;
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
