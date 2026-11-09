namespace OshimaBot.Gameplay;

/// <summary>玩法层与指令层之间的纯数据契约，玩法层不依赖 Messaging/Models。</summary>
public sealed record PlayerSnapshot(
    long UserId, int Level, long Exp, long Credits, long Diamonds,
    int SignInStreak, string SignedOn, string? MainCharacter,
    int CharacterCount, int ItemCount, int SquadCount, int PendingTrainings);

public sealed record BagEntry(
    int Index, long Id, string Name, string CatalogId, int Quantity,
    int Quality, bool Locked, bool Bound, bool Equipped);

public sealed record CharacterEntry(
    int Index, long Id, string Name, int Level, long Exp,
    int BreakLevel, bool IsMain, int EquipmentCount);

public sealed record QuestEntry(int Index, string Key, string Name, int Status, int Progress, int Target);

/// <summary>
/// 统一的玩法结果。Ok=true 时按 4 段式渲染（Title / Status / Lines / Actions）；
/// Ok=false 时按错误三段式渲染（发生了什么 / 没有发生什么 / 下一步）。
/// </summary>
public sealed record ActionResult(
    bool Ok, string Title, string Status, IReadOnlyList<string> Lines, IReadOnlyList<string> Actions)
{
    public static ActionResult Success(
        string title, string status,
        IEnumerable<string>? lines = null, IEnumerable<string>? actions = null)
        => new(true, title, status,
            lines?.ToArray() ?? [], actions?.ToArray() ?? ["/状态"]);

    public static ActionResult Fail(string what, string notHappened, string next, string? action = null)
        => new(false, what, notHappened, [next], action is null ? ["/帮助"] : [action]);
}
