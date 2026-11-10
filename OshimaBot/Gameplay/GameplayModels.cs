namespace OshimaBot.Gameplay;

/// <summary>玩法层与指令层之间的纯数据契约，玩法层不依赖 Messaging/Models。</summary>
/// <summary>玩家快照。等级/经验属于**角色**（Core 模型），这里只取主战角色的值用于展示。</summary>
public sealed record PlayerSnapshot(
    long UserId, double Credits, double Materials,
    int SignInStreak, string SignedOn, string? MainCharacter, int MainLevel, double MainExp,
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

/// <summary>P2 经济与排行视图。</summary>
public sealed record StoreEntry(int Index, long Id, string Name, long Price, int Stock);

public sealed record MarketEntry(
    int Index, long Id, string ItemName, int Quality, long Price, long Fee, string SellerHandle, bool IsMine);

public sealed record OfferEntry(
    int Index, long Id, string ItemName, long Price, string SellerHandle, bool IsMine);

public sealed record RankEntry(int Index, string Handle, double Value);
