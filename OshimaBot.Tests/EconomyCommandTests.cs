using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OshimaBot.Commands;
using OshimaBot.Data;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>P2 指令层：真实 SQLite + CommandRouter 的端到端验证。</summary>
[Collection(BossPoolCollection.Name)]
public sealed class EconomyCommandTests
{
    private const long UserId = 42;

    [Fact]
    public void Registry_ResolvesP2VerbsAndKeepsPurchaseDistinct()
    {
        using EconomyCommandDatabase db = new();
        CommandRegistry registry = CommandCatalog.Build(db.Dependencies);

        Assert.Equal(85, registry.All.Count);
        Assert.Equal("shop", registry.Resolve("/商店")!.Command.Key);
        Assert.Equal("buy", registry.Resolve("/购买 @1 x2")!.Command.Key);
        Assert.Equal(new[] { "@1", "x2" }, registry.Resolve("/购买 @1 x2")!.Args);
        Assert.Equal("market-buy", registry.Resolve("/购买市场 @1")!.Command.Key);
        Assert.Equal("boss", registry.Resolve("/Boss")!.Command.Key);
        Assert.Equal("rank", registry.Resolve("/排行榜 战绩")!.Command.Key);
        Assert.Equal("list-item", registry.Resolve("/上架 @1 200")!.Command.Key);
    }

    [Fact]
    public async Task Router_StoreBuyAndMarketLifecycle()
    {
        using EconomyCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        string daily = await RouteAsync(router, "/商店");
        Assert.Contains("每日商店", daily);
        Assert.Contains("4 种商品", daily);

        // 慈善基金会（/商店4）是免费 + 每人限购 1，用它验证购买链路最稳
        string shop = await RouteAsync(router, "/商店4");
        Assert.Contains("@1", shop);
        Assert.Contains("免费", shop);

        Assert.Contains("购买成功", await RouteAsync(router, "/购买 @1"));
        Assert.Contains("上架成功", await RouteAsync(router, "/上架 @1 200"));
        Assert.Contains("200 金币", await RouteAsync(router, "/市场"));
        Assert.Contains("不能购买自己的挂单", await RouteAsync(router, "/购买市场 @1"));
        Assert.Contains("已下架", await RouteAsync(router, "/下架 @1"));
        Assert.Contains("市场暂时空着", await RouteAsync(router, "/市场"));
    }

    [Fact]
    public async Task Router_OffersInstanceBossDuelAndRanking()
    {
        using EconomyCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();
        await TestItems.SeedAsync(db.Provider, UserId, 3);   // 旧版注册不带物品

        Assert.Contains("报价单已发布", await RouteAsync(router, "/报价 @1 300"));
        Assert.Contains("300 金币", await RouteAsync(router, "/报价列表"));
        Assert.Contains("不能接受自己的报价单", await RouteAsync(router, "/接受报价 @1"));
        Assert.Contains("报价已撤回", await RouteAsync(router, "/撤回报价 @1"));

        Assert.Contains("材料数量不足", await RouteAsync(router, "/锻造 @1 1"));
        Assert.Contains("进入秘境", await RouteAsync(router, "/秘境 精英"));
        Assert.Contains("尚未结束", await RouteAsync(router, "/收取秘境"));

        // Boss 是全服共享的持久目标：没有每日次数限制，重复挑战会继续累计伤害。
        string bossReply = await RouteAsync(router, "/Boss");
        Assert.Contains("Boss 讨伐", bossReply);
        Assert.Contains("全服共享", bossReply);
        Assert.Contains("决斗", await RouteAsync(router, "/决斗"));

        string rank = await RouteAsync(router, "/排行榜 战绩");
        Assert.Contains("战绩榜", rank);
        Assert.Contains("@1", rank);
    }

    [Fact]
    public async Task Router_LookAndHelpReflectP2()
    {
        using EconomyCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        string look = await RouteAsync(router, "/观察");
        Assert.Contains("补给站", look);
        Assert.Contains("交易摊", look);

        int lastPage = (CommandCatalog.Keys.Count + CommandReplies.HelpPageSize - 1) / CommandReplies.HelpPageSize;
        string help = await RouteAsync(router, $"/帮助 p{lastPage}");
        Assert.Contains($"{lastPage}/{lastPage}", help);
        Assert.Contains("经济战斗", help);

        string legacy = await RouteAsync(router, "/玩法帮助");
        Assert.Contains("P0", legacy);
        Assert.Contains("P1", legacy);
        Assert.Contains("P2", legacy);
    }

    private static async Task<string> RouteAsync(CommandRouter router, string command)
    {
        BotReply? reply = await router.RouteAsync(Message(command), new OtherData());
        Assert.NotNull(reply);
        return reply!.Markdown?.Content ?? reply.Text ?? "";
    }

    private static ThirdPartyMessage Message(string detail)
        => new()
        {
            Id = "MSG-P2", Detail = detail, IsGroup = false, OpenId = "USER",
            AuthorOpenId = "USER", FunGameUID = UserId, Timestamp = "2026-11-10 00:40:00"
        };

    private sealed class EconomyCommandDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public CommandDependencies Dependencies { get; }

        public EconomyCommandDatabase()
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<IUserSessionService, UserSessionService>();
            services.AddGameplayRuntime();
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IEconomyService, EconomyService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();

            Dependencies = new CommandDependencies(
                null!, null!, null!,
                Provider.GetRequiredService<IUserSessionService>(),
                Provider.GetRequiredService<IPlayerService>(),
                Provider.GetRequiredService<IEconomyService>(),
                NullLogger.Instance);
        }

        public CommandRouter CreateRouter() => new(
            CommandCatalog.Build(Dependencies), Dependencies,
            Provider.GetRequiredService<IServiceScopeFactory>(),
            Provider.GetRequiredService<IMemoryCache>(),
            NullLogger<CommandRouter>.Instance);

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
