namespace OshimaBot.Data.Entities;

/// <summary>所有货币变动必须与「加物品/扣物品」在同一事务写入，用于对账与防丢。</summary>
public sealed class LedgerEntry
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Currency { get; set; } = "credits";
    public long Delta { get; set; }
    public long BalanceAfter { get; set; }
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
    public string ItemCatalogId { get; set; } = "";
    public string PriceCurrency { get; set; } = "credits";
    public long Price { get; set; }
    public int Stock { get; set; } = -1;
    public int Quota { get; set; } = -1;
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
