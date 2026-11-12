namespace OshimaBot.Gameplay;

/// <summary>房间玩法类型（对齐旧版四种房间：赛马 / 共斗 / 混战 / 团队死斗）。</summary>
public enum RoomKind
{
    HorseRacing,
    Cooperative,
    Mix,
    Team
}

/// <summary>
/// 房间规则常量，全部对齐旧版 <c>OnlineService.CreateRoom</c>：
/// 7 位房号、按群隔离（<c>GameMap = groupId</c>）、空闲 10 分钟自动解散、每种房间有人数上限。
/// </summary>
public static class RoomRules
{
    /// <summary>旧版 <c>FunGameConstant.RoomExpireTime</c>。</summary>
    public const int ExpireMinutes = 10;

    /// <summary>旧版「房间人数不足」（<c>UserAndIsReady.Count &lt; 2</c>）。</summary>
    public const int MinMembers = 2;

    public const int MaxPasswordLength = 16;

    public static readonly IReadOnlyList<(RoomKind Kind, string Name, int MaxUsers, string[] Aliases)> Kinds =
    [
        (RoomKind.HorseRacing, "赛马房间", 8, ["赛马", "horseracing", "horse"]),
        (RoomKind.Cooperative, "共斗房间", 4, ["共斗", "cooperative", "coop"]),
        (RoomKind.Mix, "混战房间", 10, ["混战", "mix"]),
        (RoomKind.Team, "团队死斗房间", 8, ["团战", "团队", "team"])
    ];

    public static string Name(RoomKind kind) => Kinds.First(x => x.Kind == kind).Name;

    public static int MaxUsers(RoomKind kind) => Kinds.First(x => x.Kind == kind).MaxUsers;

    /// <summary>
    /// 玩法是否已经接入：四种房间都已能开局（混战/团战走战斗队列、赛马是独立小游戏、共斗是组队讨伐）。
    /// 保留这个开关，后续新增房型（如赛事竞猜）可以先把房间建出来再接玩法。
    /// </summary>
    public static bool IsPlayable(RoomKind kind) => true;

    /// <summary>房号字符集：去掉容易看错的 0/O/1/I。</summary>
    public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int CodeLength = 7;

    /// <summary>解析房间类型：中文名或英文别名，大小写不敏感。</summary>
    public static RoomKind? ParseKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string value = text.Trim();
        foreach ((RoomKind kind, string _, int _, string[] aliases) in Kinds)
        {
            foreach (string alias in aliases)
            {
                if (string.Equals(alias, value, StringComparison.OrdinalIgnoreCase)) return kind;
            }
        }
        return null;
    }

    /// <summary>
    /// 共斗奖励（旧版 <c>Cooperative</c>）：全灭敌人给 <paramref name="coopLevel"/>；
    /// 没打完则按「击杀数 / 敌人数」折算（整数除法，可能为 0）。
    /// </summary>
    public static int CoopReward(int coopLevel, int enemyCount, int killed)
    {
        int level = Math.Max(0, coopLevel);
        if (enemyCount <= 0) return 0;
        int safeKilled = Math.Clamp(killed, 0, enemyCount);
        if (safeKilled >= enemyCount) return level;
        return level / enemyCount * safeKilled;
    }

    public static string KindHint()
        => string.Join(" / ", Kinds.Select(x => x.Aliases[0]));
}
