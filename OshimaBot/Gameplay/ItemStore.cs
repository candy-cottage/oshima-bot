using System.Globalization;
using System.Text.Json;
using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Game.Services;

namespace OshimaBot.Gameplay;

/// <summary>
/// 玩法层物品：Core <see cref="Item"/> 快照 + 持久化记录。
/// <para><see cref="Id"/> 是 <see cref="CoreItemRecord.LocalId"/>（整型，供应关联引用）；
/// <see cref="Quality"/> 在玩法里仍是 1–7 的档位，落库时换算成 <c>QualityType</c>。</para>
/// </summary>
public sealed class PlayerItem(CoreItemRecord record, Item item)
{
    public CoreItemRecord Record { get; } = record;

    public Item Item { get; } = item;

    public long Id => Record.LocalId;

    public long UserId
    {
        get => Record.UserId;
        set => Record.UserId = value;
    }

    public string CatalogId => Record.CatalogId;

    public string Name => Item.Name;

    public int Quantity
    {
        get => Record.Quantity;
        set => Record.Quantity = Math.Max(1, value);
    }

    /// <summary>玩法品质档位（1 普通 … 5 传说，6 神话，7 不朽）。</summary>
    public int Quality
    {
        get => GameplayRules.QualityTierOf(Item.QualityType);
        set => Item.QualityType = GameplayRules.QualityTypeOf(value);
    }

    public bool Locked
    {
        get => Record.IsLock;
        set
        {
            Record.IsLock = value;
            Item.IsLock = value;
        }
    }

    public bool Bound
    {
        get => Record.IsBound;
        set => Record.IsBound = value;
    }

    public DateTime AcquiredAtUtc => Record.UpdatedAtUtc;
}

/// <summary>
/// 物品的 Core 快照读写。与 <see cref="CharacterStore"/> 一样直接作用于传入的 <see cref="BotDbContext"/>，
/// 以便和玩法事务共用同一个上下文。
/// </summary>
public static class ItemStore
{
    private static JsonSerializerOptions Options => JsonTool.JsonSerializerOptions;

    public static async Task<List<PlayerItem>> ListAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        List<CoreItemRecord> rows = await db.CoreItems
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.LocalId)
            .ToListAsync(ct);

        List<PlayerItem> result = [];
        foreach (CoreItemRecord row in rows)
        {
            Item? item = Restore(row);
            if (item is not null) result.Add(new PlayerItem(row, item));
        }
        return result;
    }

    /// <summary>按背包列表序号取物品（1 基，顺序与 <c>/背包</c> 一致）。</summary>
    public static async Task<PlayerItem?> ByIndexAsync(
        BotDbContext db, long userId, int index, CancellationToken ct)
    {
        if (index <= 0) return null;
        List<PlayerItem> list = await ListAsync(db, userId, ct);
        return index > list.Count ? null : list[index - 1];
    }

    /// <summary>按本地键批量取（市场/报价列表用），返回 LocalId → 物品。</summary>
    public static async Task<Dictionary<long, PlayerItem>> ByIdsAsync(
        BotDbContext db, IReadOnlyCollection<long> localIds, CancellationToken ct)
    {
        if (localIds.Count == 0) return [];
        List<CoreItemRecord> rows = await db.CoreItems
            .Where(x => localIds.Contains(x.LocalId))
            .ToListAsync(ct);

        Dictionary<long, PlayerItem> result = [];
        foreach (CoreItemRecord row in rows)
        {
            Item? item = Restore(row);
            if (item is not null) result[row.LocalId] = new PlayerItem(row, item);
        }
        return result;
    }

    public static async Task<PlayerItem?> ByIdAsync(BotDbContext db, long localId, CancellationToken ct)
    {
        CoreItemRecord? row = await db.CoreItems.FirstOrDefaultAsync(x => x.LocalId == localId, ct);
        if (row is null) return null;
        Item? item = Restore(row);
        return item is null ? null : new PlayerItem(row, item);
    }

    /// <summary>发放物品：优先并入同目录、同品质、同名的未绑未锁堆叠。</summary>
    public static async Task<PlayerItem> AddAsync(
        BotDbContext db, long userId, string name, int quality,
        int quantity, CancellationToken ct, string catalogId = "gacha")
    {
        QualityType type = GameplayRules.QualityTypeOf(quality);
        List<CoreItemRecord> rows = await db.CoreItems
            .Where(x => x.UserId == userId && x.CatalogId == catalogId
                        && x.Quality == (int)type && !x.IsLock && !x.IsBound)
            .ToListAsync(ct);

        foreach (CoreItemRecord row in rows)
        {
            Item? existing = Restore(row);
            if (existing is null || existing.Name != name) continue;
            row.Quantity += quantity;
            row.UpdatedAtUtc = DateTime.UtcNow;
            return new PlayerItem(row, existing);
        }

        Item item = CreateItem(catalogId, name, type);
        AssignPriceIfMissing(item);
        CoreItemRecord record = new()
        {
            Guid = item.Guid.ToString(),
            UserId = userId,
            CatalogId = catalogId,
            Quality = (int)type,
            Price = item.Price,
            IsLock = false,
            IsTradable = item.IsTradable,
            ItemType = (int)item.ItemType,
            EquipSlotType = (int)item.EquipSlotType,
            Quantity = Math.Max(1, quantity),
            UpdatedAtUtc = DateTime.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(item, Options)
        };
        db.CoreItems.Add(record);
        return new PlayerItem(record, item);
    }

    /// <summary>
    /// 按**现成的 Core 快照**入库（魔法卡/魔法卡包这类运行时生成的物品）。
    /// <para>与 <see cref="AddAsync"/> 的区别：不并堆叠、不按模板重建——技能、次数、描述都要原样保留，
    /// 所以每张魔法卡都是独立一行。</para>
    /// </summary>
    public static PlayerItem AddSnapshot(
        BotDbContext db, long userId, Item item, string catalogId, int quantity = 1)
    {
        AssignPriceIfMissing(item);
        CoreItemRecord record = new()
        {
            Guid = item.Guid.ToString(),
            UserId = userId,
            CatalogId = catalogId,
            Quality = (int)item.QualityType,
            Price = item.Price,
            IsLock = item.IsLock,
            IsTradable = item.IsTradable,
            ItemType = (int)item.ItemType,
            EquipSlotType = (int)item.EquipSlotType,
            Quantity = Math.Max(1, quantity),
            UpdatedAtUtc = DateTime.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(item, Options)
        };
        db.CoreItems.Add(record);
        return new PlayerItem(record, item);
    }

    /// <summary>
    /// 入包时给没有回收价的物品补一个（旧版 <c>AddItemToUserInventory</c> 总会写 Price）。
    /// <para>内容包多数物品没设 <c>Price</c>，不补的话「商店出售」永远卖不出去。</para>
    /// </summary>
    private static void AssignPriceIfMissing(Item item)
    {
        if (item.Price > 0) return;
        item.Price = GameplayRules.DefaultItemPrice(item.QualityType);
    }

    /// <summary>按名字统计持有数量（跨堆叠），突破材料校验用。</summary>
    public static async Task<Dictionary<string, int>> CountByNamesAsync(
        BotDbContext db, long userId, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        Dictionary<string, int> counts = [];
        if (names.Count == 0) return counts;

        HashSet<string> wanted = [.. names];
        List<PlayerItem> items = await ListAsync(db, userId, ct);
        foreach (PlayerItem item in items)
        {
            if (!wanted.Contains(item.Name)) continue;
            counts[item.Name] = counts.GetValueOrDefault(item.Name) + item.Quantity;
        }
        return counts;
    }

    /// <summary>
    /// 按名字扣除数量（跨堆叠，后加入的先扣，与旧版 <c>TakeLast</c> 一致）。
    /// **先整体校验再扣**，不足时一件不动并返回 false。
    /// </summary>
    public static async Task<bool> ConsumeByNamesAsync(
        BotDbContext db, long userId, IReadOnlyDictionary<string, int> needs, CancellationToken ct)
    {
        if (needs.Count == 0) return true;

        List<PlayerItem> items = await ListAsync(db, userId, ct);
        foreach ((string name, int need) in needs)
        {
            if (need <= 0) continue;
            int have = items.Where(x => x.Name == name).Sum(x => x.Quantity);
            if (have < need) return false;
        }

        foreach ((string name, int need) in needs)
        {
            int remaining = need;
            if (remaining <= 0) continue;
            foreach (PlayerItem item in items
                .Where(x => x.Name == name)
                .OrderByDescending(x => x.AcquiredAtUtc).ThenByDescending(x => x.Id))
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, item.Quantity);
                if (item.Quantity <= take) Remove(db, item);   // Quantity setter 会把 0 抬成 1，必须整行移除
                else item.Quantity -= take;
                remaining -= take;
            }
        }
        return true;
    }

    /// <summary>把物品（含品质/锁定/绑定）写回记录与快照；不调用 SaveChanges。</summary>
    public static void Sync(PlayerItem item)
    {
        CoreItemRecord record = item.Record;
        record.UserId = item.UserId;
        record.Quality = (int)item.Item.QualityType;
        record.Price = item.Item.Price;
        record.IsLock = item.Item.IsLock;
        record.IsTradable = item.Item.IsTradable;
        record.ItemType = (int)item.Item.ItemType;
        record.EquipSlotType = (int)item.Item.EquipSlotType;
        record.Quantity = Math.Max(1, record.Quantity);
        record.SnapshotJson = JsonSerializer.Serialize(item.Item, Options);
        record.UpdatedAtUtc = DateTime.UtcNow;
    }

    public static void Remove(BotDbContext db, PlayerItem item) => db.CoreItems.Remove(item.Record);

    /// <summary>按目录 Id 找内容包模板并复制；找不到就造一个空壳（用于秘境掉落这类动态物品）。</summary>
    private static Item CreateItem(string catalogId, string name, QualityType quality)
    {
        Item? template = FunGameService.AllItems
            .FirstOrDefault(i => i.Id.ToString(CultureInfo.InvariantCulture) == catalogId);

        Item item = template?.Copy() ?? new Item();
        item.Guid = Guid.NewGuid();          // Copy 会复制 Guid
        if (!string.IsNullOrWhiteSpace(name)) item.Name = name;
        item.QualityType = quality;
        item.IsLock = false;
        return item;
    }

    private static Item? Restore(CoreItemRecord row)
    {
        try
        {
            return JsonSerializer.Deserialize<Item>(row.SnapshotJson, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
