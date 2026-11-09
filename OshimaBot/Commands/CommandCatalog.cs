using FunGame.Core.Library.Constant;
using OshimaBot.Game.Services;
using OshimaBot.Messaging;
using OshimaBot.Models;
using OshimaBot.Services;

namespace OshimaBot.Commands;

/// <summary>
/// 显式命令工厂（用户要求：switch-case 清单式注册，禁止反射扫描程序集）。
/// 新增指令必须同时改 Keys 与 Create 的 switch；帮助、别名表、权限矩阵都由这一份数据生成。
/// </summary>
public static class CommandCatalog
{
    public static readonly IReadOnlyList<string> Keys =
    [
        "help", "battle-help", "cancel", "bind", "uptime", "legacy-help",
        "magic-card", "magic-card-pack",
        "char", "skill", "item", "stats", "team-stats",
        "rank-win", "rank-team-win", "rank-rating", "rank-team-rating",
        "log", "log-full",
        "sim-debug", "sim-team-map", "sim-team", "sim-map", "sim-mix", "sim"
    ];

    public static CommandRegistry Build(CommandDependencies deps)
        => new(Keys.Select(key => Create(key, deps)));

    public static ICommand Create(string key, CommandDependencies deps) => key switch
    {
        // ---- 系统 ----
        "help" => new DelegateCommand(
            key: "help", name: "帮助", aliases: ["help", "?"], usage: "/帮助 [p2]",
            description: "列出全部指令（分页）", group: CommandGroups.System,
            handler: context => Task.FromResult(CommandReplies.Help(context.Registry, context.Page))),

        "battle-help" => new DelegateCommand(
            key: "battle-help", name: "战斗帮助", aliases: ["角色帮助", "物品帮助", "battlehelp"],
            usage: "/战斗帮助", description: "战斗与资料查询说明", group: CommandGroups.System,
            handler: _ => Task.FromResult(CommandReplies.Compose("⚔️ 战斗与资料指令", "旧版指令仍可直接使用",
            [
                "- /查角色 <编号或名称>　满级角色资料",
                "- /查技能 <编号或名称>　1/2 级与满级技能",
                "- /查物品 <编号或名称>　物品属性",
                "- /混战模拟 [复活次数]　个人混战",
                "- /FunGame模拟　无复活混战",
                "- /FunGame团队模拟　团队模拟",
                "- /个人地图模拟 · /团队地图模拟 · /团队调试模拟",
                "- /查数据 · /查团队数据　累计统计",
                "- /查个人胜率 · /查团队胜率 · /查个人评分 · /查团队评分",
                "- /上次的日志 · /上次的完整日志"
            ], ["/帮助", "/查角色 1", "/FunGame模拟"]))),

        "cancel" => new DelegateCommand(
            key: "cancel", name: "取消", aliases: ["重置状态", "cancel", "reset"], usage: "/取消",
            description: "清空待确认操作与会话状态", group: CommandGroups.System,
            handler: async context =>
            {
                await deps.Sessions.ClearPendingAsync(context.UserId, context.CancellationToken);
                return CommandReplies.Compose("🧹 状态已重置", "待确认操作与会话游标已清空",
                    ["- 上一条待确认操作已作废", "- 存档与背包数据不受影响"], ["/帮助"]);
            }),

        "bind" => new DelegateCommand(
            key: "bind", name: "绑定", aliases: ["获取接入码", "bind"], usage: "/绑定",
            description: "签发 6 位一次性绑定码（仅单聊）", group: CommandGroups.System, audit: true,
            handler: async context =>
            {
                if (context.IsGroup)
                {
                    return CommandReplies.Error("群聊不能签发绑定码", "没有生成任何码", "请在与机器人的单聊里发送 /绑定");
                }
                if (context.UserId <= 0)
                {
                    return CommandReplies.Error("身份尚未就绪", "没有生成任何码", "请稍后重发一条消息触发注册");
                }
                string code = await deps.BindCodes.IssueAsync(context.UserId, "qq_bind", context.CancellationToken);
                return CommandReplies.Compose("🔑 一次性绑定码", $"{code} · {deps.BindCodes.TtlSeconds / 60} 分钟内有效",
                [
                    $"- 绑定码：{code}",
                    "- 只能使用一次，用后即失效",
                    "- 旧版直接回显 OpenID 的「接入码」已停用"
                ], ["/帮助"]);
            }),

        "uptime" => new DelegateCommand(
            key: "uptime", name: "查询服务器启动时间", aliases: ["启动时间", "uptime", "status"],
            usage: "/查询服务器启动时间", description: "查看服务启动时间（UTC）", group: CommandGroups.System,
            handler: _ => Task.FromResult(CommandReplies.Compose("🖥️ 服务器状态",
                $"启动于 {FunGameRuntime.StartedAt:yyyy-MM-dd HH:mm:ss} UTC",
                [$"- 核心：{FunGameInfo.FunGame_Version}", "- 接入：WebSocket 单模式"], ["/帮助"]))),

        "legacy-help" => new DelegateCommand(
            key: "legacy-help", name: "玩法帮助", aliases: ["存档帮助", "社团帮助", "活动帮助", "商店帮助", "legacy"],
            usage: "/玩法帮助", description: "查看尚未迁移的模块", group: CommandGroups.Legacy,
            handler: _ => Task.FromResult(CommandReplies.Error(
                "该模块尚未迁移到 OshimaBot", "存档、社团、活动和商店暂不可用",
                "当前开放角色、物品、战斗查询与模拟指令", "/帮助"))),

        // ---- 查询 ----
        "magic-card" => new DelegateCommand(
            key: "magic-card", name: "预览魔法卡", aliases: ["魔法卡", "card"], usage: "/预览魔法卡",
            description: "随机生成一张魔法卡", group: CommandGroups.Query,
            handler: _ => Task.FromResult(CommandReplies.Detail("🃏 魔法卡", deps.Queries.GenerateMagicCard(),
                actions: ["/预览魔法卡", "/预览魔法卡包"]))),

        "magic-card-pack" => new DelegateCommand(
            key: "magic-card-pack", name: "预览魔法卡包", aliases: ["魔法卡包", "pack"], usage: "/预览魔法卡包",
            description: "随机生成一包魔法卡", group: CommandGroups.Query,
            handler: _ => Task.FromResult(CommandReplies.Detail("🎴 魔法卡包", deps.Queries.GenerateMagicCardPack(),
                actions: ["/预览魔法卡", "/预览魔法卡包"]))),

        "char" => new DelegateCommand(
            key: "char", name: "查角色", aliases: ["角色", "char", "character"], usage: "/查角色 <编号或名称>",
            description: "查看满级角色资料", group: CommandGroups.Query, prefixMatch: true,
            handler: context => Task.FromResult(Found("📖 角色资料",
                deps.Queries.GetCharacterInfo(context.ArgumentText), "未找到角色", context, "/查角色 1"))),

        "skill" => new DelegateCommand(
            key: "skill", name: "查技能", aliases: ["技能", "skill"], usage: "/查技能 <编号或名称>",
            description: "查看 1/2 级与满级技能", group: CommandGroups.Query, prefixMatch: true,
            handler: context =>
            {
                string argument = context.ArgumentText;
                string result = long.TryParse(argument, out long id)
                    ? deps.Queries.GetSkillInfo(id, null)
                    : deps.Queries.GetSkillInfo(null, argument);
                return Task.FromResult(Found("📖 技能资料", result, "未找到技能", context, "/查技能 1"));
            }),

        "item" => new DelegateCommand(
            key: "item", name: "查物品", aliases: ["物品", "item"], usage: "/查物品 <编号或名称>",
            description: "查看物品资料", group: CommandGroups.Query, prefixMatch: true,
            handler: context =>
            {
                string argument = context.ArgumentText;
                string result = long.TryParse(argument, out long id)
                    ? deps.Queries.GetItemInfo(id, null)
                    : deps.Queries.GetItemInfo(null, argument);
                return Task.FromResult(Found("📖 物品资料", result, "未找到物品", context, "/查物品 1"));
            }),

        "stats" => new DelegateCommand(
            key: "stats", name: "查数据", aliases: ["数据", "stats"], usage: "/查数据 <编号>",
            description: "角色累计统计", group: CommandGroups.Stats, prefixMatch: true,
            handler: context => StatsAsync(deps, false, "📊 角色统计", context)),

        "team-stats" => new DelegateCommand(
            key: "team-stats", name: "查团队数据", aliases: ["团队数据", "teamstats"], usage: "/查团队数据 <编号>",
            description: "角色团队累计统计", group: CommandGroups.Stats, prefixMatch: true,
            handler: context => StatsAsync(deps, true, "📊 角色团队统计", context)),

        "rank-win" => new DelegateCommand(
            key: "rank-win", name: "查个人胜率", aliases: ["个人胜率", "rankwin"], usage: "/查个人胜率",
            description: "个人胜率排行", group: CommandGroups.Stats, prefixMatch: true,
            handler: context => RankingAsync(deps, false, false, "🏆 个人胜率排行", context)),

        "rank-team-win" => new DelegateCommand(
            key: "rank-team-win", name: "查团队胜率", aliases: ["团队胜率", "rankteamwin"], usage: "/查团队胜率",
            description: "团队胜率排行", group: CommandGroups.Stats, prefixMatch: true,
            handler: context => RankingAsync(deps, true, false, "🏆 团队胜率排行", context)),

        "rank-rating" => new DelegateCommand(
            key: "rank-rating", name: "查个人评分", aliases: ["个人评分", "rankrating"], usage: "/查个人评分",
            description: "个人评分排行", group: CommandGroups.Stats, prefixMatch: true,
            handler: context => RankingAsync(deps, false, true, "🏆 个人评分排行", context)),

        "rank-team-rating" => new DelegateCommand(
            key: "rank-team-rating", name: "查团队评分", aliases: ["团队评分", "rankteamrating"], usage: "/查团队评分",
            description: "团队评分排行", group: CommandGroups.Stats, prefixMatch: true,
            handler: context => RankingAsync(deps, true, true, "🏆 团队评分排行", context)),

        // ---- 日志与模拟 ----
        "log" => new DelegateCommand(
            key: "log", name: "上次的日志", aliases: ["日志", "log"], usage: "/上次的日志 [p2]",
            description: "最近一次模拟日志", group: CommandGroups.Simulation,
            handler: async context => CommandReplies.CodePage("📜 上次的模拟日志",
                await deps.Simulations.GetLastAsync(false, context.CancellationToken), context.Page, "/上次的日志")),

        "log-full" => new DelegateCommand(
            key: "log-full", name: "上次的完整日志", aliases: ["完整日志", "logfull"], usage: "/上次的完整日志 [p2]",
            description: "最近一次完整模拟日志", group: CommandGroups.Simulation,
            handler: async context => CommandReplies.CodePage("📜 上次的完整日志",
                await deps.Simulations.GetLastAsync(true, context.CancellationToken), context.Page, "/上次的完整日志")),

        "sim-debug" => new DelegateCommand(
            key: "sim-debug", name: "团队调试模拟", aliases: ["团队调试", "simdebug"], usage: "/团队调试模拟 [p2]",
            description: "团队地图调试模拟", group: CommandGroups.Simulation, prefixMatch: true, audit: true,
            handler: context => RunSimulationAsync(deps, new SimulationRequest
            {
                IsTeam = true, HasMap = true, IsDebug = true, ShowAll = true, MaxRespawnTimesMix = 0
            }, context)),

        "sim-team-map" => new DelegateCommand(
            key: "sim-team-map", name: "团队地图模拟", aliases: ["团队地图", "simteammap"], usage: "/团队地图模拟 [p2]",
            description: "带地图的团队模拟", group: CommandGroups.Simulation, prefixMatch: true, audit: true,
            handler: context => RunSimulationAsync(deps, new SimulationRequest { IsTeam = true, HasMap = true }, context)),

        "sim-team" => new DelegateCommand(
            key: "sim-team", name: "FunGame团队模拟", aliases: ["团队模拟", "simteam"], usage: "/FunGame团队模拟 [p2]",
            description: "团队模拟", group: CommandGroups.Simulation, prefixMatch: true, audit: true,
            handler: context => RunSimulationAsync(deps, new SimulationRequest { IsTeam = true }, context)),

        "sim-map" => new DelegateCommand(
            key: "sim-map", name: "个人地图模拟", aliases: ["个人地图", "simmap"], usage: "/个人地图模拟 [p2]",
            description: "带地图的个人模拟", group: CommandGroups.Simulation, prefixMatch: true, audit: true,
            handler: context => RunSimulationAsync(deps, new SimulationRequest { HasMap = true, MaxRespawnTimesMix = 0 }, context)),

        "sim-mix" => new DelegateCommand(
            key: "sim-mix", name: "混战模拟", aliases: ["混战", "simmix"], usage: "/混战模拟 [复活次数] [p2]",
            description: "个人混战模拟", group: CommandGroups.Simulation, prefixMatch: true, audit: true,
            handler: context =>
            {
                SimulationRequest request = new()
                {
                    MaxRespawnTimesMix = context.TryInt(0, out int times) ? times : 1
                };
                return RunSimulationAsync(deps, request, context);
            }),

        "sim" => new DelegateCommand(
            key: "sim", name: "FunGame模拟", aliases: ["模拟", "sim"], usage: "/FunGame模拟 [p2]",
            description: "无复活混战模拟", group: CommandGroups.Simulation, prefixMatch: true, audit: true,
            handler: context => RunSimulationAsync(deps, new SimulationRequest { MaxRespawnTimesMix = 0 }, context)),

        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "未注册的指令键。")
    };

    private static async Task<BotReply> RunSimulationAsync(
        CommandDependencies deps, SimulationRequest request, CommandContext context)
    {
        SimulationResult? result = await deps.Simulations.RunAsync(request, context.CancellationToken);
        if (result is null)
        {
            return CommandReplies.Error("已有模拟正在运行", "没有启动新的模拟", "请等待上一次模拟结束后重试", "/上次的日志");
        }
        deps.Logger.LogInformation("模拟完成：Seed={Seed}, Duration={Duration}", result.Seed, result.Duration);
        return CommandReplies.CodePage($"🎲 模拟结果 · 种子 {result.Seed}", result.Messages,
            context.Page, $"/{context.Verb}");
    }

    private static async Task<BotReply> StatsAsync(
        CommandDependencies deps, bool team, string title, CommandContext context)
    {
        if (!context.TryInt(0, out int id))
        {
            return CommandReplies.Error("缺少或无效的角色编号", "没有执行统计查询",
                "用法：/查数据 <编号> 或 /查团队数据 <编号>", team ? "/查团队数据 1" : "/查数据 1");
        }
        string result = await deps.Queries.GetStatsAsync(id, team, context.CancellationToken);
        return Found(title, result, team ? "未找到角色团队统计" : "未找到角色统计", context,
            team ? "/查团队数据 1" : "/查数据 1");
    }

    private static async Task<BotReply> RankingAsync(
        CommandDependencies deps, bool team, bool byRating, string title, CommandContext context)
    {
        List<string> ranking = await deps.Queries.GetRankingAsync(team, byRating, context.CancellationToken);
        if (ranking.Count == 0)
        {
            return CommandReplies.Error("暂无统计数据", "没有生成排行", "先运行一次模拟再查询", "/FunGame模拟");
        }
        string text = string.Join("\r\n\r\n", ranking);
        return text.Length > QQMarkdown.DefaultMaxLength - 300
            ? CommandReplies.Paginated(title, text, context.Page, $"/{context.Verb}")
            : CommandReplies.Detail(title, text, actions: ["/帮助"]);
    }

    private static BotReply Found(
        string title, string? result, string fallback, CommandContext context, params string[] actions)
    {
        if (string.IsNullOrWhiteSpace(result))
        {
            return CommandReplies.Error(fallback, "查询没有返回内容",
                "检查参数后重试，或输入 /帮助 查看用法", actions.FirstOrDefault() ?? "/帮助");
        }
        // 翻页命令保留原参数，例如 /查技能 123 p2，否则第二页会丢查询目标。
        string command = $"/{context.Verb} {context.ArgumentText}".TrimEnd();
        return result.Length > QQMarkdown.DefaultMaxLength - 300
            ? CommandReplies.Paginated(title, result, context.Page, command)
            : CommandReplies.Detail(title, result, actions: actions.Length == 0 ? ["/帮助"] : actions);
    }
}
