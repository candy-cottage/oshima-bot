using OshimaBot.Gameplay;

namespace OshimaBot.Commands;

/// <summary>
/// 帮助分类：照抄旧版 <c>FunGameOrderList</c> 的 8 个帮助表（存档/角色/物品/战斗/玩法/社团/活动/商店）。
/// <para>旧版是手维护字典，新版改成「按 key 显式归类 + 注册表同源」——
/// 命令新增/改名只要进这张表就会出现帮助里，不必两处同步。分类用 switch 而非反射，与 CommandCatalog 一致。</para>
/// </summary>
public static class CommandCategories
{
    public const int HelpPageSize = 15;

    /// <summary>主菜单顺序与旧版一致：1 存档 2 角色 3 物品 4 战斗 5 玩法 6 社团 7 活动 8 商店。</summary>
    public static readonly IReadOnlyList<string> Order =
    [
        Archive, Character, Item, Battle, Play, Club, Activity, Shop
    ];

    public const string Archive = "存档帮助";
    public const string Character = "角色帮助";
    public const string Item = "物品帮助";
    public const string Battle = "战斗帮助";
    public const string Play = "玩法帮助";
    public const string Club = "社团帮助";
    public const string Activity = "活动帮助";
    public const string Shop = "商店帮助";

    /// <summary>某类帮助的简介，主菜单里给玩家一句说明。</summary>
    public static string Description(string category) => category switch
    {
        Archive => "建档、存档备份与还原",
        Character => "角色养成、装备、小队",
        Item => "背包、物品使用与分解",
        Battle => "战斗模拟、资料查询与排行",
        Play => "签到抽卡、探索、房间与私信",
        Club => "社团创建、成员与任务",
        Activity => "活动、任务板与赛马",
        Shop => "商店、市场、报价交易",
        _ => ""
    };

    /// <summary>
    /// 每条命令唯一归类。兜底进「玩法帮助」——新增命令没来得及归类时宁可出现在帮助里，
    /// 也不要变成玩家永远看不到的隐形指令。
    /// </summary>
    /// <summary>
    /// 分类帮助的页码：照抄旧版「在分类指令后加数字跳页」（存档帮助2），
    /// 同时兼容新版统一的 pN。裸数字只在 pN 缺省时生效，避免影响其它命令的参数语义。
    /// </summary>
    public static int HelpPage(CommandContext context)
    {
        if (context.PageRequested || context.Page > 1) return context.Page;
        if (context.FirstArgument is { } first && int.TryParse(first, out int page) && page > 0) return page;
        return context.Page;
    }

    public static string CategoryOf(ICommand command) => command.Key switch
    {
        // 存档
        "register" or "status" or "backup" or "snapshots" or "restore" or "db-backup"
            or         "bind" or "cancel" or "uptime" or "legacy-help"
            or "help-archive" or "help-character" or "help-item" or "help-club"
            or "help-activity" or "help-shop"
            or "rename-review" or "rename-approve" or "rename-reject" => Archive,

        // 角色
        "characters" or "set-main" or "levelup" or "break" or "train" or "train-claim"
            or "party" or "normal-attack-upgrade" or "normal-attack-upgrade-info"
            or "skill-upgrade" or "skill-upgrade-info" or "rename-character" or "rename-mine"
            or "reroll" or "reroll-confirm" or "reroll-cancel" or "look" or "fortune"
            or "notice" or "set-notice" => Character,

        // 物品
        "bag" or "bag-item" or "search" or "use" or "salvage" or "salvage-quality" or "exchange"
            or "use-magic-card" or "craft-magic-pack" or "lock" or "unlock" or "equip"
            or "unequip" or "magic-card" or "magic-card-pack" => Item,

        // 战斗 / 资料 / 排行
        "char" or "skill" or "item" or "stats" or "team-stats" or "log" or "log-full"
            or "sim" or "sim-mix" or "sim-map" or "sim-team" or "sim-team-map" or "sim-debug"
            or "duel" or "full-duel" or "team-duel" or "boss" or "boss-info" or "boss-team"
            or "instance" or "instance-claim" or "rank" or "rank-win" or "rank-team-win"
            or "rank-rating" or "rank-team-rating" or "battle-help" => Battle,

        // 玩法（签到抽卡 / 探索 / 房间 / 私信）
        "signin" or "quests" or "quest-claim" or "gacha" or "gacha-ten"
            or "gacha-material" or "gacha-ten-material" or "explore" or "explore-info"
            or "explore-claim" or "world-map" or "region-info" or "home-city"
            or "room-create" or "room-join" or "room-quit" or "room-start" or "room-list"
            or "room-info" or "mail-send" or "mail-inbox" or "mail-read" => Play,

        // 社团
        "club-create" or "club-list" or "club-info" or "club-mine" or "club-join"
            or "club-quit" or "club-disband" or "club-members" or "club-admins"
            or "club-applications" or "club-invites" or "club-approve" or "club-reject"
            or "club-invite" or "club-kick" or "club-transfer" or "club-settings"
            or "club-donate" or "club-tasks" or "club-store" or "club-market" => Club,

        // 活动 / 任务板 / 赛马
        "activities" or "activity-info" or "activity-do" or "activity-claim"
            or "quest-board" or "quest-do" or "quest-info" or "quest-settle"
            or "horse-join" or "rank-horse" or "rank-coop" or "help" => Activity,

        // 商店 / 市场 / 报价 / 锻造 / 发放
        "shop" or "shop-detail" or "store-sell" or "store-add" or "buy" or "market"
            or "list-item" or "delist" or "market-buy" or "my-purchases"
            or "offers" or "offer" or "offer-accept" or "offer-create" or "offer-add"
            or "offer-add-want" or "offer-remove" or "offer-remove-want" or "offer-send"
            or "offer-reject" or "offer-info" or "offer-cancel" or "forge" or "forge-claim"
            or "rank-forge" or "grant-item" or "grant-currency" or "grant-magic"
            or "grant-log" or "revoke-grant" => Shop,

        // 兜底：新命令自动落到玩法帮助，不会在帮助里消失。
        _ => Play
    };
}
