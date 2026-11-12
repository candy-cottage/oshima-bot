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
