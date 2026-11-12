using System.Globalization;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Combat;
using FunGame.Core.Library.Constant;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Messaging;
using OshimaBot.Models;
using OshimaBot.Security;
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
        "world-map", "region-info", "home-city", "explore", "explore-info", "explore-claim",
        "magic-card", "magic-card-pack",
        "char", "skill", "item", "stats", "team-stats",
        "rank-win", "rank-team-win", "rank-rating", "rank-team-rating",
        "log", "log-full",
        "sim-debug", "sim-team-map", "sim-team", "sim-map", "sim-mix", "sim",
        // ---- P1：旅人存档 ----
        "register", "status", "look", "notice", "fortune", "signin",
        "quests", "quest-claim", "gacha", "gacha-ten", "gacha-material", "gacha-ten-material", "bag",
        "use", "salvage", "salvage-quality", "exchange", "use-magic-card", "craft-magic-pack", "search",
        "lock", "unlock", "equip", "unequip",
        "characters", "set-main", "levelup", "break", "train", "train-claim", "party",
        "normal-attack-upgrade", "normal-attack-upgrade-info",
        "skill-upgrade", "skill-upgrade-info", "rename-character",
        // ---- P2：经济与战斗 ----
        "shop", "shop-logistics", "shop-weapons", "shop-yuki", "shop-welfare",
        "shop-forge", "shop-horse", "shop-coop", "buy", "market", "list-item", "delist", "market-buy",
        "offers", "offer", "offer-accept", "offer-cancel",
        "forge", "forge-claim", "instance", "instance-claim",
        "duel", "full-duel", "team-duel", "boss", "boss-info", "boss-team", "rank",
        "rank-horse", "rank-coop", "rank-forge",
        // ---- 多人房间 ----
        "room-create", "room-join", "horse-join", "room-quit", "room-start", "room-list", "room-info",
        // ---- 社团 ----
        "club-create", "club-list", "club-info", "club-mine", "club-join", "club-quit", "club-disband",
        "club-members", "club-admins", "club-applications", "club-invites", "club-approve", "club-reject",
        "club-invite", "club-kick", "club-transfer", "club-settings", "club-donate",
        "club-tasks", "club-store", "club-market",
        // ---- 审核（管理员）----
        "rename-review", "rename-approve", "rename-reject",
        // ---- 私信 ----
        "mail-send", "mail-inbox", "mail-read",
        // ---- 活动与运营公告 ----
        "activities", "activity-info", "activity-do", "activity-claim", "set-notice"
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
                    return CommandReplies.Error("群聊不能签发绑定码", "没有生成任何码",
                        "请在与机器人的单聊里发送 /绑定", "/帮助");
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
            usage: "/玩法帮助", description: "查看当前开放的玩法范围", group: CommandGroups.Legacy,
            handler: _ => Task.FromResult(CommandReplies.Compose("🧭 OshimaBot 版本状态",
                "P0 至 P2 玩法已全部开放",
                [
                    "- P0　角色/技能/物品查询、战斗模拟与统计排行",
                    "- P1　存档、背包、签到、抽卡、练级、每日任务",
                    "- P2　商店、市场、报价交易、锻造、秘境、决斗、Boss、排行榜",
                    "- P3（社团/赛马/赛事预测）按计划不在本轮范围"
                ], ["/帮助", "/状态", "/商店"]))),

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

        // 旧版是 4 条独立通道：金币抽卡/十连、钻石抽卡/十连，费用都是单价 × 次数（无折扣）
        "gacha" => new DelegateCommand(
            key: "gacha", name: "抽卡", aliases: ["gacha"], usage: "/抽卡 [数量]",
            description: $"消耗金币抽取（{GameplayRules.GachaCostCredits}/次）",
            group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.GachaAsync(context.UserId, Quantity(context, 0, 1), false,
                    context.CancellationToken))),

        "gacha-ten" => new DelegateCommand(
            key: "gacha-ten", name: "十连抽卡", aliases: ["十连", "gacha10"], usage: "/十连抽卡",
            description: $"金币十连（{GameplayRules.GachaTenCostCredits} 金币，无保底）",
            group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.GachaAsync(context.UserId, 10, false, context.CancellationToken))),

        "gacha-material" => new DelegateCommand(
            key: "gacha-material", name: "钻石抽卡", aliases: ["钻石单抽", "diamond-gacha"],
            usage: "/钻石抽卡 [数量]",
            description: $"消耗钻石抽取（{GameplayRules.GachaCost}/次）",
            group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.GachaAsync(context.UserId, Quantity(context, 0, 1), true,
                    context.CancellationToken))),

        "gacha-ten-material" => new DelegateCommand(
            key: "gacha-ten-material", name: "钻石十连抽卡", aliases: ["钻石十连", "gacha10m"],
            usage: "/钻石十连抽卡",
            description: $"钻石十连（{GameplayRules.GachaTenCost} 钻石，无保底）",
            group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.GachaAsync(context.UserId, 10, true, context.CancellationToken))),

        // 旧版角色养成：普攻/技能升级（默认预览）、角色改名
        "normal-attack-upgrade" => new DelegateCommand(
            key: "normal-attack-upgrade", name: "普攻升级", aliases: ["升级普攻"],
            usage: "/普攻升级 [@编号]", description: "升级角色普攻等级",
            group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.UpgradeNormalAttackAsync(context.UserId, RefIndex(context), false,
                    context.CancellationToken))),

        "normal-attack-upgrade-info" => new DelegateCommand(
            key: "normal-attack-upgrade-info", name: "查看普攻升级", aliases: ["普攻升级信息"],
            usage: "/查看普攻升级 [@编号]", description: "查看下一次普攻升级需求",
            group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.UpgradeNormalAttackAsync(context.UserId, RefIndex(context), true,
                    context.CancellationToken))),

        "skill-upgrade" => new DelegateCommand(
            key: "skill-upgrade", name: "技能升级", aliases: ["升级技能"],
            usage: "/技能升级 @编号 技能名", description: "升级角色技能（等级 0 时即学习）",
            group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.UpgradeSkillAsync(context.UserId, RefIndex(context),
                    JoinFrom(context, 1), false, context.CancellationToken))),

        "skill-upgrade-info" => new DelegateCommand(
            key: "skill-upgrade-info", name: "查看技能升级", aliases: ["技能升级信息"],
            usage: "/查看技能升级 @编号 技能名", description: "查看下一次技能升级需求",
            group: CommandGroups.Player,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.UpgradeSkillAsync(context.UserId, RefIndex(context),
                    JoinFrom(context, 1), true, context.CancellationToken))),

        "rename-character" => new DelegateCommand(
            key: "rename-character", name: "角色改名", aliases: ["改名"],
            usage: "/角色改名 新名字", description: "修改主战角色的名字",
            group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(
                await deps.Players.RenameAsync(context.UserId, JoinFrom(context, 0),
                    context.CancellationToken))),

        "bag" => new DelegateCommand(
            key: "bag", name: "背包", aliases: ["查看库存", "bag", "inventory"], usage: "/背包 [p2]",
            description: "查看背包，@编号 用于其它指令", group: CommandGroups.Player,
            handler: context => BagAsync(deps, context)),

        "search" => new DelegateCommand(
            key: "search", name: "库存搜索", aliases: ["库存查询", "search"], usage: "/库存搜索 <关键词> [p2]",
            description: "按名称或描述搜索背包（旧版库存搜索1/2 合并）", group: CommandGroups.Player,
            prefixMatch: true,
            handler: context => SearchAsync(deps, context)),

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

        "salvage-quality" => new DelegateCommand(
            key: "salvage-quality", name: "品质分解", aliases: ["按品质分解"], usage: "/品质分解 <品质 0-6>",
            description: "按品质批量分解背包里的未锁定物品", group: CommandGroups.Player,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                if (!context.TryInt(0, out int quality) || quality < 0 || quality > 6)
                {
                    return CommandReplies.Error("缺少或无效的品质序号", "没有分解任何物品",
                        "用法：/品质分解 0　（0 普通 / 1 优秀 / 2 稀有 / 3 史诗 / 4 传说 / 5 神话 / 6 不朽）",
                        "/背包");
                }
                return CommandReplies.FromResult(await deps.Players.SalvageByQualityAsync(
                    context.UserId, quality, context.CancellationToken));
            }),

        "exchange" => new DelegateCommand(
            key: "exchange", name: "兑换金币", aliases: ["兑换", "exchange"], usage: "/兑换金币 <钻石数>",
            description: $"钻石换金币（{GameplayRules.ExchangeMaterialUnit} 钻 = {GameplayRules.ExchangeCreditsPerUnit} 金币）",
            group: CommandGroups.Player, audit: true, prefixMatch: true,
            handler: async context =>
            {
                if (!context.TryInt(0, out int materials) || materials < 0)
                {
                    return CommandReplies.Error("缺少或无效的钻石数量", "没有兑换任何金币",
                        $"用法：/兑换金币 100（最低 {GameplayRules.ExchangeMaterialUnit} 钻一档）", "/状态");
                }
                return CommandReplies.FromResult(await deps.Players.ExchangeCreditsAsync(
                    context.UserId, materials, context.CancellationToken));
            }),

        "use-magic-card" => new DelegateCommand(
            key: "use-magic-card", name: "使用魔法卡", aliases: ["魔法卡使用"], usage: "/使用魔法卡 @卡 [@卡包]",
            description: "把魔法卡打进同品质的魔法卡包", group: CommandGroups.Player,
            audit: true, prefixMatch: true,
            handler: async context => CommandReplies.FromResult(await deps.Players.UseMagicCardAsync(
                context.UserId, RefIndex(context), RefIndex(context, 1), context.CancellationToken))),

        "craft-magic-pack" => new DelegateCommand(
            key: "craft-magic-pack", name: "合成魔法卡", aliases: ["合成卡包", "合成魔法卡包"],
            usage: "/合成魔法卡 @1 @2 @3",
            description: "3 张魔法卡合成一个魔法卡包", group: CommandGroups.Player, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Players.CraftMagicCardPackAsync(
                context.UserId, RefIndex(context), RefIndex(context, 1), RefIndex(context, 2),
                context.CancellationToken))),

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

        // ---- P2：经济与战斗（全部由显式 switch 注册）----
        "shop" => new DelegateCommand(
            key: "shop", name: "商店", aliases: ["shop"], usage: "/商店 [p2]",
            description: "铎京补给站货架", group: CommandGroups.Economy,
            handler: context => ShopAsync(context)),

        "buy" => new DelegateCommand(
            key: "buy", name: "购买", aliases: ["buy"], usage: "/购买 @1 [x2]",
            description: "购买当前商店的商品（作用于上次查看的店）", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Economy.BuyAsync(
                context.UserId, null, RefIndex(context), Quantity(context, 1, 1), context.CancellationToken))),

        // 旧版 7 个固定商店：商店1–4 是常规店，商店5–7 是积分店
        "shop-logistics" => StoreCommand("shop-logistics", "商店1", ["后勤部", "后勤"],
            StoreCatalog.LogisticsKey, "探索者协会后勤部（探索许可与魔法卡礼包）"),
        "shop-weapons" => StoreCommand("shop-weapons", "商店2", ["武器商会", "武器店"],
            StoreCatalog.WeaponsKey, "铎京武器商会"),
        "shop-yuki" => StoreCommand("shop-yuki", "商店3", ["杂货铺", "杂货店"],
            StoreCatalog.YukiKey, "小雪杂货铺"),
        "shop-welfare" => StoreCommand("shop-welfare", "商店4", ["慈善基金会", "基金会"],
            StoreCatalog.WelfareKey, "铎京慈善基金会（免费福利，限购 1）"),
        "shop-forge" => StoreCommand("shop-forge", "锻造商店", ["商店5", "锻造积分商店"],
            StoreCatalog.ForgeKey, "用锻造积分兑换"),
        "shop-horse" => StoreCommand("shop-horse", "赛马商店", ["商店6", "赛马积分商店"],
            StoreCatalog.HorseRacingKey, "用赛马积分兑换钻石"),
        "shop-coop" => StoreCommand("shop-coop", "共斗商店", ["商店7", "共斗积分商店"],
            StoreCatalog.CooperativeKey, "用共斗积分兑换"),

        "market" => new DelegateCommand(
            key: "market", name: "市场", aliases: ["market"], usage: "/市场 [p2]",
            description: "旅人挂单列表", group: CommandGroups.Economy,
            handler: context => MarketAsync(deps, context)),

        "list-item" => new DelegateCommand(
            key: "list-item", name: "上架", aliases: ["list"], usage: "/上架 @1 <价格>",
            description: "把背包物品挂到市场（收上架费）", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Economy.ListOnMarketAsync(
                context.UserId, RefIndex(context), Price(context), context.CancellationToken))),

        "delist" => new DelegateCommand(
            key: "delist", name: "下架", aliases: ["delist"], usage: "/下架 @1",
            description: "撤回自己的挂单", group: CommandGroups.Economy,
            handler: async context => CommandReplies.FromResult(await deps.Economy.DelistAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "market-buy" => new DelegateCommand(
            key: "market-buy", name: "购买市场", aliases: ["marketbuy"], usage: "/购买市场 @1",
            description: "购买市场挂单（买卖双方各记一笔账）", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Economy.BuyMarketAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "offers" => new DelegateCommand(
            key: "offers", name: "报价列表", aliases: ["offers"], usage: "/报价列表 [p2]",
            description: "查看公开报价单", group: CommandGroups.Economy,
            handler: context => OffersAsync(deps, context)),

        "offer" => new DelegateCommand(
            key: "offer", name: "报价", aliases: ["offer"], usage: "/报价 @1 <价格>",
            description: "发布物品报价单（不收上架费）", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Economy.CreateOfferAsync(
                context.UserId, RefIndex(context), Price(context), context.CancellationToken))),

        "offer-accept" => new DelegateCommand(
            key: "offer-accept", name: "接受报价", aliases: ["offeraccept"], usage: "/接受报价 @1",
            description: "按报价买下物品", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Economy.AcceptOfferAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "offer-cancel" => new DelegateCommand(
            key: "offer-cancel", name: "撤回报价", aliases: ["offercancel"], usage: "/撤回报价 @1",
            description: "撤回自己发布的报价单", group: CommandGroups.Economy,
            handler: async context => CommandReplies.FromResult(await deps.Economy.CancelOfferAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "forge" => new DelegateCommand(
            key: "forge", name: "锻造", aliases: ["forge"], usage: "/锻造 @1 [分钟]",
            description: "消耗 2 个同类材料 + 金币开始锻造", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Economy.ForgeAsync(
                context.UserId, RefIndex(context),
                Quantity(context, 1, GameplayRules.ForgeDefaultMinutes), context.CancellationToken))),

        "forge-claim" => new DelegateCommand(
            key: "forge-claim", name: "收取锻造", aliases: ["forgeclaim"], usage: "/收取锻造",
            description: "结算已完成的锻造", group: CommandGroups.Economy,
            handler: async context => CommandReplies.FromResult(
                await deps.Economy.ClaimForgeAsync(context.UserId, context.CancellationToken))),

        "instance" => new DelegateCommand(
            key: "instance", name: "秘境", aliases: ["instance"], usage: "/秘境 [类型] [难度]",
            description: "进入秘境探索（7 类 · 难度 1-5 · 定时结算）", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Economy.InstanceAsync(
                context.UserId, context.ArgumentText, context.CancellationToken))),

        "instance-claim" => new DelegateCommand(
            key: "instance-claim", name: "收取秘境", aliases: ["instanceclaim"], usage: "/收取秘境",
            description: "结算秘境经验、金币与掉落", group: CommandGroups.Economy,
            handler: async context => CommandReplies.FromResult(
                await deps.Economy.ClaimInstanceAsync(context.UserId, context.CancellationToken))),

        "duel" => new DelegateCommand(
            key: "duel", name: "决斗", aliases: ["duel"], usage: "/决斗 [UID|昵称]",
            description: "即时决斗，胜负都累计战绩；不给对手就打随机对手", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                string? target = Target(context);
                return target is null
                    ? CommandReplies.FromResult(await deps.Economy.DuelAsync(
                        context.UserId, context.CancellationToken))
                    : CommandReplies.FromResult((await deps.Economy.DuelWithAsync(
                        context.UserId, target, context.CancellationToken)).Result);
            }),

        "full-duel" => new DelegateCommand(
            key: "full-duel", name: "完整决斗", aliases: ["完整战斗"], usage: "/完整决斗 <UID|昵称> [p2]",
            description: "与指定旅人单挑，并附逐回合完整战报", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                string? target = Target(context);
                if (target is null)
                {
                    return CommandReplies.Error("没有指定对手", "没有开始战斗",
                        "用法：/完整决斗 123456　（UID / Handle / 昵称都行）", "/排行榜");
                }
                BattleReport report = await deps.Economy.DuelWithAsync(
                    context.UserId, target, context.CancellationToken);
                return Report(report, context.Page, $"/完整决斗 {target}");
            }),

        "team-duel" => new DelegateCommand(
            key: "team-duel", name: "小队决斗", aliases: ["小队对战"], usage: "/小队决斗 <UID|昵称> [p2]",
            description: "双方小队对战，附逐回合战报", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                string? target = Target(context);
                if (target is null)
                {
                    return CommandReplies.Error("没有指定对手", "没有开始战斗",
                        "用法：/小队决斗 123456　先 /小队 编成 1–3 人", "/小队");
                }
                BattleReport report = await deps.Economy.TeamDuelWithAsync(
                    context.UserId, target, context.CancellationToken);
                return Report(report, context.Page, $"/小队决斗 {target}");
            }),

        "boss" => new DelegateCommand(
            key: "boss", name: "Boss", aliases: ["挑战Boss"], usage: "/Boss [编号]",
            description: "讨伐 Boss；不给编号就打当前残血的那只", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context => CommandReplies.FromResult(
                context.TryInt(0, out int index)
                    ? await deps.Economy.BossAtAsync(context.UserId, index, context.CancellationToken)
                    : await deps.Economy.BossAsync(context.UserId, context.CancellationToken))),

        "boss-info" => new DelegateCommand(
            key: "boss-info", name: "查询Boss", aliases: ["Boss列表", "bossinfo"], usage: "/查询Boss [编号]",
            description: "查看讨伐目标列表，或某只目标的详细属性", group: CommandGroups.Economy,
            prefixMatch: true,
            handler: context => Task.FromResult(BossInfo(context))),

        "boss-team" => new DelegateCommand(
            key: "boss-team", name: "小队讨伐Boss", aliases: ["Boss小队讨伐", "小队讨伐"],
            usage: "/小队讨伐Boss <编号> [p2]",
            description: "用小队编成讨伐指定编号的 Boss", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                if (!context.TryInt(0, out int index) || index <= 0)
                {
                    return CommandReplies.Error("缺少或无效的 Boss 编号", "没有开始讨伐",
                        "用 /查询Boss 查看编号", "/查询Boss");
                }
                BattleReport report = await deps.Economy.BossTeamAsync(
                    context.UserId, index, context.CancellationToken);
                return Report(report, context.Page, $"/小队讨伐Boss {index}");
            }),

        "rank" => new DelegateCommand(
            key: "rank", name: "排行榜", aliases: ["rank", "ranklist"], usage: "/排行榜 [金币|等级|战绩]",
            description: "查看排行榜：金币 / 等级 / 战绩 / 赛马 / 共斗 / 锻造", group: CommandGroups.Economy,
            handler: context => RankAsync(deps, context)),

        // ---- 多人房间（旧版 OnlineService）：按群隔离，房号 7 位，空闲 10 分钟解散 ----
        "room-create" => new DelegateCommand(
            key: "room-create", name: "创建房间",
            aliases: ["建房", "创建赛马", "创建共斗", "创建混战", "创建团战"],
            usage: "/创建房间 <赛马|共斗|混战|团战> [密码]",
            description: "建多人房间，返回 7 位房号", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                RoomKind? kind = KindFromText(context.Raw);
                string? password;
                if (kind is null)
                {
                    kind = RoomRules.ParseKind(context.FirstArgument);
                    password = context.Args.Count > 1 ? context.Args[1] : null;
                }
                else
                {
                    password = context.FirstArgument;
                }
                if (kind is null)
                {
                    return CommandReplies.Error("房间类型无效", "没有创建任何房间",
                        $"用法：/创建房间 {RoomRules.KindHint()} [密码]", "/帮助");
                }
                return CommandReplies.FromResult(await deps.Rooms.CreateAsync(
                    context.UserId, Scope(context), kind.Value.ToString(), password, context.CancellationToken));
            }),

        "room-join" => new DelegateCommand(
            key: "room-join", name: "加入房间", aliases: ["进房"], usage: "/加入房间 <房号> [密码]",
            description: "用房号加入本群的房间", group: CommandGroups.Economy, prefixMatch: true,
            handler: async context =>
            {
                string? code = context.FirstArgument;
                if (string.IsNullOrWhiteSpace(code))
                {
                    return CommandReplies.Error("没有给房号", "没有加入任何房间",
                        "用法：/加入房间 房号 [密码]　房号用 /房间列表 查", "/房间列表");
                }
                return CommandReplies.FromResult(await deps.Rooms.JoinAsync(
                    context.UserId, Scope(context), code, context.Args.Count > 1 ? context.Args[1] : null,
                    context.CancellationToken));
            }),

        "horse-join" => new DelegateCommand(
            key: "horse-join", name: "加入赛马", aliases: ["进入赛马"], usage: "/加入赛马",
            description: "直接加入本群的赛马房间，不用记房号", group: CommandGroups.Economy,
            handler: async context => CommandReplies.FromResult(await deps.Rooms.JoinHorseRoomAsync(
                context.UserId, Scope(context), context.CancellationToken))),

        "room-quit" => new DelegateCommand(
            key: "room-quit", name: "退出房间", aliases: ["离开房间"], usage: "/退出房间",
            description: "离开当前房间；房主离开会自动转让", group: CommandGroups.Economy,
            handler: async context => CommandReplies.FromResult(await deps.Rooms.QuitAsync(
                context.UserId, Scope(context), context.CancellationToken))),

        "room-start" => new DelegateCommand(
            key: "room-start", name: "开始游戏", aliases: ["开始赛马"], usage: "/开始游戏",
            description: "房主开一局（赛马房间任何人都能开）", group: CommandGroups.Economy,
            audit: true,
            handler: async context =>
            {
                BattleReport report = await deps.Rooms.StartAsync(
                    context.UserId, Scope(context), context.CancellationToken);
                return Report(report, context.Page, "/开始游戏");
            }),

        "room-list" => new DelegateCommand(
            key: "room-list", name: "房间列表", aliases: ["房间"], usage: "/房间列表 [p2]",
            description: "本群当前有哪些房间", group: CommandGroups.Economy,
            handler: context => Task.FromResult(RoomList(deps, context))),

        "room-info" => new DelegateCommand(
            key: "room-info", name: "房间信息", aliases: ["我的房间"], usage: "/房间信息 [房号]",
            description: "查看成员与状态；不给房号就看自己所在的房间", group: CommandGroups.Economy,
            handler: context => Task.FromResult(RoomInfo(deps, context))),

        "rank-horse" => new DelegateCommand(
            key: "rank-horse", name: "赛马排行榜", aliases: ["赛马积分榜"], usage: "/赛马排行榜",
            description: "按赛马积分排行（/开始游戏 跑赛马来攒）", group: CommandGroups.Economy,
            handler: context => RankAsync(deps, context, "horse")),

        "rank-coop" => new DelegateCommand(
            key: "rank-coop", name: "共斗排行榜", aliases: ["共斗积分榜"], usage: "/共斗排行榜",
            description: "按共斗积分排行（共斗房间来攒）", group: CommandGroups.Economy,
            handler: context => RankAsync(deps, context, "coop")),

        "rank-forge" => new DelegateCommand(
            key: "rank-forge", name: "锻造排行榜", aliases: ["锻造积分榜"], usage: "/锻造排行榜",
            description: "按锻造积分排行（/锻造 来攒）", group: CommandGroups.Economy,
            handler: context => RankAsync(deps, context, "forge")),

        // ---- 社团（M6.2）：旧版一个社团一个 JSON 文件，这里落库；一个人只能在一个社团 ----
        "club-create" => new DelegateCommand(
            key: "club-create", name: "创建社团", aliases: ["建社团"], usage: "/创建社团 <前缀> [私密]",
            description: "前缀 3–4 位；名字自动生成，可用 /社团设置 名称 改", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                string? prefix = context.FirstArgument;
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    return CommandReplies.Error("没有给社团前缀", "没有创建任何社团",
                        "用法：/创建社团 ABC　后面加「私密」则是邀请制", "/社团列表");
                }
                bool isPublic = !context.HasFlag("私密") && !context.HasFlag("private");
                return CommandReplies.FromResult(await deps.Clubs.CreateAsync(
                    context.UserId, prefix, isPublic, context.CancellationToken));
            }),

        "club-list" => new DelegateCommand(
            key: "club-list", name: "社团列表", aliases: ["社团"], usage: "/社团列表 [p2]",
            description: "本服所有社团", group: CommandGroups.Economy,
            handler: context => ClubListAsync(deps, context)),

        "club-info" => new DelegateCommand(
            key: "club-info", name: "查看社团", aliases: ["社团信息"], usage: "/查看社团 <编号>",
            description: "看社团公告、成员数、基金与加入规则", group: CommandGroups.Economy,
            prefixMatch: true,
            handler: context => ClubInfoAsync(deps, context)),

        "club-mine" => new DelegateCommand(
            key: "club-mine", name: "我的社团", aliases: ["我的公会"], usage: "/我的社团",
            description: "查看自己所在社团", group: CommandGroups.Economy,
            handler: context => ClubMineAsync(deps, context)),

        "club-join" => new DelegateCommand(
            key: "club-join", name: "加入社团", aliases: ["申请入团"], usage: "/加入社团 <编号>",
            description: "公开社团直接进；需批准的会进申请队列", group: CommandGroups.Economy,
            prefixMatch: true,
            handler: async context =>
            {
                if (!context.TryInt(0, out int clubId) || clubId <= 0)
                {
                    return CommandReplies.Error("缺少社团编号", "没有加入任何社团",
                        "用 /社团列表 查看编号", "/社团列表");
                }
                return CommandReplies.FromResult(await deps.Clubs.JoinAsync(
                    context.UserId, clubId, context.CancellationToken));
            }),

        "club-quit" => new DelegateCommand(
            key: "club-quit", name: "退出社团", aliases: ["退团"], usage: "/退出社团",
            description: "社长需先转让或解散", group: CommandGroups.Economy,
            handler: async context => CommandReplies.FromResult(await deps.Clubs.QuitAsync(
                context.UserId, context.CancellationToken))),

        "club-disband" => new DelegateCommand(
            key: "club-disband", name: "解散社团", aliases: ["解散公会"], usage: "/解散社团",
            description: "社长解散整个社团", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Clubs.DisbandAsync(
                context.UserId, context.CancellationToken))),

        "club-members" => new DelegateCommand(
            key: "club-members", name: "查看社团成员", aliases: ["社团成员"], usage: "/查看社团成员 [p2]",
            description: "成员列表与身份（@编号 用于踢出/转让）", group: CommandGroups.Economy,
            handler: context => ClubMembersAsync(deps, context)),

        "club-admins" => new DelegateCommand(
            key: "club-admins", name: "查看社团管理", aliases: ["社团管理"], usage: "/查看社团管理",
            description: "社长与管理人员", group: CommandGroups.Economy,
            handler: context => ClubAdminsAsync(deps, context)),

        "club-applications" => new DelegateCommand(
            key: "club-applications", name: "查看申请人列表", aliases: ["查看申请人"], usage: "/查看申请人列表",
            description: "等待审批的入团申请（社长/管理）", group: CommandGroups.Economy,
            handler: context => ClubApplicationsAsync(deps, context)),

        "club-invites" => new DelegateCommand(
            key: "club-invites", name: "查看受邀人列表", aliases: ["查看受邀人"], usage: "/查看受邀人列表",
            description: "已发出但还没接受的邀请", group: CommandGroups.Economy,
            handler: context => ClubInvitesAsync(deps, context)),

        "club-approve" => new DelegateCommand(
            key: "club-approve", name: "社团批准", aliases: ["批准入团"], usage: "/社团批准 @编号",
            description: "批准申请人入团", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Clubs.ApproveAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "club-reject" => new DelegateCommand(
            key: "club-reject", name: "社团拒绝", aliases: ["拒绝入团"], usage: "/社团拒绝 @编号",
            description: "拒绝入团申请", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Clubs.RejectAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "club-invite" => new DelegateCommand(
            key: "club-invite", name: "邀请加入", aliases: ["邀请入团"], usage: "/邀请加入 <UID|昵称>",
            description: "邀请某人加入社团（可绕过审批）", group: CommandGroups.Economy, audit: true,
            prefixMatch: true,
            handler: async context =>
            {
                string? target = Target(context);
                if (target is null)
                {
                    return CommandReplies.Error("没有指定邀请对象", "没有邀请任何人",
                        "用法：/邀请加入 123456　（UID / Handle / 昵称）", "/社团成员");
                }
                return CommandReplies.FromResult(await deps.Clubs.InviteAsync(
                    context.UserId, target, context.CancellationToken));
            }),

        "club-kick" => new DelegateCommand(
            key: "club-kick", name: "社团踢出", aliases: ["踢出社团"], usage: "/社团踢出 @编号",
            description: "踢出成员（管理不能踢管理）", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Clubs.KickAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "club-transfer" => new DelegateCommand(
            key: "club-transfer", name: "社团转让", aliases: ["转让社团"], usage: "/社团转让 @编号",
            description: "把社长转让给成员", group: CommandGroups.Economy, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Clubs.TransferAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "club-settings" => new DelegateCommand(
            key: "club-settings", name: "社团设置", aliases: ["社团配置"], usage: "/社团设置 <公开|私密|需批准|免批准|公告 文本|名称 新名|任命 @编号|卸任 @编号>",
            description: "改社团规则、公告、名字与管理人员", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                string? action = context.FirstArgument;
                if (string.IsNullOrWhiteSpace(action))
                {
                    return CommandReplies.Error("没有说要改什么", "没有修改任何设置",
                        "可用：公开 / 私密 / 需批准 / 免批准 / 公告 <文本> / 名称 <新名> / 任命 @编号 / 卸任 @编号",
                        "/我的社团");
                }
                string? value = context.Args.Count > 1 ? JoinFrom(context, 1) : null;
                return CommandReplies.FromResult(await deps.Clubs.UpdateSettingsAsync(
                    context.UserId, action, value, context.CancellationToken));
            }),

        "club-donate" => new DelegateCommand(
            key: "club-donate", name: "社团捐献", aliases: ["捐献社团"], usage: "/社团捐献 <金币>",
            description: "往社团基金投金币，计入个人捐献", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                if (!context.TryInt(0, out int credits) || credits <= 0)
                {
                    return CommandReplies.Error("捐献数量不合法", "没有捐献任何金币",
                        "用法：/社团捐献 1000", "/社团捐献");
                }
                return CommandReplies.FromResult(await deps.Clubs.DonateAsync(
                    context.UserId, credits, context.CancellationToken));
            }),

        "club-tasks" => new DelegateCommand(
            key: "club-tasks", name: "社团任务", aliases: ["社团周常"], usage: "/社团任务 [提交 @编号 [x数量] | 领取 编号]",
            description: "周常任务进度；提交物资推进征集，领取把奖励打进社团基金", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                string action = (context.FirstArgument ?? "").Trim();
                if (action.Length == 0 || action is "列表" or "list")
                {
                    return await ClubTasksAsync(deps, context);
                }
                if (action is "提交" or "submit" or "上交")
                {
                    return CommandReplies.FromResult(await deps.Clubs.ContributeTaskAsync(
                        context.UserId, RefIndex(context, 1), Quantity(context, 2, 1), context.CancellationToken));
                }
                if (action is "领取" or "结算" or "claim")
                {
                    if (!context.TryInt(1, out int index) || index <= 0)
                    {
                        return CommandReplies.Error("没有给任务编号", "没有结算任何任务",
                            "用法：/社团任务 领取 1　编号用 /社团任务 查看", "/社团任务");
                    }
                    return CommandReplies.FromResult(await deps.Clubs.ClaimTaskAsync(
                        context.UserId, index, context.CancellationToken));
                }
                return CommandReplies.Error("不认识的子命令", "没有执行任何操作",
                    "用法：/社团任务　/社团任务 提交 @编号 x2　/社团任务 领取 1", "/社团任务");
            }),

        "club-store" => new DelegateCommand(
            key: "club-store", name: "社团商店", aliases: ["社团兑换"], usage: "/社团商店 [买 编号 [x数量] | 卖 @编号 [x数量]]",
            description: "用社团基金兑换钻石与突破材料；管理可用基金收购成员物品", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                string action = (context.FirstArgument ?? "").Trim();
                if (action.Length == 0 || action is "列表" or "list")
                {
                    return await ClubStoreAsync(deps, context);
                }
                if (action is "买" or "购买" or "兑换" or "buy")
                {
                    if (!context.TryInt(1, out int index) || index <= 0)
                    {
                        return CommandReplies.Error("没有给商品编号", "没有扣基金，也没有发放物品",
                            "用法：/社团商店 买 1 x2　编号用 /社团商店 查看", "/社团商店");
                    }
                    return CommandReplies.FromResult(await deps.Clubs.StoreBuyAsync(
                        context.UserId, index, Quantity(context, 2, 1), context.CancellationToken));
                }
                if (action is "卖" or "出售" or "收购" or "sell")
                {
                    return CommandReplies.FromResult(await deps.Clubs.StoreSellAsync(
                        context.UserId, RefIndex(context, 1), Quantity(context, 2, 1), context.CancellationToken));
                }
                return CommandReplies.Error("不认识的子命令", "没有执行任何操作",
                    "用法：/社团商店　/社团商店 买 1　/社团商店 卖 @编号", "/社团商店");
            }),

        "club-market" => new DelegateCommand(
            key: "club-market", name: "社团市场", aliases: ["社团交易"], usage: "/社团市场 [上架 @编号 价格 | 下架 编号 | 买 编号 | 清空]",
            description: "社团内部的二手交易，手续费进社团基金", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                string action = (context.FirstArgument ?? "").Trim();
                if (action.Length == 0 || action is "列表" or "list")
                {
                    return await ClubMarketAsync(deps, context);
                }
                if (action is "上架" or "出售" or "挂单" or "sell")
                {
                    int itemIndex = RefIndex(context, 1);
                    if (itemIndex <= 0 || !long.TryParse(context.Args.Count > 2 ? context.Args[2] : "", out long price))
                    {
                        return CommandReplies.Error("上架参数不完整", "没有上架任何物品",
                            "用法：/社团市场 上架 @物品编号 800", "/社团市场");
                    }
                    return CommandReplies.FromResult(await deps.Clubs.MarketListAsync(
                        context.UserId, itemIndex, price, context.CancellationToken));
                }
                if (action is "下架" or "撤回" or "delist")
                {
                    return CommandReplies.FromResult(await deps.Clubs.MarketDelistAsync(
                        context.UserId, RefIndex(context, 1), context.CancellationToken));
                }
                if (action is "买" or "购买" or "buy")
                {
                    return CommandReplies.FromResult(await deps.Clubs.MarketBuyAsync(
                        context.UserId, RefIndex(context, 1), context.CancellationToken));
                }
                if (action is "清空" or "clear")
                {
                    return CommandReplies.FromResult(await deps.Clubs.MarketClearAsync(
                        context.UserId, context.CancellationToken));
                }
                return CommandReplies.Error("不认识的子命令", "没有执行任何操作",
                    "用法：/社团市场　/社团市场 上架 @编号 800　/社团市场 买 1　/社团市场 下架 1　/社团市场 清空",
                    "/社团市场");
            }),

        // ---- 改名审核：公开名称先审后生效（平台规范），管理员专属 ----
        "rename-review" => new DelegateCommand(
            key: "rename-review", name: "改名审核", aliases: ["审核列表", "待审改名"], usage: "/改名审核",
            description: "待审的改名申请（社团名等公开名称）", group: CommandGroups.Economy,
            prefixMatch: true, requiredRole: BotRole.Admin,
            handler: context => RenameReviewAsync(deps, context)),

        "rename-approve" => new DelegateCommand(
            key: "rename-approve", name: "批准改名", aliases: ["通过改名"], usage: "/批准改名 编号",
            description: "通过改名申请并对全服生效", group: CommandGroups.Economy,
            prefixMatch: true, requiredRole: BotRole.Admin, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Clubs.ApproveRenameAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        "rename-reject" => new DelegateCommand(
            key: "rename-reject", name: "拒绝改名", aliases: ["驳回改名"], usage: "/拒绝改名 编号",
            description: "拒绝改名申请，原名保持不变", group: CommandGroups.Economy,
            prefixMatch: true, requiredRole: BotRole.Admin, audit: true,
            handler: async context => CommandReplies.FromResult(await deps.Clubs.RejectRenameAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        // ---- 私信（旧版 user_mails，内容过 NamingPolicy）----
        "mail-send" => new DelegateCommand(
            key: "mail-send", name: "私信", aliases: ["发送私信"], usage: "/私信 <UID|Handle|昵称> <内容>",
            description: "给指定旅人发一条私信，每天有上限", group: CommandGroups.Economy,
            prefixMatch: true, audit: true,
            handler: async context =>
            {
                string? target = Target(context);
                string content = JoinFrom(context, 1);
                if (target is null || content.Length == 0)
                {
                    return CommandReplies.Error("私信参数不完整", "没有发送任何私信",
                        "用法：/私信 123456 周末一起打共斗吗", "/收件箱");
                }
                return CommandReplies.FromResult(await deps.Mail.SendAsync(
                    context.UserId, target, content, context.CancellationToken));
            }),

        "mail-inbox" => new DelegateCommand(
            key: "mail-inbox", name: "收件箱", aliases: ["信箱", "我的私信"], usage: "/收件箱 [未读] [p2]",
            description: "查看私信列表，最近 50 封", group: CommandGroups.Economy,
            handler: context => MailInboxAsync(deps, context)),

        "mail-read" => new DelegateCommand(
            key: "mail-read", name: "读信", aliases: ["查看私信"], usage: "/读信 <编号>",
            description: "读一封私信（读后标记已读）", group: CommandGroups.Economy,
            prefixMatch: true,
            handler: async context => CommandReplies.FromResult(await deps.Mail.ReadAsync(
                context.UserId, RefIndex(context), context.CancellationToken))),

        // ---- 活动（旧版 GetEvents / PerformEvent / ClaimEventPrize）----
        "activities" => new DelegateCommand(
            key: "activities", name: "活动", aliases: ["活动中心", "events"], usage: "/活动",
            description: "当前有哪些活动、进行中还是未开放", group: CommandGroups.Economy,
            handler: context => ActivitiesAsync(deps, context)),

        "activity-info" => new DelegateCommand(
            key: "activity-info", name: "查活动", aliases: ["活动详情"], usage: "/查活动 <编号>",
            description: "活动详情：任务进度、成本与奖励", group: CommandGroups.Economy,
            prefixMatch: true,
            handler: context => ActivityInfoAsync(deps, context)),

        "activity-do" => new DelegateCommand(
            key: "activity-do", name: "做活动任务", aliases: ["做活动"], usage: "/做活动任务 <活动> <任务> [@物品]",
            description: "提交资源推进活动任务；成本是物品时用 @编号 指定", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                if (!context.TryInt(0, out int activity) || activity <= 0
                    || !context.TryInt(1, out int quest) || quest <= 0)
                {
                    return CommandReplies.Error("活动或任务编号缺失", "没有扣除任何资源",
                        "用法：/做活动任务 1 2　成本是物品时 /做活动任务 1 2 @3", "/活动");
                }
                return CommandReplies.FromResult(await deps.Activities.PerformAsync(
                    context.UserId, activity, quest, RefIndex(context, 2), context.CancellationToken));
            }),

        "activity-claim" => new DelegateCommand(
            key: "activity-claim", name: "领取奖励", aliases: ["活动领奖"], usage: "/领取奖励 <活动> <任务>",
            description: "领取已完成的活动任务奖励（每个任务一次）", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                if (!context.TryInt(0, out int activity) || activity <= 0
                    || !context.TryInt(1, out int quest) || quest <= 0)
                {
                    return CommandReplies.Error("活动或任务编号缺失", "没有发放任何奖励",
                        "用法：/领取奖励 1 2　编号用 /查活动 1 查看", "/活动");
                }
                return CommandReplies.FromResult(await deps.Activities.ClaimAsync(
                    context.UserId, activity, quest, context.CancellationToken));
            }),

        "set-notice" => new DelegateCommand(
            key: "set-notice", name: "设置公告", aliases: ["修改公告"], usage: "/设置公告 <文本>",
            description: "更新运营公告（管理员），内容过命名规范校验", group: CommandGroups.Economy,
            requiredRole: BotRole.Admin, audit: true, prefixMatch: true,
            handler: async context =>
            {
                string text = context.ArgumentText.Trim();
                if (text.Length == 0)
                {
                    return CommandReplies.Error("没有给公告内容", "没有修改公告",
                        "用法：/设置公告 周末双倍经验，记得上线", "/公告");
                }
                return CommandReplies.FromResult(await deps.Activities.SetNoticeAsync(
                    text, context.CancellationToken));
            }),

        // ---- 世界探索（旧版 探索 / 小队探索 / 探索信息 / 探索结算）----
        "explore" => new DelegateCommand(
            key: "explore", name: "探索", aliases: ["前往", "小队探索"],
            usage: "/探索 <地区编号> <角色序号…>　/小队探索 <地区编号>",
            description: "派角色去地区探索，2 分钟后可结算；许可按「人数 × 星级」扣", group: CommandGroups.Economy,
            audit: true, prefixMatch: true,
            handler: async context =>
            {
                // 「小队探索 1」是自带小队出发的旧写法：动词里已经含「小队」，
                // 所以这里从原始输入判断，而不是看参数（CommandContext.Verb 只会给规范名）。
                bool squadVerb = context.Raw.TrimStart().TrimStart('/')
                    .StartsWith("小队探索", StringComparison.OrdinalIgnoreCase);

                if (!context.TryInt(0, out int region) || region <= 0)
                {
                    return CommandReplies.Error("缺少地区编号", "没有开始探索",
                        squadVerb
                            ? "用法：/小队探索 1（编号用 /世界地图 查）"
                            : "用法：/探索 1 1 2（地区 + 角色序号），或 /小队探索 1；编号用 /世界地图 查",
                        "/世界地图");
                }

                string? tail = context.Args.Count > 1 ? context.Args[1] : null;
                bool useSquad = squadVerb || tail is "小队" or "squad" or "队";
                List<int> indexes = useSquad
                    ? []
                    : [.. context.Args.Skip(1)
                        .Select(arg => int.TryParse(arg.TrimStart('@'), out int value) ? value : 0)
                        .Where(value => value > 0)];
                return CommandReplies.FromResult(await deps.Explore.StartAsync(
                    context.UserId, region, indexes, useSquad, context.CancellationToken));
            }),

        "explore-info" => new DelegateCommand(
            key: "explore-info", name: "探索信息", aliases: ["探索状态"], usage: "/探索信息",
            description: "剩余探索许可、进行中的探索与剩余时间", group: CommandGroups.Economy,
            handler: context => ExploreInfoAsync(deps, context)),

        "explore-claim" => new DelegateCommand(
            key: "explore-claim", name: "探索结算", aliases: ["探索收取"], usage: "/探索结算",
            description: "结算已完成的探索（遇敌会真的打一场）", group: CommandGroups.Economy,
            audit: true,
            handler: async context =>
            {
                BattleReport report = await deps.Explore.ClaimAsync(context.UserId, context.CancellationToken);
                return Report(report, context.Page, "/探索结算");
            }),

        // ---- 世界地图（旧版 世界地图 / 查地区 / 主城）----
        "world-map" => new DelegateCommand(
            key: "world-map", name: "世界地图", aliases: ["看地图", "地区列表"], usage: "/世界地图",
            description: "列出全部地区：编号、分类、难度、天气与可采材料", group: CommandGroups.Query,
            handler: context => Task.FromResult(WorldMapAsync(deps, context))),

        "region-info" => new DelegateCommand(
            key: "region-info", name: "查地区", aliases: ["查询地区", "地区"], usage: "/查地区 [编号|名称]",
            description: "地区详情：头目、生物、材料、NPC、区域与探索剧情", group: CommandGroups.Query,
            prefixMatch: true,
            handler: context => Task.FromResult(RegionInfoAsync(deps, context))),

        "home-city" => new DelegateCommand(
            key: "home-city", name: "主城", aliases: ["铎京"], usage: "/主城",
            description: "查看主城铎京城", group: CommandGroups.Query,
            handler: context => Task.FromResult(HomeCityAsync(deps, context))),

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
        // S9：把触发者写入 simulation_runs.user_id，便于按用户复盘；
        // userId=0 的匿名 / 第三方请求留空。
        request.UserId = context.UserId > 0 ? context.UserId : null;
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
            $"主战 Lv.{snapshot.MainLevel}　金币 {snapshot.Credits}　钻石 {snapshot.Materials}　经验 {snapshot.MainExp:0.##}",
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
            $"主战 Lv.{snapshot.MainLevel} · 金币 {snapshot.Credits} · 钻石 {snapshot.Materials}",
            [
                "雾气贴着石板路，北面的铁匠铺还亮着炉火。",
                "",
                "- 后勤部　@1　领取补给（/签到 · /每日任务）",
                "- 抽卡屋　@2　碰碰手气（/抽卡 · /十连抽卡）",
                "- 训练场　@3　锻炼角色（/练级 @1 · /收取练级）",
                "- 仓库　　@4　整理背包（/背包）",
                "- 补给站　@5　/商店 · /购买 @1",
                "- 交易摊　@6　/市场 · /上架 @1 200",
                "- 锻造炉　@7　/锻造 @1 · /收取锻造",
                "",
                "补给站和交易摊已经开张，铁匠的炉火也重新烧起来了。"
            ],
            ["/签到", "/每日任务", "/商店", "/市场", "/背包"]);
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
        lines.Add("- /使用 @编号 · /分解 @编号 · /品质分解 <0-6> · /锁定 @编号");
        lines.Add("- /库存搜索 <关键词> · /兑换金币 <钻石> · /合成魔法卡 @1 @2 @3");
        return CommandReplies.ListPage("🎒 背包", $"{items.Count} 格", lines, context.Page,
            "/背包", ["/抽卡", "/状态"]);
    }

    private static async Task<BotReply> SearchAsync(CommandDependencies deps, CommandContext context)
    {
        string keyword = context.ArgumentText.Trim();
        if (keyword.Length == 0)
        {
            return CommandReplies.Error("搜索关键词为空", "没有检索背包",
                "用法：/库存搜索 火之矢　（同时匹配名称与描述）", "/背包");
        }
        IReadOnlyList<BagSearchEntry> hits = await deps.Players.SearchBagAsync(
            context.UserId, keyword, context.CancellationToken);
        List<string> lines = hits.Select(hit =>
            $"- @{hit.Index} {GameplayRules.QualityName(hit.Quality)} · {hit.Name} ×{hit.Quantity}"
            + (hit.Locked ? " 🔒" : "")
            + (string.IsNullOrWhiteSpace(hit.Description) ? "" : $"　{hit.Description}")).ToList();
        if (lines.Count == 0)
        {
            lines.Add($"- 背包里没有名称或描述包含「{keyword}」的物品");
        }
        lines.Add("- 编号与 /背包 一致：/使用 @编号 · /分解 @编号 · /锁定 @编号");
        return CommandReplies.ListPage("🔍 库存搜索", $"「{keyword}」· 命中 {hits.Count} 格",
            lines, context.Page, $"/库存搜索 {keyword}", ["/背包", "/状态"]);
    }

    private static async Task<BotReply> CharactersAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<CharacterEntry> characters = await deps.Players.GetCharactersAsync(
            context.UserId, context.CancellationToken);
        List<string> lines = characters.Select(character =>
            $"- @{character.Index} {character.Name}　Lv.{character.Level} / 上限 {GameplayRules.NextBreakLevel(character.BreakLevel).ToString() ?? "满级"}"
            + $"　突破 {GameplayRules.BreakProgress(character.BreakLevel)}　装备 {character.EquipmentCount} 件"
            + (character.IsMain ? " ⭐主战" : "")).ToList();
        if (lines.Count == 0) lines.Add("- 还没有角色，输入 /注册 领取初始角色");
        lines.Add("- /设主战 @编号 · /升级 @编号 · /突破 @编号 · /练级 @编号 [分钟]");
        return CommandReplies.ListPage("👤 我的角色", $"{characters.Count} 名", lines, context.Page,
            "/我的角色", ["/状态", "/背包"]);
    }

    // ---- 社团指令辅助 ----

    private static async Task<BotReply> ExploreInfoAsync(CommandDependencies deps, CommandContext context)
    {
        (ExploreQuota quota, IReadOnlyList<ExploreRunView> runs) = await deps.Explore.InfoAsync(
            context.UserId, context.CancellationToken);

        List<string> lines =
        [
            $"- 探索许可：{quota.Left}/{quota.DailyMax}（今日 {quota.Day}，每天 4:00 恢复 {ExploreRules.DailyQuota}）",
            $"- 午餐 11–13 点：{(quota.LunchClaimed ? "已领 +30" : "未领")}"
            + $"　晚餐 17–19 点：{(quota.DinnerClaimed ? "已领 +30" : "未领")}",
            runs.Count == 0 ? "- 当前没有进行中的探索" : $"- 进行中 {runs.Count} 场："
        ];
        lines.AddRange(runs.Select(run =>
            $"- #{run.Index} 【{run.RegionName}】{run.Characters}"
            + $"　{(run.Ready ? "✅ 可结算" : $"还需 {run.RemainingSeconds} 秒")}"
            + $"　消耗 {run.Cost}"));
        if (runs.Any(x => x.Ready)) lines.Add("- 有探索完成了：/探索结算");
        lines.Add("- 出发：/探索 地区编号 角色序号…　或 /小队探索 地区编号（用小队编成）");
        return CommandReplies.Compose("🧭 探索信息", $"许可 {quota.Left}", lines,
            ["/探索结算", "/世界地图", "/我的角色"]);
    }

    // ---- 世界地图 ----

    private static BotReply WorldMapAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<RegionSummary> regions = deps.Queries.GetRegions();
        if (regions.Count == 0)
        {
            return CommandReplies.Error("世界地图暂时无法显示", "没有列出任何地区",
                "内容包可能未加载，稍后重试", "/帮助");
        }
        List<string> lines = [.. regions.Select(region =>
            $"- {region.Id}. {region.Name}　{region.Category}　{region.Difficulty}　{region.Weather}"
            + (region.Materials.Count == 0 ? "" : $"\n  {string.Join("、", region.Materials)}"))];
        lines.Add("- /查地区 编号 看详情　·　/主城 看铎京城");
        return CommandReplies.ListPage("🗺️ 世界地图", $"{regions.Count} 个地区", lines, context.Page,
            "/世界地图", ["/查地区 1", "/主城"]);
    }

    private static BotReply RegionInfoAsync(CommandDependencies deps, CommandContext context)
    {
        string? raw = context.FirstArgument;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return WorldMapAsync(deps, context);      // 旧版「查地区」不带参数就是世界地图
        }
        string info = int.TryParse(raw, out int index)
            ? deps.Queries.GetRegionInfo(index)
            : deps.Queries.GetRegionInfo(raw);
        if (string.IsNullOrWhiteSpace(info))
        {
            return CommandReplies.Error($"找不到「{raw}」这个地区", "没有显示任何信息",
                "用 /世界地图 查看编号，或直接写地区名", "/世界地图");
        }
        string command = $"/查地区 {raw}";
        return info.Length > QQMarkdown.DefaultMaxLength - 300
            ? CommandReplies.Paginated($"🗺️ {raw}", info, context.Page, command)
            : CommandReplies.Detail($"🗺️ {raw}", info, actions: ["/世界地图", "/查地区 0"]);
    }

    private static BotReply HomeCityAsync(CommandDependencies deps, CommandContext context)
    {
        string info = deps.Queries.GetRegionInfo(0);
        return string.IsNullOrWhiteSpace(info)
            ? CommandReplies.Error("主城数据缺失", "没有显示任何信息", "内容包可能未加载", "/世界地图")
            : CommandReplies.Detail("🏙️ 铎京城", info, actions: ["/世界地图", "/状态"]);
    }

    private static async Task<BotReply> ActivitiesAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<ActivityEntry> rows = await deps.Activities.ListAsync(
            context.UserId, context.CancellationToken);
        if (rows.Count == 0)
        {
            return CommandReplies.Compose("🎪 活动中心", "暂时没有活动",
                ["- 等运营开新活动，或先去 /每日任务 攒点资源"], ["/每日任务", "/帮助"]);
        }
        List<string> lines = [.. rows.Select(row =>
            $"- @{row.Index} {row.Name}　{row.Phase}"
            + $"　任务 {row.DoneQuests}/{row.TotalQuests}"
            + (row.AllClaimed ? "（已全领）" : "")
            + $"\n  {row.Window}")];
        lines.Add("- /查活动 编号 看任务与奖励　·　/做活动任务 活动 任务 [@物品]　·　/领取奖励 活动 任务");
        return CommandReplies.Compose("🎪 活动中心", $"{rows.Count} 个活动", lines,
            ["/查活动 1", "/领取奖励 1 1", "/活动"]);
    }

    private static async Task<BotReply> ActivityInfoAsync(CommandDependencies deps, CommandContext context)
    {
        if (!context.TryInt(0, out int index) || index <= 0)
        {
            return CommandReplies.Error("缺少活动编号", "没有显示任何信息",
                "用 /活动 查看编号", "/活动");
        }
        ActivityDetail? detail = await deps.Activities.InfoAsync(
            context.UserId, index, context.CancellationToken);
        if (detail is null)
        {
            return CommandReplies.Error($"活动 @{index} 不存在", "没有显示任何信息",
                "用 /活动 查看当前活动", "/活动");
        }

        List<string> lines = [$"- 状态：{detail.Phase}　{detail.Window}", $"- {detail.Description}", "- 任务："];
        lines.AddRange(detail.Quests.Select(quest =>
        {
            string state = quest.Claimed ? "已领取"
                : quest.Ready ? "✅ 可领取"
                : $"{quest.Progress}/{quest.Target}";
            return $"  @{quest.Index} {quest.Name}　{state}\n"
                + $"    成本：{quest.Cost}　奖励：{quest.Reward}";
        }));
        lines.Add("- 做任务：/做活动任务 " + index + " 任务编号 [@物品]");
        lines.Add(detail.AnyReady ? "- 有任务可领奖了：/领取奖励 " + index + " 任务编号" : "- 做满进度后即可领奖");
        return CommandReplies.Compose($"🎪 {detail.Name}", detail.Phase, lines,
            [$"/做活动任务 {index} 1", $"/领取奖励 {index} 1", "/活动"]);
    }

    private static async Task<BotReply> MailInboxAsync(CommandDependencies deps, CommandContext context)
    {
        bool unreadOnly = context.HasFlag("未读") || context.HasFlag("unread");
        IReadOnlyList<MailEntry> mails = await deps.Mail.InboxAsync(
            context.UserId, unreadOnly, context.CancellationToken);
        if (mails.Count == 0)
        {
            return CommandReplies.Compose("📬 收件箱", unreadOnly ? "没有未读私信" : "收件箱是空的",
                ["- 用 /私信 昵称 内容 给别的旅人留言"], ["/私信", "/我的社团"]);
        }
        List<string> lines = [.. mails.Select(mail =>
            $"- @{mail.Index} {(mail.Read ? "　" : "🔵")}{mail.From}：{mail.Preview}　{mail.SentOn}")];
        lines.Add("- /读信 编号　·　/收件箱 未读 只看未读");
        int unread = mails.Count(x => !x.Read);
        return CommandReplies.ListPage("📬 收件箱",
            unreadOnly ? $"{mails.Count} 封未读" : $"共 {mails.Count} 封 · 未读 {unread}",
            lines, context.Page, "/收件箱", ["/收件箱 未读", "/私信"]);
    }

    private static async Task<BotReply> RenameReviewAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<RenameRequestEntry> rows = await deps.Clubs.PendingRenamesAsync(context.CancellationToken);
        if (rows.Count == 0)
        {
            return CommandReplies.Compose("🧾 改名审核", "没有待审申请",
                ["- 玩家用 /社团设置 名称 新名 提交后才会出现在这里"], ["/社团列表", "/帮助"]);
        }
        List<string> lines = [.. rows.Select(row =>
            $"- @{row.Index} {row.TargetName} → 「{row.RequestedName}」　申请者 {row.Requester}　{row.SubmittedOn}")];
        lines.Add("- /批准改名 编号 · /拒绝改名 编号");
        lines.Add("- 自动校验只拦长度、不可见字符、冒充官方、站外引流；人工复核不能省");
        return CommandReplies.Compose("🧾 改名审核", $"待审 {rows.Count} 条", lines,
            ["/批准改名 1", "/拒绝改名 1", "/社团列表"]);
    }

    private static async Task<BotReply> ClubTasksAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<ClubTaskEntry> tasks = await deps.Clubs.TasksAsync(context.UserId, context.CancellationToken);
        if (tasks.Count == 0)
        {
            return CommandReplies.Error("你当前没有加入任何社团", "没有任务可显示",
                "用 /社团列表 看看，或 /创建社团 自己开一个", "/社团列表");
        }
        List<string> lines = [.. tasks.Select(task =>
        {
            string state = task.Claimed ? "已结算"
                : task.Done ? "✅ 可结算"
                : $"{task.Progress}/{task.Target} {task.Unit}";
            return $"- @{task.Index} {task.Name}　{state}　奖励 {task.RewardFunds} 基金\n"
                + $"  {task.Hint}";
        })];
        lines.Add("- 提交物资：/社团任务 提交 @物品编号 [x数量]（按分解价一半折算基金）");
        lines.Add("- 结算：/社团任务 领取 编号（社长或管理）");
        lines.Add("- 奖励进社团基金，可在 /社团商店 兑换物资");
        return CommandReplies.Compose("🏁 社团任务", $"本周 · {GameplayRules.ClubWeek(DateTime.UtcNow)}",
            lines, ["/社团任务 领取 1", "/社团商店", "/我的社团"]);
    }

    private static async Task<BotReply> ClubStoreAsync(CommandDependencies deps, CommandContext context)
    {
        ClubStoreView view = await deps.Clubs.StoreAsync(context.UserId, context.CancellationToken);
        if (view.Goods.Count == 0)
        {
            return CommandReplies.Error("你当前没有加入任何社团", "没有货架可显示",
                "用 /社团列表 看看，或 /创建社团 自己开一个", "/社团列表");
        }
        List<string> lines = [.. view.Goods.Select(good =>
            $"- @{good.Index} {good.Name}　{good.Price} 基金　{good.Description}")];
        lines.Add($"- 可用基金：{view.Funds:0.##}（成员 /社团捐献 与社团市场手续费都会进来）");
        lines.Add("- 兑换：/社团商店 买 编号 [x数量]（社长或管理操作）");
        lines.Add("- 收购：/社团商店 卖 @物品编号 [x数量]（管理用基金按分解价一半收成员物资）");
        return CommandReplies.Compose("🛍️ 社团商店", $"社团基金 {view.Funds:0.##}", lines,
            ["/社团商店 买 1", "/社团捐献", "/我的社团"]);
    }

    private static async Task<BotReply> ClubMarketAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<ClubMarketEntry> listings = await deps.Clubs.MarketAsync(
            context.UserId, context.CancellationToken);
        if (listings.Count == 0)
        {
            return CommandReplies.Compose("🏪 社团市场", "暂无挂单",
                [
                    "- 上架：/社团市场 上架 @物品编号 价格",
                    "- 手续费 5% 进社团基金，比全局市场的 15% 低",
                    "- 只能在本社团成员之间交易"
                ], ["/背包", "/我的社团"]);
        }
        List<string> lines = [.. listings.Select(listing =>
            $"- @{listing.Index} {listing.Name}（{GameplayRules.QualityName(listing.Quality)}）"
            + $"　{listing.Price} 金币　卖家 {listing.Seller}{(listing.Mine ? "（你）" : "")}")];
        lines.Add("- /社团市场 买 编号　·　/社团市场 下架 编号　·　/社团市场 上架 @物品编号 价格");
        lines.Add("- 社长可用 /社团市场 清空 把全部挂单退回卖家");
        return CommandReplies.Compose("🏪 社团市场", $"{listings.Count} 件在售", lines,
            ["/社团市场 买 1", "/社团商店", "/我的社团"]);
    }

    private static async Task<BotReply> ClubListAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<ClubSummary> clubs = await deps.Clubs.ListAsync(context.CancellationToken);
        if (clubs.Count == 0)
        {
            return CommandReplies.Compose("🏛️ 社团列表", "还没有任何社团",
                ["- 用 /创建社团 <前缀> 开一个"], ["/创建社团 ABC", "/帮助"]);
        }
        List<string> lines = [.. clubs.Select(club =>
            $"- #{club.Id} {club.Prefix}·{club.Name}　{club.Members}/{club.MaxMembers} 人"
            + $"　社长 {club.MasterName}"
            + $"　{(club.IsPublic ? "公开" : "🔒仅邀请")}{(club.NeedApproval ? " · 需批准" : "")}"
            + $"　基金 {club.Funds:0.##}")];
        lines.Add("- /查看社团 编号　·　/加入社团 编号");
        return CommandReplies.ListPage("🏛️ 社团列表", $"共 {clubs.Count} 个", lines, context.Page,
            "/社团列表", ["/创建社团 ABC"]);
    }

    private static async Task<BotReply> ClubInfoAsync(CommandDependencies deps, CommandContext context)
    {
        if (!context.TryInt(0, out int clubId) || clubId <= 0)
        {
            return CommandReplies.Error("缺少社团编号", "没有显示任何信息",
                "用 /社团列表 查看编号", "/社团列表");
        }
        ClubDetail? detail = await deps.Clubs.InfoAsync(clubId, context.UserId, context.CancellationToken);
        if (detail is null)
        {
            return CommandReplies.Error($"不存在编号 {clubId} 的社团", "没有显示任何信息",
                "用 /社团列表 查看现有社团", "/社团列表");
        }
        return RenderClub(detail);
    }

    private static async Task<BotReply> ClubMineAsync(CommandDependencies deps, CommandContext context)
    {
        ClubDetail? detail = await deps.Clubs.MineAsync(context.UserId, context.CancellationToken);
        if (detail is null)
        {
            return CommandReplies.Error("你当前没有加入任何社团", "没有显示任何信息",
                "用 /社团列表 看看，或 /创建社团 自己开一个", "/社团列表");
        }
        return RenderClub(detail);
    }

    private static async Task<BotReply> ClubMembersAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<ClubMemberEntry> members = await deps.Clubs.MembersAsync(
            context.UserId, context.CancellationToken);
        if (members.Count == 0)
        {
            return CommandReplies.Error("你当前没有加入任何社团", "没有成员可显示",
                "用 /社团列表 看看，或 /创建社团 自己开一个", "/社团列表");
        }
        List<string> lines = [.. members.Select((member, i) =>
            $"- @{i + 1} {member.Display}　{RoleName(member.Role)}"
            + $"　入团 {member.JoinedOn}　捐献 {member.Contribution:0.##}")];
        lines.Add("- 社长/管理：/社团踢出 @编号 · /社团转让 @编号 · /社团设置 任命 @编号");
        return CommandReplies.ListPage("👥 社团成员", $"{members.Count}/{ClubService.MaxMembers} 人",
            lines, context.Page, "/查看社团成员", ["/我的社团", "/查看社团管理"]);
    }

    private static async Task<BotReply> ClubAdminsAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<ClubMemberEntry> members = await deps.Clubs.MembersAsync(
            context.UserId, context.CancellationToken);
        List<ClubMemberEntry> admins = [.. members.Where(x => x.Role >= ClubRole.Admin)];
        if (admins.Count == 0)
        {
            return CommandReplies.Error("你当前没有加入任何社团", "没有管理人员可显示",
                "用 /社团列表 看看，或 /创建社团 自己开一个", "/社团列表");
        }
        List<string> lines = [.. admins.Select(member => $"- {member.Display}　{RoleName(member.Role)}")];
        lines.Add("- 社长可 /社团设置 任命 @编号 提升管理，或 卸任 @编号");
        return CommandReplies.Compose("🎖️ 社团管理", $"{admins.Count} 人", lines,
            ["/查看社团成员", "/社团设置"]);
    }

    private static async Task<BotReply> ClubApplicationsAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<ClubApplicantEntry> rows = await deps.Clubs.ApplicationsAsync(
            context.UserId, context.CancellationToken);
        if (rows.Count == 0)
        {
            return CommandReplies.Compose("📋 申请人列表", "暂无申请",
                ["- 社员用 /加入社团 编号 提交申请后会出现在这里"], ["/我的社团", "/社团设置 需批准"]);
        }
        List<string> lines = [.. rows.Select(row =>
            $"- @{row.Index} {row.Display}　申请于 {row.AppliedOn}")];
        lines.Add("- /社团批准 @编号 · /社团拒绝 @编号");
        return CommandReplies.Compose("📋 申请人列表", $"共 {rows.Count} 人", lines,
            ["/社团批准 @1", "/社团拒绝 @1"]);
    }

    private static async Task<BotReply> ClubInvitesAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<ClubInviteEntry> rows = await deps.Clubs.InvitesAsync(
            context.UserId, context.CancellationToken);
        if (rows.Count == 0)
        {
            return CommandReplies.Compose("✉️ 受邀人列表", "暂无邀请",
                ["- 用 /邀请加入 昵称 发邀请"], ["/邀请加入", "/我的社团"]);
        }
        List<string> lines = [.. rows.Select(row => $"- @{row.Index} {row.Display}　邀请于 {row.InvitedOn}")];
        lines.Add("- 对方用 /加入社团 编号 即可加入（可绕过未公开与审批）");
        return CommandReplies.Compose("✉️ 受邀人列表", $"共 {rows.Count} 人", lines, ["/社团成员", "/我的社团"]);
    }

    private static BotReply RenderClub(ClubDetail detail)
    {
        List<string> lines =
        [
            $"- 编号：{detail.Id}　名称：{detail.Prefix}·{detail.Name}",
            $"- 社长：{detail.MasterName}" + (detail.IsMaster ? "（你）" : "") + $"　成员 {detail.Members}/{detail.MaxMembers}",
            $"- 加入规则：{(detail.IsPublic ? "公开" : "🔒 仅邀请")}{(detail.NeedApproval ? " · 需批准" : " · 免批准")}",
            $"- 社团基金：{detail.Funds:0.##}",
            detail.Notice.Length == 0 ? "- 公告：（空）" : $"- 公告：{detail.Notice}",
            $"- 成立于 {detail.CreatedOn}"
        ];
        List<string> actions = detail.IsMember
            ? ["/社团任务", "/社团商店", "/社团市场", "/查看社团成员", "/社团捐献", "/社团设置"]
            : [$"/加入社团 {detail.Id}", "/社团列表"];
        if (detail.IsMember) actions.Add("/退出社团");
        return CommandReplies.Compose("🏛️ 社团信息", $"{detail.Prefix}·{detail.Name}", lines, actions);
    }

    private static string RoleName(ClubRole role) => role switch
    {
        ClubRole.Master => "👑 社长",
        ClubRole.Admin => "🎖️ 管理",
        _ => "成员"
    };

    // ---- 房间指令辅助 ----

    /// <summary>房间按群隔离：群聊取群 OpenId，私聊返回空串（服务层会拒绝）。</summary>
    private static string Scope(CommandContext context) => context.Message.ChatScope();

    /// <summary>
    /// 从原始输入里认房间类型：`/创建赛马 密码` 这种旧写法没有类型参数，
    /// 类型藏在动词里；规范写法 `/创建房间 混战` 走参数解析。
    /// </summary>
    private static RoomKind? KindFromText(string raw)
    {
        foreach ((RoomKind kind, string _, int _, string[] aliases) in RoomRules.Kinds)
        {
            if (aliases.Any(alias => raw.Contains(alias, StringComparison.OrdinalIgnoreCase))) return kind;
        }
        return null;
    }

    private static BotReply RoomList(CommandDependencies deps, CommandContext context)
    {
        string scope = Scope(context);
        if (scope.Length == 0)
        {
            return CommandReplies.Error("请在群里使用", "没有列出任何房间",
                "多人房间只能在群聊里使用", "/帮助");
        }
        IReadOnlyList<RoomSummary> rooms = deps.Rooms.List(scope);
        if (rooms.Count == 0)
        {
            return CommandReplies.Compose("🏠 房间列表", "本群暂无房间",
                ["- 用 /创建房间 混战 开一个"], ["/创建房间 混战", "/帮助"]);
        }
        List<string> lines = [.. rooms.Select(room =>
            $"- [ {room.Code} ] {room.KindName}　{room.Members}/{room.MaxUsers} 人"
            + $"　房主 {room.MasterName}" + (room.HasPassword ? " 🔒" : "")
            + (room.Playing ? "　⚔️ 对局中" : ""))];
        lines.Add("- /加入房间 房号　·　/房间信息 房号");
        return CommandReplies.ListPage("🏠 房间列表", $"共 {rooms.Count} 个", lines, context.Page,
            "/房间列表", ["/创建房间 混战"]);
    }

    private static BotReply RoomInfo(CommandDependencies deps, CommandContext context)
    {
        string scope = Scope(context);
        if (scope.Length == 0)
        {
            return CommandReplies.Error("请在群里使用", "没有显示房间信息",
                "多人房间只能在群聊里使用", "/帮助");
        }
        RoomDetail? detail = deps.Rooms.Info(scope, Target(context), context.UserId);
        if (detail is null)
        {
            bool asked = !string.IsNullOrWhiteSpace(context.FirstArgument);
            return asked
                ? CommandReplies.Error("本群没有这个房间", "没有显示房间信息",
                    "用 /房间列表 查看当前有哪些房间", "/房间列表")
                : CommandReplies.Error("你当前不在任何房间中", "没有显示房间信息",
                    "用 /加入房间 房号 加入一个，或 /房间列表", "/房间列表");
        }
        List<string> lines =
        [
            $"- 房号：[ {detail.Code} ]　{detail.KindName}　{detail.Members.Count}/{detail.MaxUsers} 人",
            $"- 房主：{detail.MasterName}{(detail.IsMaster ? "（你）" : "")}"
            + (detail.HasPassword ? "　🔒 有密码" : ""),
            $"- 状态：{(detail.Playing ? "对局中" : "等待开局")}",
            "- 成员："
        ];
        lines.AddRange(detail.Members.Select(member =>
            $"  · {member.Display}{(member.UserId == detail.MasterId ? " 👑" : "")}"));
        lines.Add(detail.IsMaster || detail.KindName.StartsWith("赛马", StringComparison.Ordinal)
            ? "- 人都到齐了就 /开始游戏"
            : "- 等房主 /开始游戏");
        return CommandReplies.Compose($"🏠 {detail.KindName}", $"[ {detail.Code} ]", lines,
            ["/开始游戏", "/退出房间", "/房间列表"]);
    }

    // ---- P2 指令辅助 ----

    /// <summary>决斗类指令的对手参数：UID / Handle / 昵称，允许写成 @123。</summary>
    private static string? Target(CommandContext context)
    {
        string? raw = context.FirstArgument;
        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith('-')) return null;
        string value = raw.Trim().TrimStart('@').Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>带完整战报的回复：摘要走四段式，逐回合日志交给分页（一条消息装不下）。</summary>
    private static BotReply Report(BattleReport report, int page, string command)
    {
        if (!report.Result.Ok) return CommandReplies.FromResult(report.Result);

        List<string> lines = [.. report.Result.Lines];
        if (report.Log.Count > 0)
        {
            lines.Add("");
            lines.Add($"**逐回合战报（{report.Log.Count} 行）**");
            lines.AddRange(report.Log);
        }
        return CommandReplies.ListPage(report.Result.Title, report.Result.Status, lines, page, command,
            report.Result.Actions);
    }

    /// <summary>Boss 列表 / 单只详情。Boss 池是进程内的静态池，直接读，不需要开事务。</summary>
    private static BotReply BossInfo(CommandContext context)
    {
        if (context.TryInt(0, out int index) && index > 0)
        {
            BossEntry? entry = BossPool.Get(index);
            if (entry is null)
            {
                return CommandReplies.Error($"没有编号 {index} 的讨伐目标", "没有显示任何信息",
                    "用 /查询Boss 查看当前编号", "/查询Boss");
            }
            FunGame.Core.Entity.Character boss = entry.Character;
            string gear = string.Join("、", new[]
                {
                    boss.EquipSlot.Weapon?.Name, boss.EquipSlot.Armor?.Name, boss.EquipSlot.Shoes?.Name,
                    boss.EquipSlot.Accessory1?.Name, boss.EquipSlot.Accessory2?.Name,
                    boss.EquipSlot.MagicCardPack?.Name
                }.OfType<string>());
            return CommandReplies.Compose($"🐉 {entry.Display}", $"编号 {entry.Index} · 剩余 HP {entry.HPPercent:P0}",
                [
                    $"- 生命：{boss.HP:0.##}/{boss.MaxHP:0.##}　魔法：{boss.MP:0.##}/{boss.MaxMP:0.##}",
                    $"- 攻击：{boss.ATK:0.##}　防御：{boss.DEF:0.##}　速度：{boss.SPD:0.##}",
                    $"- 命中：{boss.HR:0.##}　闪避：{boss.EvadeRate:P0}　暴击：{boss.CritRate:P0}",
                    $"- 技能：{boss.Skills.Count} 个" + (boss.Skills.Count == 0 ? "" : $"（{string.Join("、", boss.Skills.Take(4).Select(x => x.Name))}）"),
                    $"- 装备：{(string.IsNullOrEmpty(gear) ? "无" : gear)}",
                    "- 全服共享：你打掉的 HP 会累计到这只目标上"
                ],
                [$"/小队讨伐Boss {entry.Index}", $"/Boss {entry.Index}", "/查询Boss"]);
        }

        IReadOnlyList<BossEntry> all = BossPool.All;
        if (all.Count == 0)
        {
            return CommandReplies.Compose("🐉 讨伐目标", "池已空",
                ["- 当前没有任何讨伐目标，等刷新后再来"], ["/Boss", "/状态"]);
        }
        List<string> lines = [.. all.Select(entry =>
            $"- {entry.Index}. {entry.Display}　剩余 HP {entry.HPPercent:P0}"
            + $"　（/Boss {entry.Index} 讨伐 · /小队讨伐Boss {entry.Index} 小队出战）")];
        lines.Add("- HP 百分比是全服共享的剩余量，越低的越容易收掉");
        return CommandReplies.ListPage("🐉 讨伐目标", $"共 {all.Count} 只", lines, context.Page, "/查询Boss",
            ["/Boss", "/状态"]);
    }


    /// <summary>把从 <paramref name="start"/> 开始的参数拼回一个字符串（技能名/角色名可能含空格）。</summary>
    private static string JoinFrom(CommandContext context, int start)
        => context.Args.Count <= start ? "" : string.Join(' ', context.Args.Skip(start));

    private static long Price(CommandContext context, int position = 1)
        => position < context.Args.Count && long.TryParse(context.Args[position], out long value)
            ? value : 0; // 0 会被仓储的价格范围校验拦下并给出三段式提示

    /// <summary>7 个固定商店共用的命令构造（显式工厂，不反射）。</summary>
    private static ICommand StoreCommand(
        string key, string name, string[] aliases, string storeKey, string description)
        => new DelegateCommand(
            key: key, name: name, aliases: aliases, usage: $"/{name} [p2]",
            description: description, group: CommandGroups.Economy,
            handler: context => ShopAsync(context, storeKey));

    private static async Task<BotReply> ShopAsync(CommandContext context, string? storeKey = null)
    {
        CommandDependencies deps = context.Dependencies;
        IReadOnlyList<StoreEntry> goods = await deps.Economy.GetStoreAsync(
            context.UserId, storeKey, context.CancellationToken);
        if (goods.Count == 0)
        {
            return CommandReplies.Detail("🏪 商店",
                "- 商店暂时没有商品\n- 可用：/商店（每日）、/商店1~4（常规）、/商店5~7（积分）",
                null, ["/商店查看"]);
        }

        List<string> lines = [.. goods.Select(good =>
        {
            string stock = good.Stock < 0 ? "无限" : good.Stock.ToString(CultureInfo.InvariantCulture);
            string quota = good.Quota > 0 ? $"　限购 {good.Bought}/{good.Quota}" : "";
            string price = good.Currency == "免费" ? "免费" : $"{good.Price} {good.Currency}";
            return $"- @{good.Index} {good.Name}　{price}　库存 {stock}{quota}";
        })];
        lines.Add("- /购买 @编号 [x数量]　·　作用于当前商店");
        string title = storeKey is null ? "🏪 每日商店（每 4:00 刷新）"
            : $"🏪 {StoreCatalog.Find(storeKey)?.Name ?? "商店"}";
        return CommandReplies.ListPage(title, $"{goods.Count} 种商品", lines,
            context.Page, storeKey is null ? "/商店" : $"/{StoreCatalog.Find(storeKey)?.Name}", ["/背包", "/状态"]);
    }

    private static async Task<BotReply> MarketAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<MarketEntry> listings = await deps.Economy.GetMarketAsync(
            context.UserId, context.CancellationToken);
        List<string> lines = listings.Select(listing =>
            $"- @{listing.Index} {GameplayRules.QualityName(listing.Quality)} · {listing.ItemName}"
            + $"　{listing.Price} 金币　卖家 {listing.SellerHandle}{(listing.IsMine ? "（我的）" : "")}").ToList();
        if (lines.Count == 0) lines.Add("- 市场暂时空着，用 /上架 @编号 <价格> 挂出第一件物品");
        lines.Add("- /上架 @编号 <价格> · /购买市场 @编号 · /下架 @编号");
        return CommandReplies.ListPage("🏷️ 旅人市场", $"{listings.Count} 件在售", lines,
            context.Page, "/市场", ["/背包", "/状态"]);
    }

    private static async Task<BotReply> OffersAsync(CommandDependencies deps, CommandContext context)
    {
        IReadOnlyList<OfferEntry> offers = await deps.Economy.GetOffersAsync(
            context.UserId, context.CancellationToken);
        List<string> lines = offers.Select(offer =>
            $"- @{offer.Index} {offer.ItemName}　{offer.Price} 金币"
            + $"　发起 {offer.SellerHandle}{(offer.IsMine ? "（我的）" : "")}").ToList();
        if (lines.Count == 0) lines.Add("- 暂无报价单，用 /报价 @编号 <价格> 发布");
        lines.Add("- /报价 @编号 <价格> · /接受报价 @编号 · /撤回报价 @编号");
        return CommandReplies.ListPage("📝 公开报价", $"{offers.Count} 份报价", lines,
            context.Page, "/报价列表", ["/市场", "/背包"]);
    }

    /// <param name="forcedBoard">旧版有独立的「赛马/共斗/锻造排行榜」指令，它们固定看对应积分榜。</param>
    private static async Task<BotReply> RankAsync(
        CommandDependencies deps, CommandContext context, string? forcedBoard = null)
    {
        string board = forcedBoard ?? context.FirstArgument ?? "credits";
        IReadOnlyList<RankEntry> ranks = await deps.Economy.GetRankingAsync(
            board, context.CancellationToken);
        List<string> lines = ranks.Select(rank =>
            $"- @{rank.Index} {rank.Handle}　{rank.Value:0.##}").ToList();
        if (lines.Count == 0)
        {
            lines.Add("- 榜单还没有数据：金币/等级靠日常养成，战绩靠 /决斗 与 /Boss，"
                + "赛马/共斗积分靠对应的房间玩法，锻造积分靠 /锻造");
        }
        return CommandReplies.ListPage("🏆 排行榜", GameplayRules.RankingBoardName(board), lines,
            context.Page, $"/排行榜 {board}", ["/决斗", "/Boss", "/状态"]);
    }
}
