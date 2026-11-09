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

/// <summary>P1 指令层：显式注册表 + CommandRouter + 真实玩法仓储的端到端验证。</summary>
public sealed class GameplayCommandTests
{
    private const long UserId = 42;

    [Fact]
    public void Registry_RegistersP1VerbsWithoutBreakingP0Aliases()
    {
        using GameplayCommandDatabase db = new();
        CommandRegistry registry = CommandCatalog.Build(db.Dependencies);

        Assert.Equal(49, registry.All.Count);
        Assert.Equal("status", registry.Resolve("/状态")!.Command.Key);
        Assert.Equal("status", registry.Resolve("存档")!.Command.Key);
        Assert.Equal("gacha-ten", registry.Resolve("/十连抽卡")!.Command.Key);
        Assert.Equal("char", registry.Resolve("查角色 1")!.Command.Key); // 旧别名未被「我的角色」抢走
        Assert.Equal("characters", registry.Resolve("/我的角色")!.Command.Key);

        CommandMatch party = registry.Resolve("/小队 加入 @2")!;
        Assert.Equal("party", party.Command.Key);
        Assert.Equal(new[] { "加入", "@2" }, party.Args);
        Assert.Equal("x10", Assert.Single(registry.Resolve("/抽卡 x10")!.Args));
        Assert.Equal("@3", Assert.Single(registry.Resolve("/领奖 @3")!.Args));
        Assert.Equal(2, registry.Resolve("/背包 p2")!.Page);
    }

    [Fact]
    public async Task Router_SignInQuestsAndClaimChainWorks()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        string signin = await RouteAsync(router, "/签到");
        string quests = await RouteAsync(router, "/每日任务");
        string claim = await RouteAsync(router, "/领奖 @1");
        string repeat = await RouteAsync(router, "/领奖 @1");
        string status = await RouteAsync(router, "/状态");

        Assert.Contains("签到成功", signin);
        Assert.Contains("可领取", quests);
        Assert.Contains("任务奖励已领取", claim);
        Assert.Contains("已领取", repeat);
        Assert.Contains("指令未能完成", repeat);
        Assert.Contains("旅人档案", status);
        Assert.Contains("金币", status);
    }

    [Fact]
    public async Task Router_BagReferencesLockAndSalvageWork()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        Assert.Contains("抽卡", await RouteAsync(router, "/抽卡 3"));
        string bag = await RouteAsync(router, "/背包");
        Assert.Contains("@1", bag);
        Assert.Contains("物品已锁定", await RouteAsync(router, "/锁定 @1"));
        Assert.Contains("锁定或绑定", await RouteAsync(router, "/分解 @1"));
        Assert.Contains("物品已解锁", await RouteAsync(router, "/解锁 @1"));
        Assert.Contains("分解完成", await RouteAsync(router, "/分解 @1"));
    }

    [Fact]
    public async Task Router_NoIdentityReturnsStructuredError()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        BotReply? reply = await router.RouteAsync(Message("/签到", userId: 0), new OtherData());

        Assert.NotNull(reply);
        string content = reply!.Markdown!.Content!;
        Assert.Contains("身份尚未就绪", content);
        Assert.Contains("没有发生", content);
        Assert.Contains("下一步", content);
    }

    [Fact]
    public async Task Help_LastPageContainsP1Group()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        string content = await RouteAsync(router, "/帮助 p9");

        Assert.Contains("9/9", content);
        bool hasP1 = content.Contains("/状态", StringComparison.Ordinal)
                     || content.Contains("/抽卡", StringComparison.Ordinal)
                     || content.Contains("/每日任务", StringComparison.Ordinal)
                     || content.Contains("旅人存档", StringComparison.Ordinal);
        Assert.True(hasP1, content);
    }

    [Fact]
    public void FromResult_RendersSuccessAndFailureDifferently()
    {
        string ok = CommandReplies.FromResult(ActionResult.Success("标题", "状态", ["- 行"], ["/帮助"]))
            .Markdown!.Content!;
        string fail = CommandReplies.FromResult(ActionResult.Fail("发生了什么", "没有发生什么", "下一步", "/帮助"))
            .Markdown!.Content!;

        Assert.Contains("## 标题", ok);
        Assert.Contains("状态", ok);
        Assert.Contains("指令未能完成", fail);
        Assert.Contains("没有发生", fail);
        Assert.Contains("下一步", fail);
    }

    [Fact]
    public async Task Router_LookAndLegacyHelpReflectMigratedP1()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        string look = await RouteAsync(router, "/观察");
        string legacy = await RouteAsync(router, "/玩法帮助");

        Assert.Contains("中央广场", look);
        Assert.Contains("P1", legacy);
        Assert.Contains("商店", legacy);
    }

    private static async Task<string> RouteAsync(CommandRouter router, string command)
    {
        BotReply? reply = await router.RouteAsync(Message(command), new OtherData());
        Assert.NotNull(reply);
        return reply!.Markdown?.Content ?? reply.Text ?? "";
    }

    private static ThirdPartyMessage Message(string detail, long userId = UserId)
        => new()
        {
            Id = "MSG-P1", Detail = detail, IsGroup = false, OpenId = "USER",
            AuthorOpenId = "USER", FunGameUID = userId, Timestamp = "2026-11-10 00:00:00"
        };

    private sealed class GameplayCommandDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public CommandDependencies Dependencies { get; }

        public GameplayCommandDatabase()
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
            services.AddSingleton<IPlayerService, PlayerService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();

            Dependencies = new CommandDependencies(
                null!, null!, null!,
                Provider.GetRequiredService<IUserSessionService>(),
                Provider.GetRequiredService<IPlayerService>(),
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
