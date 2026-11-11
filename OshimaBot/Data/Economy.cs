namespace OshimaBot.Data.Entities;

/// <summary>所有货币变动必须与「加物品/扣物品」在同一事务写入，用于对账与防丢。</summary>
public sealed class LedgerEntry
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Currency { get; set; } = "credits";

    /// <summary>变动量。与 Core 的 <c>double</c> 货币一致，不再截断为整数。</summary>
    public double Delta { get; set; }

    /// <summary>变动后余额，可与 <c>CorePlayers</c> 逐笔对账。</summary>
    public double BalanceAfter { get; set; }
    public string Reason { get; set; } = "";
    public string RefType { get; set; } = "";
    public string RefId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Store
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "system";
    public DateTime? RefreshAtUtc { get; set; }
    public string ConfigJson { get; set; } = "{}";
}

public sealed class StoreGood
{
    public long Id { get; set; }
    public string StoreKey { get; set; } = "";

    /// <summary>货架序号（每日商店按 1–4 固定，常规商店按目录顺序）。</summary>
    public int Slot { get; set; }

    /// <summary>每日商店归属的用户；常规/积分商店为 0（全局共享一份）。</summary>
    public long OwnerUserId { get; set; }

    /// <summary>展示名（旧版 <c>Goods.Name</c>；物品名与商品名可能不同）。</summary>
    public string Name { get; set; } = "";

    public string ItemCatalogId { get; set; } = "";

    /// <summary>credits / diamonds / forge_points / horse_points / coop_points / free。</summary>
    public string PriceCurrency { get; set; } = "credits";

    public long Price { get; set; }

    /// <summary>初始库存；-1 表示无限。</summary>
    public int Stock { get; set; } = -1;

    /// <summary>限购数量（每人）；0 表示不限购。</summary>
    public int Quota { get; set; }

    /// <summary>已售出数量（叠加在 <see cref="Stock"/> 上算剩余）。</summary>
    public int SoldCount { get; set; }

    /// <summary>每个用户的已购数量，JSON：{ "userId": count }。</summary>
    public string UsersBuyCountJson { get; set; } = "{}";

    public DateTime? RefreshAtUtc { get; set; }
}

public sealed class MarketListing
{
    public long Id { get; set; }
    public long SellerUserId { get; set; }
    public long ItemInstanceId { get; set; }
    public long Price { get; set; }
    public long Fee { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
}

public sealed class Trade
{
    public long Id { get; set; }
    public long? BuyerUserId { get; set; }
    public long SellerUserId { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
}

public sealed class TradeItem
{
    public long Id { get; set; }
    public long TradeId { get; set; }
    public long FromUserId { get; set; }
    public long ItemInstanceId { get; set; }
    public int Quantity { get; set; } = 1;
    public long Price { get; set; }
}
