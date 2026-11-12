using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OshimaBot.Commands;
using OshimaBot.Messaging;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

public sealed class CommandFrameworkTests
{
    [Fact]
    public void Registry_ResolvesExactAliasesAndLegacyPrefixes()
    {
        CommandRegistry registry = CreateRegistry(out _);

        Assert.Equal("help", registry.Resolve("/帮助")!.Command.Key);
        Assert.Equal("help", registry.Resolve("help")!.Command.Key);
        Assert.Equal("char", registry.Resolve("查角色 1")!.Command.Key);
        Assert.Equal("1", Assert.Single(registry.Resolve("查角色 1")!.Args));
        Assert.Equal("1", Assert.Single(registry.Resolve("查角色1")!.Args)); // 旧版无空格前缀写法
        Assert.Equal("team-stats", registry.Resolve("查团队数据 3")!.Command.Key); // 最长前缀优先
        Assert.Equal("stats", registry.Resolve("查数据 3")!.Command.Key);
        Assert.Equal("log", registry.Resolve("上次的日志")!.Command.Key);
        Assert.Equal("log-full", registry.Resolve("/上次的完整日志")!.Command.Key);
        Assert.Null(registry.Resolve("不存在的指令"));
        Assert.Null(registry.Resolve("   "));
    }

    [Fact]
    public void Registry_StripsPageTokensWithoutTouchingNumericArguments()
    {
        CommandRegistry registry = CreateRegistry(out _);

        CommandMatch log = registry.Resolve("/上次的日志 p3")!;
        Assert.Equal(3, log.Page);
        Assert.Empty(log.Args);
        Assert.Equal(2, registry.Resolve("/帮助 p2")!.Page);

        CommandMatch stats = registry.Resolve("查数据 12")!;
        Assert.Equal(1, stats.Page);
        Assert.Equal("12", Assert.Single(stats.Args));
    }

    [Fact]
    public void Catalog_AllCommandsHaveCompleteMetadata()
    {
        CommandRegistry registry = CreateRegistry(out _);

        Assert.Equal(CommandCatalog.Keys.Count, registry.All.Count);
        Assert.Equal(CommandCatalog.Keys.Count, CommandCatalog.Keys.Distinct().Count());
        foreach (ICommand command in registry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Name), command.Key);
            Assert.False(string.IsNullOrWhiteSpace(command.Usage), command.Key);
            Assert.False(string.IsNullOrWhiteSpace(command.Description), command.Key);
            Assert.False(string.IsNullOrWhiteSpace(command.Group), command.Key);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandCatalog.Create("unknown", CreateDeps(out _, out _)));
    }

    [Fact]
    public async Task Help_IsGeneratedFromRegistryAndUsesFourSections()
    {
        (CommandRouter router, CommandRegistry registry, _, _) = CreateRouter();

        BotReply? reply = await router.RouteAsync(Message("/帮助"), new OtherData());

        Assert.NotNull(reply);
        string content = reply!.Markdown!.Content!;
        Assert.StartsWith("## ", content);
        Assert.Contains("第 1/", content);
        Assert.Contains("qqbot-cmd-input", content);
        Assert.NotNull(reply.Keyboard);
        Assert.Equal(5, reply.Keyboard!.Content!.Rows[0].Buttons.Count);
        Assert.Equal(2, reply.Keyboard.Content.Rows.Count); // 常用指令行 + 分页行

        // 帮助与注册表同源：翻完所有页应覆盖每一条 Usage。
        int pages = (registry.All.Count + CommandReplies.HelpPageSize - 1) / CommandReplies.HelpPageSize;
        string allHelp = string.Concat(Enumerable.Range(1, pages)
            .Select(page => CommandReplies.Help(registry, page).Markdown!.Content));
        foreach (ICommand command in registry.All)
        {
            // Usage 经 Markdown 转义（< > 等），名称本身是安全锚点。
            Assert.Contains(command.Name, allHelp, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Router_UnknownCommandReturnsNull()
    {
        (CommandRouter router, _, _, _) = CreateRouter();
        Assert.Null(await router.RouteAsync(Message("随便说点什么"), new OtherData()));
    }

    [Fact]
    public async Task Router_ErrorRepliesHaveThreeSegments()
    {
        (CommandRouter router, _, _, _) = CreateRouter();

        BotReply? reply = await router.RouteAsync(Message("/绑定", isGroup: true), new OtherData());

        Assert.NotNull(reply);
        string content = reply!.Markdown!.Content!;
        Assert.Contains("没有发生", content);
        Assert.Contains("下一步", content);
        Assert.Contains("qqbot-cmd-input", content);
    }

    [Fact]
    public async Task Router_UptimeUsesFourSegmentMarkdown()
    {
        (CommandRouter router, _, _, _) = CreateRouter();

        BotReply? reply = await router.RouteAsync(Message("/查询服务器启动时间"), new OtherData());

        Assert.Contains("服务器状态", reply!.Markdown!.Content!);
        Assert.Contains("UTC", reply.Markdown!.Content!);
        Assert.Contains("qqbot-cmd-input", reply.Markdown!.Content!);
    }

    [Fact]
    public async Task Router_BindGuardsGroupChatAndAnonymousUsers()
    {
        (CommandRouter router, _, _, FakeBindCodes codes) = CreateRouter();

        BotReply? group = await router.RouteAsync(Message("/绑定", isGroup: true), new OtherData());
        Assert.Contains("群聊不能签发绑定码", group!.Markdown!.Content!);
        Assert.Equal(0, codes.Calls);

        BotReply? anonymous = await router.RouteAsync(Message("/获取接入码", userId: 0), new OtherData());
        Assert.Contains("身份尚未就绪", anonymous!.Markdown!.Content!);
        Assert.Equal(0, codes.Calls);

        BotReply? ok = await router.RouteAsync(Message("/绑定"), new OtherData());
        Assert.Contains("一次性绑定码", ok!.Markdown!.Content!);
        Assert.Matches(@"\d{6}", ok.Markdown!.Content!);
        Assert.Equal(1, codes.Calls);
    }

    [Fact]
    public async Task Router_CancelClearsPendingSession()
    {
        (CommandRouter router, _, FakeSessions sessions, _) = CreateRouter();

        BotReply? reply = await router.RouteAsync(Message("/重置状态"), new OtherData());

        Assert.Contains("状态已重置", reply!.Markdown!.Content!);
        Assert.Equal(1, sessions.ClearCalls);
    }

    [Fact]
    public async Task Router_EnforcesRequiredRole()
    {
        DelegateCommand adminCommand = new("admin", "管理员指令", [], "/管理员指令", "测试",
            CommandGroups.System, _ => Task.FromResult(BotReply.FromText("ok")), requiredRole: BotRole.Admin);
        (CommandRouter router, _, _, _) = CreateRouter(new CommandRegistry([adminCommand]));

        BotReply? denied = await router.RouteAsync(Message("/管理员指令"), new OtherData());
        Assert.Contains("权限不足", denied!.Markdown!.Content!);

        ThirdPartyMessage message = Message("/管理员指令");
        message.Principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, BotRole.Admin.ToString())], "test"));
        BotReply? allowed = await router.RouteAsync(message, new OtherData());
        Assert.Equal("ok", allowed!.Text);
    }

    [Fact]
    public async Task Router_EnforcesCooldown()
    {
        DelegateCommand slowCommand = new("slow", "慢指令", [], "/慢指令", "测试",
            CommandGroups.System, _ => Task.FromResult(BotReply.FromText("ok")), cooldownMilliseconds: 60_000);
        (CommandRouter router, _, _, _) = CreateRouter(new CommandRegistry([slowCommand]));

        Assert.Equal("ok", (await router.RouteAsync(Message("/慢指令"), new OtherData()))!.Text);
        Assert.Contains("操作太快", (await router.RouteAsync(Message("/慢指令"), new OtherData()))!.Markdown!.Content!);
    }

    [Fact]
    public void CommandReplies_PaginatesLongContent()
    {
        string body = new('x', 7000);
        Assert.Equal(3, QQMarkdown.Split(body, QQMarkdown.DefaultMaxLength).Count);

        BotReply first = CommandReplies.Paginated("📜 长日志", body, 1, "/上次的日志");
        Assert.Contains("第 1/3 页", first.Markdown!.Content!);
        Assert.Contains("p2", first.Markdown!.Content!); // CmdInput 会 urlencode，页码仍可见
        Assert.NotNull(first.Keyboard);

        BotReply last = CommandReplies.Paginated("📜 长日志", body, 99, "/上次的日志");
        Assert.Contains("第 3/3 页", last.Markdown!.Content!);
    }

    [Fact]
    public void CommandContext_ParsesOptionsReferencesAndFlags()
    {
        CommandRegistry registry = CreateRegistry(out CommandDependencies deps);
        CommandContext context = new()
        {
            Message = Message("/测试 @3 x2 --seed=42 --yes"),
            Raw = "/测试 @3 x2 --seed=42 --yes",
            Verb = "测试",
            Args = ["@3", "x2", "--seed=42", "--yes"],
            Page = 1,
            Registry = registry,
            Dependencies = deps,
            Data = new OtherData(),
            CancellationToken = CancellationToken.None
        };

        Assert.Equal("3", context.Reference());
        Assert.Equal("42", context.Option("seed"));
        Assert.True(context.HasFlag("--yes"));
        Assert.False(context.HasFlag("--no"));
        Assert.Equal("@3 x2 --seed=42 --yes", context.ArgumentText);
    }

    private static CommandRegistry CreateRegistry(out CommandDependencies deps)
    {
        deps = CreateDeps(out _, out _);
        return CommandCatalog.Build(deps);
    }

    private static CommandDependencies CreateDeps(out FakeSessions sessions, out FakeBindCodes codes)
    {
        sessions = new FakeSessions();
        codes = new FakeBindCodes();
        return new CommandDependencies(null!, null!, codes, sessions, null!, null!, null!, null!, null!, null!, null!, null!, null!, NullLogger.Instance);
    }

    private static (CommandRouter Router, CommandRegistry Registry, FakeSessions Sessions, FakeBindCodes Codes)
        CreateRouter(CommandRegistry? customRegistry = null)
    {
        CommandDependencies deps = CreateDeps(out FakeSessions sessions, out FakeBindCodes codes);
        CommandRegistry registry = customRegistry ?? CommandCatalog.Build(deps);
        CommandRouter router = new(registry, deps, new EmptyScopeFactory(),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<CommandRouter>.Instance);
        return (router, registry, sessions, codes);
    }

    private static ThirdPartyMessage Message(string detail, bool isGroup = false, long userId = 42)
        => new()
        {
            Id = "MSG-TEST", Detail = detail, IsGroup = isGroup, OpenId = isGroup ? "GROUP" : "USER",
            AuthorOpenId = "USER", FunGameUID = userId, Timestamp = "2026-11-09 23:00:00"
        };

    private sealed class FakeBindCodes : IBindCodeService
    {
        public int Calls { get; private set; }
        public int TtlSeconds => 300;

        public Task<string> IssueAsync(long userId, string purpose = "qq_bind", CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult("123456");
        }

        public Task<BindRedeemResult> RedeemAsync(string code, ExternalIdentity identity, CancellationToken cancellationToken = default)
            => Task.FromResult(BindRedeemResult.Ok(42));
    }

    private sealed class FakeSessions : IUserSessionService
    {
        public int ClearCalls { get; private set; }
        public Task<UserSessionState?> GetAsync(long userId, CancellationToken cancellationToken = default) => Task.FromResult<UserSessionState?>(null);
        public Task SetPendingAsync(long userId, string action, string payloadJson, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearPendingAsync(long userId, CancellationToken cancellationToken = default)
        {
            ClearCalls++;
            return Task.CompletedTask;
        }
        public Task SetPageCursorAsync(long userId, string cursor, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new EmptyScope();
    }

    private sealed class EmptyScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();
        public void Dispose() { }
    }
}
