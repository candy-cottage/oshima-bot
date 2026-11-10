namespace OshimaBot.Data.Core;

/// <summary>
/// Core 语义的玩家货币与主战引用。
/// <para>只保存 <c>Inventory.Credits</c> / <c>Inventory.Materials</c>（都是 <see cref="double"/>）
/// 与主战角色 Guid；账号信息仍在 <c>users</c> 表，避免重复存储 <c>User</c> 对象图。</para>
/// <para>注意：Oshima 内容包把第二资源（Materials）显示为「钻石」，
/// 显示名一律取 <c>GameplayEquilibriumConstant.InGameMaterial</c>，不得硬编码。</para>
/// </summary>
public sealed class CorePlayerRecord
{
    public long UserId { get; set; }

    /// <summary>对应 <c>Inventory.Credits</c>（金币）。</summary>
    public double Credits { get; set; }

    /// <summary>对应 <c>Inventory.Materials</c>（第二资源，Oshima 显示为「钻石」）。</summary>
    public double Materials { get; set; }

    /// <summary>对应 <c>Inventory.MainCharacter.Guid</c>。</summary>
    public string? MainCharacterGuid { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>
/// Core <c>Character</c> 的持久化记录：索引列 + 完整实体快照。
/// <para>索引列只服务于查询、排序与排行；权威数据是 <see cref="SnapshotJson"/>，
/// 由 Core 自带的 <c>CharacterConverter</c> 序列化，避免字段级映射失真。</para>
/// </summary>
public sealed class CoreCharacterRecord
{
    /// <summary><c>Character.Guid</c>，业务主键。</summary>
    public string Guid { get; set; } = "";

    public long UserId { get; set; }

    /// <summary>内容包引用（角色模板），用于内容升级后重建。</summary>
    public string CatalogId { get; set; } = "";

    public int Level { get; set; }
    public double Exp { get; set; }
    public int ExLevel { get; set; }
    public int MaxLevel { get; set; }

    /// <summary>突破进度，对应 <c>EquilibriumConstant.LevelBreakList</c> 索引。</summary>
    public int LevelBreak { get; set; }

    /// <summary>评级分，Core 用 <c>Character.RoleRating</c> 换算。</summary>
    public int Promotion { get; set; }

    public string SnapshotJson { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}

/// <summary>
/// Core <c>Item</c> 的持久化记录：索引列 + 完整实体快照。
/// <para><see cref="Quality"/> 直接存 <c>QualityType</c> 的枚举值（0–6），
/// 不用 1–5 的自造映射；<see cref="Quantity"/> 是堆叠数（Core 的 Item 是单件实例）。</para>
/// </summary>
public sealed class CoreItemRecord
{
    /// <summary><c>Item.Guid</c>，业务主键。</summary>
    public string Guid { get; set; } = "";

    public long UserId { get; set; }

    /// <summary>托管归属；-1 表示市场/报价托管中，不出现在任何玩家背包。</summary>
    public long EscrowUserId { get; set; }

    public string CatalogId { get; set; } = "";

    /// <summary><c>QualityType</c>：0=White … 6=Gold。</summary>
    public int Quality { get; set; }

    public double Price { get; set; }
    public bool IsLock { get; set; }
    public bool IsTradable { get; set; }

    /// <summary><c>EquipSlotType</c>：0=None，其余对应 6 槽之一。</summary>
    public int EquipSlotType { get; set; }

    /// <summary>堆叠数量；Core 的 Item 本身是单件，堆叠是持久层的聚合策略。</summary>
    public int Quantity { get; set; } = 1;

    public string SnapshotJson { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}
