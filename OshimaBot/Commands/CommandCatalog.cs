using FunGame.Core.Library.Constant;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
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
        "sim-debug", "sim-team-map", "sim-team", "sim-map", "sim-mix", "sim",
        // ---- P1：旅人存档 ----
        "register", "status", "look", "notice", "fortune", "signin",
        "quests", "quest-claim", "gacha", "gacha-ten", "bag",
        "use", "salvage", "lock", "unlock", "equip", "unequip",
        "characters", "set-main", "levelup", "break", "train", "train-claim", "party"
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
                "P2 模块尚未迁移", "商店、市场、交易、锻造、秘境与决斗暂不可用",
                "P1 存档、角色、背包、签到、抽卡与练级已开放，输入 /状态 查看", "/状态"))),

        // ---- P1：旅人存档（全部由显式 switch 注册）----
        "register" => new DelegateCommand(
            key: "register", name: "注册", aliases: ["register", "建档"], usage: "/注册",
            description: "建立旅人存档并领取初始资源", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.RegisterAsync(context.UserId, context.CancellationToken))),

        "status" => new DelegateCommand(
            key: "status", name: "状态", aliases: ["存档", "me"], usage: "/状态",
            description: "旅人档案：等级、货币、角色与背包概览", group: CommandGroups.Player,
            handler: context => StatusAsync(deps, context)),

        "look" => new DelegateCommand(
            key: "look", name: "观察", aliases: ["场景", "look"], usage: "/观察",
            description: "MUD 场景：当前所在与可交互地点", group: CommandGroups.Player,
            handler: context => LookAsync(deps, context)),

        "notice" => new DelegateCommand(
            key: "notice", name: "公告", aliases: ["notice"], usage: "/公告",
            description: "查看运营公告", group: CommandGroups.Player,
            handler: async context => CommandReplies.Compose("📜 铎京公告", null,
                [QQMarkdown.Escape(await deps.Players.GetNoticeAsync(context.CancellationToken))],
                ["/状态", "/帮助"])),

        "fortune" => new DelegateCommand(
            key: "fortune", name: "运势", aliases: ["今日运势", "fortune"], usage: "/运势",
            description: "今日占卜（同一天结果固定）", group: CommandGroups.Player,
            handler: async context => CommandReplies.Compose("🔮 今日运势",
                GameplayRules.Today(),
                [QQMarkdown.Escape(await deps.Players.GetFortuneAsync(context.UserId, context.CancellationToken))],
                ["/签到", "/每日任务"])),

        "signin" => new DelegateCommand(
            key: "signin", name: "签到", aliases: ["signin", "daily"], usage: "/签到",
            description: "每日签到，连续签到有额外钻石", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.SignInAsync(context.UserId, context.CancellationToken))),

        "quests" => new DelegateCommand(
            key: "quests", name: "每日任务", aliases: ["任务", "quests"], usage: "/每日任务 [p2]",
            description: "今日任务进度与领奖编号", group: CommandGroups.Player,
            handler: context => QuestsAsync(deps, context)),

        "quest-claim" => new DelegateCommand(
            key: "quest-claim", name: "领奖", aliases: ["领取任务", "questclaim"], usage: "/领奖 @1",
            description: "领取指定任务的每日奖励", group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.ClaimQuestAsync(context.UserId, RefIndex(context), context.CancellationToken))),

        "gacha" => new DelegateCommand(
            key: "gacha", name: "抽卡", aliases: ["gacha"], usage: "/抽卡 [数量]",
            description: "消耗钻石抽取物品（最多 10 连）", group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.GachaAsync(context.UserId, Quantity(context, 0, 1), context.CancellationToken))),

        "gacha-ten" => new DelegateCommand(
            key: "gacha-ten", name: "十连抽卡", aliases: ["十连", "gacha10"], usage: "/十连抽卡",
            description: "十连抽卡（折扣价 + 史诗保底）", group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.GachaAsync(context.UserId, 10, context.CancellationToken))),

        "bag" => new DelegateCommand(
            key: "bag", name: "背包", aliases: ["查看库存", "bag", "inventory"], usage: "/背包 [p2]",
            description: "查看背包，@编号 用于其它指令", group: CommandGroups.Player,
            handler: context => BagAsync(deps, context)),

        "use" => new DelegateCommand(
            key: "use", name: "使用", aliases: ["use"], usage: "/使用 @1 [x2]",
            description: "使用物品换取旅人经验", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(await deps.Players.UseAsync(
                context.UserId, RefIndex(context), Quantity(context, 1, 1), context.CancellationToken))),

        "salvage" => new DelegateCommand(
            key: "salvage", name: "分解", aliases: ["salvage"], usage: "/分解 @1 [x2]",
            description: "分解未锁定物品换取金币", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(await deps.Players.SalvageAsync(
                context.UserId, RefIndex(context), Quantity(context, 1, 1), context.CancellationToken))),

        "lock" => new DelegateCommand(
            key: "lock", name: "锁定", aliases: ["lock"], usage: "/锁定 @1",
            description: "锁定物品，防止误分解", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(await deps.Players.SetLockAsync(
                context.UserId, RefIndex(context), true, context.CancellationToken))),

        "unlock" => new DelegateCommand(
            key: "unlock", name: "解锁", aliases: ["unlock"], usage: "/解锁 @1",
            description: "解除物品锁定", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(await deps.Players.SetLockAsync(
                context.UserId, RefIndex(context), false, context.CancellationToken))),

        "equip" => new DelegateCommand(
            key: "equip", name: "装备", aliases: ["equip"], usage: "/装备 @1",
            description: "把物品装到主战角色的空槽", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.EquipAsync(context.UserId, RefIndex(context), context.CancellationToken))),

        "unequip" => new DelegateCommand(
            key: "unequip", name: "卸下", aliases: ["unequip"], usage: "/卸下 weapon|armor|trinket",
            description: "卸下主战角色指定槽位的装备", group: CommandGroups.Player,
            handler: async context =>
            {
                string slot = context.FirstArgument ?? "";
                return string.IsNullOrWhiteSpace(slot)
                    ? CommandReplies.Error("缺少槽位", "没有卸下任何装备",
                        "用法：/卸下 weapon · /卸下 armor · /卸下 trinket", "/角色")
                    : CommandReplies.FromResult(await deps.Players.UnequipAsync(
                        context.UserId, slot.Trim().ToLowerInvariant(), context.CancellationToken));
            }),

        "characters" => new DelegateCommand(
            key: "characters", name: "我的角色", aliases: ["角色列表", "chars"], usage: "/我的角色 [p2]",
            description: "角色列表与主战标记", group: CommandGroups.Player,
            handler: context => CharactersAsync(deps, context)),

        "set-main" => new DelegateCommand(
            key: "set-main", name: "设主战", aliases: ["主战", "setmain"], usage: "/设主战 @1",
            description: "指定主战角色", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(await deps.Players.SetMainAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "levelup" => new DelegateCommand(
            key: "levelup", name: "升级", aliases: ["levelup"], usage: "/升级 @1",
            description: "消耗旅人经验提升角色等级", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(await deps.Players.LevelUpAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "break" => new DelegateCommand(
            key: "break", name: "突破", aliases: ["break", "突破上限"], usage: "/突破 @1",
            description: "满级后消耗金币提升等级上限", group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Players.BreakAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "train" => new DelegateCommand(
            key: "train", name: "练级", aliases: ["训练", "train"], usage: "/练级 @1 [分钟]",
            description: "花费金币开始离线练级", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(await deps.Players.TrainStartAsync(
                context.UserId, RefIndex(context),
                Quantity(context, 1, GameplayRules.TrainDefaultMinutes), context.CancellationToken))),

        "train-claim" => new DelegateCommand(
            key: "train-claim", name: "收取练级", aliases: ["收训练", "trainclaim"], usage: "/收取练级",
            description: "结算已完成的练级经验", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.TrainClaimAsync(context.UserId, context.CancellationToken))),

        "party" => new DelegateCommand(
            key: "party", name: "小队", aliases: ["编队", "party"], usage: "/小队 [加入|移除 @编号]",
            description: "查看或调整 3 人主力小队", group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(await deps.Players.SquadAsync(
                context.UserId, context.FirstArgument ?? "", RefIndex(context, 1), context.CancellationToken))),

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
        if (context.UserId > 0 && deps.Players is not null)
        {
            await deps.Players.RecordSimulationAsync(context.UserId, context.CancellationToken);
        }
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

    // ---- P1 指令辅助 ----

    /// <summary>@编号 / 纯数字都接受，统一转成 1-based 序号；无效返回 0 由仓储给出错误提示。</summary>
    private static int RefIndex(CommandContext context, int position = 0)
    {
        if (position >= context.Args.Count) return 0;
        string token = context.Args[position];
        if (token.StartsWith('@')) token = token[1..];
        return int.TryParse(token, out int value) && value > 0 ? value : 0;
    }

    /// <summary>x2 / 2 都表示数量，缺省用 fallback。</summary>
    private static int Quantity(CommandContext context, int position, int fallback)
    {
        if (position >= context.Args.Count) return fallback;
        string token = context.Args[position];
        if (token.Length > 1 && (token[0] == 'x' || token[0] == 'X')) token = token[1..];
        return int.TryParse(token, out int value) && value > 0 ? value : fallback;
    }

    private static BotReply NoIdentity()
        => CommandReplies.Error("身份尚未就绪", "没有读写任何存档",
            "请通过 QQ 消息触发注册，或稍后重试", "/帮助");

    private static async Task<BotReply> StatusAsync(CommandDependencies deps, CommandContext context)
    {
        PlayerSnapshot? snapshot = await deps.Players.GetSnapshotAsync(context.UserId, context.CancellationToken);
        if (snapshot is null) return NoIdentity();
        return CommandReplies.Compose("📍 铎京 · 旅人档案",
            $"Lv.{snapshot.Level}　金币 {snapshot.Credits}　钻石 {snapshot.Diamonds}　经验 {snapshot.Exp}",
            [
                "潮湿的石板路延伸到雾里，铁匠铺的锤声隔着两条街传来。",
                "",
                $"- 主战角色：{snapshot.MainCharacter ?? "（无）"}　角色 {snapshot.CharacterCount} 名",
                $"- 背包 {snapshot.ItemCount} 格　小队 {snapshot.SquadCount}/3 人　练级中 {snapshot.PendingTrainings} 场",
                $"- 连续签到 {snapshot.SignInStreak} 天　最近签到：{(string.IsNullOrEmpty(snapshot.SignedOn) ? "尚未" : snapshot.SignedOn)}"
            ],
            ["/签到", "/每日任务", "/背包", "/我的角色", "/抽卡"]);
    }

    private static async Task<BotReply> LookAsync(CommandDependencies deps, CommandContext context)
    {
        PlayerSnapshot? snapshot = await deps.Players.GetSnapshotAsync(context.UserId, context.CancellationToken);
        if (snapshot is null) return NoIdentity();
        return CommandReplies.Compose("📍 铎京 · 中央广场",
            $"Lv.{snapshot.Level} · 金币 {snapshot.Credits} · 钻石 {snapshot.Diamonds}",
            [
                "雾气贴着石板路，北面的铁匠铺还亮着炉火。",
                "",
                "- 后勤部　@1　领取补给（/签到 · /每日任务）",
                "- 抽卡屋　@2　碰碰手气（/抽卡 · /十连抽卡）",
                "- 训练场　@3　锻炼角色（/练级 @1 · /收取练级）",
                "- 仓库　　@4　整理背包（/背包）",
                "",
                "商店与交易摊位还蒙着布——那是下一批工匠的活。"
            ],
            ["/签到", "/每日任务", "/抽卡", "/练级 @1 1", "/背包"]);
    }

    private static async Task<BotReply> QuestsAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<QuestEntry> quests = await deps.Players.GetQuestsAsync(
            context.UserId, context.CancellationToken);
        List<string> lines = quests.Select(quest =>
            $"- @{quest.Index} {quest.Name}　{quest.Progress}/{quest.Target}　{QuestStatus(quest.Status)}").ToList();
        lines.Add($"- 完成后 /领奖 @编号（金币 +{GameplayRules.QuestRewardCredits} / 钻石 +{GameplayRules.QuestRewardDiamonds}）");
        return CommandReplies.ListPage("📋 每日任务", GameplayRules.Today(), lines, context.Page,
            "/每日任务", ["/签到", "/抽卡", "/FunGame模拟", "/状态"]);
    }

    private static string QuestStatus(int status) => status switch
    {
        1 => "✅ 可领取",
        2 => "已领取",
        _ => "进行中"
    };

    private static async Task<BotReply> BagAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<BagEntry> items = await deps.Players.GetBagAsync(context.UserId, context.CancellationToken);
        List<string> lines = items.Select(item =>
            $"- @{item.Index} {GameplayRules.QualityName(item.Quality)} · {item.Name} ×{item.Quantity}"
            + (item.Locked ? " 🔒" : "") + (item.Equipped ? " ⚔️" : "") + (item.Bound ? " 绑定" : "")).ToList();
        if (lines.Count == 0) lines.Add("- 背包空空如也，先 /抽卡 或 /签到");
        lines.Add("- /使用 @编号 [x数量] · /分解 @编号 · /锁定 @编号 · /装备 @编号");
        return CommandReplies.ListPage("🎒 背包", $"{items.Count} 格", lines, context.Page,
            "/背包", ["/抽卡", "/状态"]);
    }

    private static async Task<BotReply> CharactersAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<CharacterEntry> characters = await deps.Players.GetCharactersAsync(
            context.UserId, context.CancellationToken);
        List<string> lines = characters.Select(character =>
            $"- @{character.Index} {character.Name}　Lv.{character.Level} / 上限 {GameplayRules.LevelCap(character.BreakLevel)}"
            + $"　突破 {character.BreakLevel} 阶　装备 {character.EquipmentCount} 件"
            + (character.IsMain ? " ⭐主战" : "")).ToList();
        if (lines.Count == 0) lines.Add("- 还没有角色，输入 /注册 领取初始角色");
        lines.Add("- /设主战 @编号 · /升级 @编号 · /突破 @编号 · /练级 @编号 [分钟]");
        return CommandReplies.ListPage("👤 我的角色", $"{characters.Count} 名", lines, context.Page,
            "/我的角色", ["/状态", "/背包"]);
    }
}
