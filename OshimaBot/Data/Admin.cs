namespace OshimaBot.Data.Entities;

/// <summary>
/// 管理员发放记录：**撤销与对账的唯一凭据**（2026-11-15 补）。
/// <para>为什么需要一张表而不是只靠 <c>ledger</c>：货币发放有流水，但**物品与魔法卡的发放此前没有任何留痕**，
/// 只有命令层的审计行（谁敲了命令，看不出给了谁多少）。发错了要撤回时，既不知道自己发过什么，
/// 也无法确认「撤的是不是那一笔」。这张表把「谁 / 给谁 / 什么 / 多少 / 何时」记下来，
/// 撤销时按编号定位，撤完打标记（不删行，保留审计链）。</para>
/// </summary>
public sealed class AdminGrant
{
    public long Id { get; set; }

    /// <summary>执行发放的管理员。</summary>
    public long AdminId { get; set; }

    /// <summary>收到东西的玩家。</summary>
    public long TargetUserId { get; set; }

    /// <summary>发放类型：<see cref="GrantKinds"/>。</summary>
    public string Kind { get; set; } = "";

    /// <summary>物品名或魔法卡名；货币为空。</summary>
    public string Name { get; set; } = "";

    /// <summary>件数（物品与魔法卡）；货币为 0。</summary>
    public int Quantity { get; set; }

    /// <summary>金额（货币）；物品为 0。</summary>
    public double Amount { get; set; }

    /// <summary>内容包里的物品 Id，用于对账时确认发的是哪一件（同名不同 Id 的情况）。</summary>
    public string CatalogId { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>撤销时间；为空表示还没撤销过。</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>撤销人。与 <see cref="AdminId"/> 分开记：别人也可以来收尾。</summary>
    public long? RevokedBy { get; set; }

    /// <summary>撤销时的实际结果说明（撤了多少、差多少）。</summary>
    public string RevokeNote { get; set; } = "";
}

/// <summary>发放类型常量。与 <c>ledger.currency</c> 的取值保持一致，方便对账。</summary>
public static class GrantKinds
{
    public const string Item = "item";
    public const string Magic = "magic";
    public const string Credits = "credits";
    public const string Diamonds = "diamonds";

    public static string Display(string kind) => kind switch
    {
        Item => "物品",
        Magic => "魔法卡",
        Credits => "金币",
        Diamonds => "钻石",
        _ => "东西"
    };

    public static bool IsCurrency(string kind) => kind is Credits or Diamonds;
}
