using System.Text.RegularExpressions;
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
using OshimaBot.Services;
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

        Assert.Equal(137, registry.All.Count);
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
        await TestItems.SeedAsync(db.Provider, UserId, 3);   // 旧版注册不带物品

        Assert.Contains("抽卡", await RouteAsync(router, "/抽卡 3"));
        string bag = await RouteAsync(router, "/背包");
        Assert.Contains("@1", bag);
        Assert.Contains("物品已锁定", await RouteAsync(router, "/锁定 @1"));
        Assert.Contains("锁定或绑定", await RouteAsync(router, "/分解 @1"));
        Assert.Contains("物品已解锁", await RouteAsync(router, "/解锁 @1"));
        Assert.Contains("分解完成", await RouteAsync(router, "/分解 @1"));
    }

    [Fact]
    public async Task Router_ItemExpansionCommandsAreReachable()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();
        await TestItems.SeedAsync(db.Provider, UserId, 2);
        string name = FunGameService.AllItems[0].Name;

        string hit = await RouteAsync(router, $"/库存搜索 {name}");
        Assert.Contains(name, hit);
        Assert.Contains("没有名称或描述包含", await RouteAsync(router, "/库存搜索 不存在的关键词"));
        Assert.Contains("钻石不足", await RouteAsync(router, "/兑换金币 10"));
        Assert.Contains("魔法卡", await RouteAsync(router, "/合成魔法卡 @1 @2 @3"));
        Assert.Contains("品质序号", await RouteAsync(router, "/品质分解 9"));
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
    public async Task Help_LastPageContainsP2Group()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();
        int lastPage = (CommandCatalog.Keys.Count + CommandReplies.HelpPageSize - 1) / CommandReplies.HelpPageSize;

        string content = await RouteAsync(router, $"/帮助 p{lastPage}");

        Assert.Contains($"{lastPage}/{lastPage}", content);
        Assert.Contains($"共 {CommandCatalog.Keys.Count} 条指令", content);
        bool hasP2 = content.Contains("/排行榜", StringComparison.Ordinal)
                     || content.Contains("经济战斗", StringComparison.Ordinal);
        Assert.True(hasP2, content);
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
    public async Task Router_RoomCommandsRequireGroupAndWorkEndToEnd()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        // 私聊：多人房间明确拒绝
        Assert.Contains("群里", await RouteAsync(router, "/创建房间 混战"));

        // 群聊：建 → 列表 → 信息 → 人数不足 → 退出
        string created = await RouteGroupAsync(router, "/创建房间 混战");
        Assert.Contains("房间创建成功", created);
        string code = Regex.Match(created, @"\[ ([A-Z0-9]{7}) \]").Groups[1].Value;
        Assert.Equal(RoomRules.CodeLength, code.Length);

        Assert.Contains(code, await RouteGroupAsync(router, "/房间列表"));
        Assert.Contains("等待开局", await RouteGroupAsync(router, "/房间信息"));
        Assert.Contains("人数不足", await RouteGroupAsync(router, "/开始游戏"));
        Assert.Contains("已离开房间", await RouteGroupAsync(router, "/退出房间"));
        Assert.Contains("暂无房间", await RouteGroupAsync(router, "/房间列表"));
    }

    [Fact]
    public async Task Router_ExploreCommandsAreReachable()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        await RouteAsync(router, "/我的角色");          // 先建档，探索需要角色
        string info = await RouteAsync(router, "/探索信息");
        Assert.Contains("探索许可", info);
        Assert.Contains("60", info);
        Assert.Contains("没有进行中的探索", info);

        // 缺成员 / 缺地区编号都给用法
        Assert.Contains("没有指定探索成员", await RouteAsync(router, "/探索 1"));
        Assert.Contains("缺少地区编号", await RouteAsync(router, "/探索"));

        // 1 人 5 星 = 5 许可
        string started = await RouteAsync(router, "/探索 1 1");
        Assert.Contains("出发探索", started);
        Assert.Contains("剩余 55", started);

        // 角色被锁 & 结算没到时间
        Assert.Contains("正在探索", await RouteAsync(router, "/探索 1 1"));
        Assert.Contains("还没结束", await RouteAsync(router, "/探索结算"));
        Assert.Contains("还需", await RouteAsync(router, "/探索信息"));
    }

    [Fact]
    public async Task Router_WorldMapCommandsAreReachable()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        string map = await RouteAsync(router, "/世界地图");
        Assert.Contains("铎京城", map);
        Assert.Contains("银辉城", map);
        Assert.Contains("泰坦遗迹", map);
        Assert.Contains("13 个地区", map);

        // 编号 / 名称 / 不带参数三种写法
        string byId = await RouteAsync(router, "/查地区 1");
        Assert.Contains("星银合金", byId);
        Assert.Contains("失控的悖论引擎", byId);
        Assert.Contains("探索剧情", byId);
        Assert.Contains(await RouteAsync(router, "/世界地图"), await RouteAsync(router, "/查地区"));

        Assert.Contains("永燃坩埚", await RouteAsync(router, "/查地区 永燃坩埚"));
        Assert.Contains("找不到", await RouteAsync(router, "/查地区 99"));
        Assert.Contains("铎京城", await RouteAsync(router, "/主城"));
    }

    [Fact]
    public async Task Router_ActivityCommandsAreReachable()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        string list = await RouteAsync(router, "/活动");
        Assert.Contains("旅人试炼", list);
        Assert.Contains("周末疾行", list);
        Assert.Contains("双旦活动", list);

        string detail = await RouteAsync(router, "/查活动 1");
        Assert.Contains("物资征调", detail);
        Assert.Contains("以币换印", detail);

        // 新号只有 5000 金币，做不了 15000 的任务
        Assert.Contains("金币不足", await RouteAsync(router, "/做活动任务 1 2"));
        // 没做满不能领奖
        Assert.Contains("还没做满", await RouteAsync(router, "/领取奖励 1 2"));
        // 编号缺失给用法
        Assert.Contains("活动或任务编号缺失", await RouteAsync(router, "/做活动任务"));
        // 公告只有管理员能改
        Assert.Contains("权限不足", await RouteAsync(router, "/设置公告 周末双倍"));
    }

    [Fact]
    public async Task Router_ClubSubsystemIsReachable()
    {
        using GameplayCommandDatabase db = new();
        CommandRouter router = db.CreateRouter();

        // 没社团时三条子域都给明确引导，而不是崩或空回复
        Assert.Contains("没有加入任何社团", await RouteAsync(router, "/社团任务"));
        Assert.Contains("没有加入任何社团", await RouteAsync(router, "/社团商店"));
        Assert.Contains("暂无挂单", await RouteAsync(router, "/社团市场"));

        // 建团后能看到周常三个任务与货架
        Assert.Contains("社团创建成功", await RouteAsync(router, "/创建社团 ABC"));
        string tasks = await RouteAsync(router, "/社团任务");
        Assert.Contains("社团捐献", tasks);
        Assert.Contains("招募新血", tasks);
        Assert.Contains("物资征集", tasks);

        string store = await RouteAsync(router, "/社团商店");
        Assert.Contains("钻石", store);
        Assert.Contains("社团基金", store);

        // 子命令缺参数时给出用法，而不是静默失败
        Assert.Contains("领取", await RouteAsync(router, "/社团任务 领取"));
        Assert.Contains("上架", await RouteAsync(router, "/社团市场 上架"));
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

    /// <summary>
    /// 全指令回归（M1.5 验收项）：每一条注册指令裸调一次，必须给出可读回复，且不能落到「玩法操作出现异常」。
    /// <para>业务失败（缺参数、缺材料、次数已用）是正常回复，不算失败；模拟器指令单独测。</para>
    /// </summary>
    [Fact]
    public async Task EveryRegisteredCommand_RespondsWithoutInternalError()
    {
        using GameplayCommandDatabase db = new();
        CommandRegistry registry = CommandCatalog.Build(db.Dependencies);
        CommandRouter router = db.CreateRouter();

        Assert.Equal(137, registry.All.Count);

        List<string> failures = [];
        int checkedCommands = 0;
        foreach (ICommand command in registry.All)
        {
            // 模拟器指令是完整对局，跑起来慢，且已有专门测试。
            if (command.Key.StartsWith("sim", StringComparison.Ordinal)) continue;

            string verb = command.Aliases.Count > 0 ? command.Aliases[0] : command.Name;
            string input = verb.StartsWith('/') ? verb : "/" + verb;
            string text = await RouteAsync(router, input);
            checkedCommands++;

            if (string.IsNullOrWhiteSpace(text))
            {
                failures.Add($"{command.Key}（{input}）→ 空回复");
            }
            else if (text.Contains("玩法操作出现异常"))
            {
                string head = text.Length > 80 ? text[..80] : text;
                failures.Add($"{command.Key}（{input}）→ {head}");
            }
        }

        Assert.True(checkedCommands >= 55, $"应覆盖绝大多数指令，实际只跑了 {checkedCommands} 条");
        Assert.Empty(failures);
    }

    private static async Task<string> RouteGroupAsync(CommandRouter router, string command, long userId = UserId)
    {
        BotReply? reply = await router.RouteAsync(GroupMessage(command, userId), new OtherData());
        Assert.NotNull(reply);
        return reply!.Markdown?.Content ?? reply.Text ?? "";
    }

    private static ThirdPartyMessage GroupMessage(string detail, long userId = UserId)
        => new()
        {
            Id = "MSG-GROUP", Detail = detail, IsGroup = true, GroupOpenId = "GROUP-1", OpenId = "GROUP-1",
            AuthorOpenId = "MEMBER", FunGameUID = userId, Timestamp = "2026-11-12 00:00:00"
        };

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
            services.AddGameplayRuntime();
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IRoomService, RoomService>();
            services.AddSingleton<IClubService, ClubService>();
            services.AddSingleton<IMailService, MailService>();
            services.AddSingleton<IActivityService, ActivityService>();
            services.AddSingleton<IExploreService, ExploreService>();
            services.AddSingleton<ISimulationStatisticsStore, EfSimulationStatisticsStore>();
            services.AddSingleton<FunGameQueryService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();

            Dependencies = new CommandDependencies(
                Provider.GetRequiredService<FunGameQueryService>(), null!, null!,
                Provider.GetRequiredService<IUserSessionService>(),
                Provider.GetRequiredService<IPlayerService>(),
                null!,
                Provider.GetRequiredService<IRoomService>(),
                Provider.GetRequiredService<IClubService>(),
                Provider.GetRequiredService<IMailService>(),
                Provider.GetRequiredService<IActivityService>(),
                Provider.GetRequiredService<IExploreService>(),
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
