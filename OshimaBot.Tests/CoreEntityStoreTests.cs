using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Game.Services;
using Xunit;
using Xunit.Abstractions;
using CoreCharacter = FunGame.Core.Entity.Character;

namespace OshimaBot.Tests;

/// <summary>
/// v3.1 数据契约：货币为 double、品质为 QualityType(0–6)、装备为 6 槽、
/// 角色/物品保存 Core 实体快照。测试用真实 SQLite 内存库验证往返。
/// </summary>
public class CoreEntityStoreTests(ITestOutputHelper output)
{
    static CoreEntityStoreTests() => FunGameRuntime.EnsureInitialized();

    private static (ServiceProvider Provider, SqliteConnection Connection) CreateStore()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<BotDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<CoreEntityStore>();
        ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        return (provider, connection);
    }

    /// <summary>
    /// 注意：Core 的 <c>Character.Copy()</c> 会复制 <c>Guid</c>（Character.cs:2111），
    /// 从模板造玩家角色时必须重赋 Guid，否则会与模板或彼此撞业务主键。
    /// </summary>
    private static CoreCharacter NewCharacter()
    {
        CoreCharacter character = FunGameService.Characters[0].Copy();
        character.Guid = Guid.NewGuid();
        return character;
    }

    [Fact]
    public async Task Player_KeepsDoubleCurrency()
    {
        (ServiceProvider provider, SqliteConnection connection) = CreateStore();
        using (provider)
        using (connection)
        {
            CoreEntityStore store = provider.GetRequiredService<CoreEntityStore>();
            await store.SavePlayerAsync(7, 1234.56, 78.9, null);

            CorePlayerRecord? player = await store.GetPlayerAsync(7);
            Assert.NotNull(player);
            Assert.Equal(1234.56, player!.Credits, 6);
            Assert.Equal(78.9, player.Materials, 6);
        }
    }

    [Fact]
    public async Task Character_RoundTripsCoreFieldsAndEquipSlots()
    {
        (ServiceProvider provider, SqliteConnection connection) = CreateStore();
        using (provider)
        using (connection)
        {
            CoreEntityStore store = provider.GetRequiredService<CoreEntityStore>();
            CoreCharacter source = NewCharacter();
            source.Level = 37;
            source.EXP = 987.65;
            source.LevelBreak = 2;
            source.Promotion = 145;

            await store.SaveCharacterAsync(7, "oshima-shiya", source);
            CoreCharacter? restored = await store.GetCharacterAsync(source.Guid.ToString());

            Assert.NotNull(restored);
            Assert.Equal(source.Guid, restored!.Guid);
            Assert.Equal(37, restored.Level);
            Assert.Equal(987.65, restored.EXP, 6);
            Assert.Equal(2, restored.LevelBreak);
            Assert.Equal(145, restored.Promotion);
            Assert.NotNull(restored.EquipSlot);
            Assert.NotNull(restored.User);
            output.WriteLine($"restored {restored.Name} Lv{restored.Level} break{restored.LevelBreak}");
        }
    }

    [Fact]
    public async Task Character_IndexColumnsMatchSnapshot()
    {
        (ServiceProvider provider, SqliteConnection connection) = CreateStore();
        using (provider)
        using (connection)
        {
            CoreEntityStore store = provider.GetRequiredService<CoreEntityStore>();
            CoreCharacter source = NewCharacter();
            source.Level = 55;
            source.Promotion = 210;
            await store.SaveCharacterAsync(9, "catalog-x", source);

            using IServiceScope scope = provider.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CoreCharacterRecord row = await db.CoreCharacters.AsNoTracking().SingleAsync();
            Assert.Equal(55, row.Level);
            Assert.Equal(210, row.Promotion);
            Assert.Equal(9, row.UserId);
            Assert.Equal("catalog-x", row.CatalogId);
            Assert.False(string.IsNullOrWhiteSpace(row.SnapshotJson));
        }
    }

    [Fact]
    public async Task Item_KeepsQualityTypeAndSixSlotEnum()
    {
        (ServiceProvider provider, SqliteConnection connection) = CreateStore();
        using (provider)
        using (connection)
        {
            CoreEntityStore store = provider.GetRequiredService<CoreEntityStore>();
            Item source = FunGameService.AllItems[0].Copy();
            source.QualityType = QualityType.Gold;
            source.Price = 13579.5;
            source.IsLock = true;
            source.EquipSlotType = EquipSlotType.Accessory2;

            await store.SaveItemAsync(7, "catalog-item", source, quantity: 3);
            Item? restored = await store.GetItemAsync(source.Guid.ToString());

            Assert.NotNull(restored);
            Assert.Equal(source.Guid, restored!.Guid);
            Assert.Equal(QualityType.Gold, restored.QualityType);
            Assert.Equal(13579.5, restored.Price, 6);
            Assert.True(restored.IsLock);
            Assert.Equal(EquipSlotType.Accessory2, restored.EquipSlotType);
            output.WriteLine($"restored item {restored.Name} quality={restored.QualityType}");
        }
    }

    [Fact]
    public async Task EscrowedItem_IsHiddenFromPlayerBag()
    {
        (ServiceProvider provider, SqliteConnection connection) = CreateStore();
        using (provider)
        using (connection)
        {
            CoreEntityStore store = provider.GetRequiredService<CoreEntityStore>();
            Item listed = FunGameService.AllItems[0].Copy();
            Item kept = FunGameService.AllItems[1].Copy();
            await store.SaveItemAsync(7, "a", listed, escrowUserId: -1);
            await store.SaveItemAsync(7, "b", kept);

            IReadOnlyList<Item> bag = await store.GetItemsAsync(7);
            Assert.Single(bag);
            Assert.Equal(kept.Guid, bag[0].Guid);
        }
    }

    [Fact]
    public async Task Characters_AreScopedByUserAndOrderedByLevel()
    {
        (ServiceProvider provider, SqliteConnection connection) = CreateStore();
        using (provider)
        using (connection)
        {
            CoreEntityStore store = provider.GetRequiredService<CoreEntityStore>();
            CoreCharacter low = NewCharacter();
            low.Level = 5;
            CoreCharacter high = NewCharacter();
            high.Level = 40;
            CoreCharacter other = NewCharacter();
            other.Level = 20;

            await store.SaveCharacterAsync(1, "a", low);
            await store.SaveCharacterAsync(1, "b", high);
            await store.SaveCharacterAsync(2, "c", other);

            IReadOnlyList<CoreCharacter> mine = await store.GetCharactersAsync(1);
            Assert.Equal(2, mine.Count);
            Assert.Equal(5, mine[0].Level);
            Assert.Equal(40, mine[1].Level);
        }
    }
}
