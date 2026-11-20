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

    /// <summary>
    /// 动词与参数之间可以没有空格（2026-11-16）：中文输入法下漏个空格太常见，
    /// <c>/领奖1</c> 应该等于 <c>/领奖 1</c>，而不是「未识别指令」。
    /// </summary>
    [Fact]
    public void Registry_AllowsVerbAttachedToArguments()
    {
        CommandRegistry registry = CreateRegistry(out _);

        CommandMatch claim = registry.Resolve("/领奖1")!;
        Assert.Equal("quest-claim", claim.Command.Key);
        Assert.Equal("1", Assert.Single(claim.Args));

        Assert.Equal("1", Assert.Single(registry.Resolve("领奖 1")!.Args));
        Assert.Equal("@1", Assert.Single(registry.Resolve("领奖@1")!.Args));   // @ 编号紧贴也认
        Assert.Equal("1", Assert.Single(registry.Resolve("领奖\u30001")!.Args)); // 中文输入法的全角空格

        CommandMatch gacha = registry.Resolve("抽卡10")!;                      // 数字参数
        Assert.Equal("gacha", gacha.Command.Key);
        Assert.Equal("10", Assert.Single(gacha.Args));

        Assert.Equal("char", registry.Resolve("查角色1")!.Command.Key);        // 旧版整段前缀写法，行为不变
        Assert.Equal("team-stats", registry.Resolve("查团队数据3")!.Command.Key);

        // 认不出来的词不能被前半截吞掉
        Assert.Null(registry.Resolve("今天天气如何"));
    }

    /// <summary>紧贴写法要挑**最长**的动词：否则「商店详情1」会被「商店」抢走，变成「商店 + 详情1」。</summary>
    [Fact]
    public void Registry_AttachedFormPrefersTheLongestVerb()
    {
        CommandRegistry registry = CreateRegistry(out _);

        CommandMatch detail = registry.Resolve("商店详情1")!;
        Assert.Equal("shop-detail", detail.Command.Key);
        Assert.Equal("1", Assert.Single(detail.Args));

        // 商店1..商店7 是固定商店命令名，精确匹配优先；带空格才是「商店 + 页码/编号」
        Assert.Equal("shop-logistics", registry.Resolve("商店1")!.Command.Key);
        Assert.Equal("shop", registry.Resolve("商店 1")!.Command.Key);
        Assert.Equal("1", Assert.Single(registry.Resolve("商店 1")!.Args));
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
    public async Task Help_MenuHasEightLegacyCategories()
    {
        (CommandRouter router, _, _, _) = CreateRouter();

        BotReply? reply = await router.RouteAsync(Message("/帮助"), new OtherData());

        Assert.NotNull(reply);
        string content = reply!.Markdown!.Content!;
        Assert.StartsWith("## ", content);
        // 照抄旧版：主菜单列 8 个分类入口，每条都是 cmd-input。
        foreach (string category in CommandCategories.Order)
        {
            Assert.Contains(category, MarkdownText.Decode(content));
            Assert.Contains(category, MarkdownText.Inserts(content).Select(x => x.Trim()));
        }
        Assert.NotNull(reply.Keyboard);
        Assert.Equal(4, reply.Keyboard!.Content!.Rows[0].Buttons.Count);
    }

    /// <summary>照抄旧版：分类指令后直接加数字跳页（存档帮助2），不必写 p2。</summary>
    [Fact]
    public async Task Help_CategoryAcceptsBarePageNumber()
    {
        (CommandRouter router, _, _, _) = CreateRouter();

        BotReply bare = (await router.RouteAsync(Message("/角色帮助 2"), new OtherData()))!;
        BotReply pform = (await router.RouteAsync(Message("/角色帮助 p2"), new OtherData()))!;

        Assert.Contains("第 2/", bare.Markdown!.Content!);
        Assert.Equal(pform.Markdown!.Content!, bare.Markdown.Content);
    }

    [Fact]
    public async Task Help_CategoriesTogetherCoverEveryCommand()
    {
        (CommandRouter router, CommandRegistry registry, _, _) = CreateRouter();

        // 帮助与注册表同源：翻完 8 个分类的所有页，应覆盖每一条指令名。
        string allHelp = "";
        foreach (string category in CommandCategories.Order)
        {
            IReadOnlyList<ICommand> inCategory = [.. registry.All.Where(c => CommandCategories.CategoryOf(c) == category)];
            int pages = Math.Max(1, (inCategory.Count + CommandCategories.HelpPageSize - 1) / CommandCategories.HelpPageSize);
            for (int page = 1; page <= pages; page++)
            {
                BotReply? reply = await router.RouteAsync(Message($"/{category} p{page}"), new OtherData());
                Assert.NotNull(reply);
                allHelp += MarkdownText.Decode(reply!.Markdown!.Content!);
            }
        }
        foreach (ICommand command in registry.All)
        {
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

    /// <summary>分类帮助里的用法就是链接：显示 usage 原文，点一下填入命令本身（空格由客户端补）。</summary>
    [Fact]
    public void Help_TurnsEveryUsageIntoALink()
    {
        (_, CommandRegistry registry, _, _) = CreateRouter();

        List<string> inserts = [];
        foreach (string category in CommandCategories.Order)
        {
            IReadOnlyList<ICommand> inCategory = [.. registry.All.Where(c => CommandCategories.CategoryOf(c) == category)];
            int pages = Math.Max(1, (inCategory.Count + CommandCategories.HelpPageSize - 1) / CommandCategories.HelpPageSize);
            for (int page = 1; page <= pages; page++)
            {
                inserts.AddRange(MarkdownText.Inserts(
                    CommandReplies.HelpCategory(registry, category, page).Markdown!.Content!));
            }
        }

        // text 里只有命令本身，不带 @ —— 客户端自动补空格，玩家直接填参数即可。
        foreach (ICommand command in registry.All)
        {
            Assert.Contains("/" + command.Name, inserts);
        }
    }

    /// <summary>战斗帮助这类正文：命令是链接，底部通用入口是按钮。</summary>
    [Fact]
    public async Task BattleHelp_LinksCommandsInTheBody()
    {
        (CommandRouter router, _, _, _) = CreateRouter();

        BotReply reply = (await router.RouteAsync(Message("/战斗帮助"), new OtherData()))!;
        string decoded = MarkdownText.Decode(reply.Markdown!.Content!);

        Assert.Contains("《筽祀牻》战斗帮助指令", decoded);          // 照抄旧版标题格式
        Assert.Contains("/查角色", decoded);                        // 正文里的指令成了链接
        Assert.Contains("<编号或名称>", decoded);                   // usage 参数说明保持纯文本
        Assert.Contains("/查角色", MarkdownText.Inserts(reply.Markdown!.Content!));

        Assert.Contains("/帮助", MarkdownText.Buttons(reply));      // 底部通用入口是按钮
        Assert.Contains("/状态", MarkdownText.Buttons(reply));
    }

    /// <summary>
    /// 结尾指令的两种归宿：没有参数的做按钮，带参数的留链接（它带着这一条的上下文）。
    /// </summary>
    [Fact]
    public void Compose_SplitsGenericActionsIntoButtons()
    {
        BotReply reply = CommandReplies.Compose("🧪 测试", null, ["- 正文"], ["/状态", "/设主战 1"]);

        Assert.Contains("/状态", MarkdownText.Buttons(reply));
        Assert.DoesNotContain("/设主战 1", MarkdownText.Buttons(reply));
        Assert.Contains("/设主战 1", MarkdownText.Inserts(reply.Markdown!.Content!));
    }

    /// <summary>排版升级：状态条走引用块（保留 ** 加粗），行动区前有分割线，按钮/链接分流不变。</summary>
    [Fact]
    public void Compose_UsesStatusBarAndDivider()
    {
        BotReply reply = CommandReplies.Compose("🧪 测试", "Lv.1 · 金币 100", ["- 正文"], ["/状态", "/设主战 1"]);
        string content = reply.Markdown!.Content!;

        string lf = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.StartsWith("## 🧪 测试", content);
        Assert.Contains("> **Lv.1", content);          // 状态条是引用块 + 加粗
        Assert.Contains("\n***\n", lf);                // 正文与行动区之间有分割线
        Assert.Contains("/状态", MarkdownText.Buttons(reply));
        Assert.Contains("/设主战 1", MarkdownText.Inserts(content));
    }

    /// <summary>排版 helper：Field 转义玩家可控的 value，Badge  escap 后仍是方括号。</summary>
    [Fact]
    public void MarkdownLayoutHelpers_EscapeValues()
    {
        Assert.Equal("- **金币**：100", QQMarkdown.Field("金币", "100"));
        Assert.Equal("- **名**：a\\*b", QQMarkdown.Field("名", "a*b"));
        Assert.Equal("[进行中]", QQMarkdown.Badge("进行中"));
        Assert.Equal("> **HP 100**", QQMarkdown.StatusBar("HP 100"));
    }

    /// <summary>正文链接化不能误伤：分隔符、分数、网址都不该变成标签。</summary>
    [Fact]
    public void LinkCommands_LeavesNonCommandsAlone()
    {
        Assert.Equal("属性 / 六个装备槽 / 技能", MarkdownText.Decode(QQCmdTag.LinkCommands("属性 / 六个装备槽 / 技能")));
        Assert.Equal("签到 1/1 已领取", MarkdownText.Decode(QQCmdTag.LinkCommands("签到 1/1 已领取")));
        Assert.Equal("见 https://bot.q.qq.com/wiki", MarkdownText.Decode(QQCmdTag.LinkCommands("见 https://bot.q.qq.com/wiki")));
        Assert.Equal("用 /练级 提升角色", MarkdownText.Decode(QQCmdTag.LinkCommands("用 /练级 提升角色")));
    }

    [Fact]
    public async Task Router_UptimeUsesFourSegmentMarkdown()
    {
        (CommandRouter router, _, _, _) = CreateRouter();

        BotReply? reply = await router.RouteAsync(Message("/查询服务器启动时间"), new OtherData());

        Assert.Contains("服务器状态", reply!.Markdown!.Content!);
        Assert.Contains("UTC", reply.Markdown!.Content!);
        Assert.Contains("/帮助", MarkdownText.Buttons(reply));
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
        return new CommandDependencies(null!, null!, codes, sessions, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, NullLogger.Instance);
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
