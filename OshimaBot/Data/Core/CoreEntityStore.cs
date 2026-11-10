using System.Text.Json;
using FunGame.Core.Api;
using FunGame.Core.Entity;
using Microsoft.EntityFrameworkCore;
using CoreCharacter = FunGame.Core.Entity.Character;

namespace OshimaBot.Data.Core;

/// <summary>
/// Core 实体的持久化入口。
/// <para>原则：<b>数值与结构以 Core 实体为准</b>，持久层只做「索引列 + 实体快照」，
/// 不再把货币/品质/装备槽映射成自定义标量。序列化复用 Core 的
/// <see cref="JsonTool.JsonSerializerOptions"/>（已注册 Character/Item/EquipSlot 等转换器）。</para>
/// </summary>
public sealed class CoreEntityStore(IServiceScopeFactory scopeFactory)
{
    private static JsonSerializerOptions Options => JsonTool.JsonSerializerOptions;

    // ---------- 玩家 ----------

    public async Task<CorePlayerRecord?> GetPlayerAsync(long userId, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return await db.CorePlayers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }

    public async Task SavePlayerAsync(
        long userId, double credits, double materials, string? mainCharacterGuid,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord? row = await db.CorePlayers.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (row is null)
        {
            row = new CorePlayerRecord { UserId = userId };
            db.CorePlayers.Add(row);
        }
        row.Credits = credits;
        row.Materials = materials;
        row.MainCharacterGuid = mainCharacterGuid;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---------- 角色 ----------

    public async Task SaveCharacterAsync(
        long userId, string catalogId, CoreCharacter character, CancellationToken cancellationToken = default)
    {
        string guid = character.Guid.ToString();
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CoreCharacterRecord? row = await db.CoreCharacters.FirstOrDefaultAsync(x => x.Guid == guid, cancellationToken);
        if (row is null)
        {
            row = new CoreCharacterRecord { Guid = guid };
            db.CoreCharacters.Add(row);
        }
        row.UserId = userId;
        row.CatalogId = catalogId;
        row.Level = character.Level;
        row.Exp = character.EXP;
        row.ExLevel = character.ExLevel;
        row.MaxLevel = character.MaxLevel;
        row.LevelBreak = character.LevelBreak;
        row.Promotion = character.Promotion;
        row.SnapshotJson = JsonSerializer.Serialize(character, Options);
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CoreCharacter?> GetCharacterAsync(string guid, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CoreCharacterRecord? row = await db.CoreCharacters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Guid == guid, cancellationToken);
        return row is null ? null : Restore(row);
    }

    public async Task<IReadOnlyList<CoreCharacter>> GetCharactersAsync(
        long userId, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        List<CoreCharacterRecord> rows = await db.CoreCharacters.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Level)
            .ThenBy(x => x.Guid)
            .ToListAsync(cancellationToken);
        List<CoreCharacter> result = [];
        foreach (CoreCharacterRecord row in rows)
        {
            CoreCharacter? character = Restore(row);
            if (character is not null) result.Add(character);
        }
        return result;
    }

    private static CoreCharacter? Restore(CoreCharacterRecord row)
    {
        CoreCharacter? character;
        try
        {
            character = JsonSerializer.Deserialize<CoreCharacter>(row.SnapshotJson, Options);
        }
        catch (JsonException)
        {
            return null;
        }
        if (character is null) return null;

        // 反序列化不会带出 User 关联，这里补一个最小引用，避免业务层拿到 null。
        character.User = new User(row.UserId);
        return character;
    }

    // ---------- 物品 ----------

    public async Task SaveItemAsync(
        long userId, string catalogId, Item item, int quantity = 1, long escrowUserId = 0,
        CancellationToken cancellationToken = default)
    {
        string guid = item.Guid.ToString();
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CoreItemRecord? row = await db.CoreItems.FirstOrDefaultAsync(x => x.Guid == guid, cancellationToken);
        if (row is null)
        {
            row = new CoreItemRecord { Guid = guid };
            db.CoreItems.Add(row);
        }
        row.UserId = userId;
        row.EscrowUserId = escrowUserId;
        row.CatalogId = catalogId;
        row.Quality = (int)item.QualityType;
        row.Price = item.Price;
        row.IsLock = item.IsLock;
        row.IsTradable = item.IsTradable;
        row.EquipSlotType = (int)item.EquipSlotType;
        row.Quantity = Math.Max(1, quantity);
        row.SnapshotJson = JsonSerializer.Serialize(item, Options);
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Item?> GetItemAsync(string guid, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CoreItemRecord? row = await db.CoreItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Guid == guid, cancellationToken);
        return row is null ? null : RestoreItem(row);
    }

    public async Task<IReadOnlyList<Item>> GetItemsAsync(long userId, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        List<CoreItemRecord> rows = await db.CoreItems.AsNoTracking()
            .Where(x => x.UserId == userId && x.EscrowUserId != -1)
            .OrderBy(x => x.Quality)
            .ThenBy(x => x.Guid)
            .ToListAsync(cancellationToken);
        List<Item> result = [];
        foreach (CoreItemRecord row in rows)
        {
            Item? item = RestoreItem(row);
            if (item is not null) result.Add(item);
        }
        return result;
    }

    private static Item? RestoreItem(CoreItemRecord row)
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
