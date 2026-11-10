using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Game.Services;
using Xunit;
using Xunit.Abstractions;

namespace OshimaBot.Tests;

/// <summary>
/// 验证 Core v3 实体可以直接序列化/反序列化。
/// <para>结论若成立，持久层应保存 Core 实体快照，而不是另建一套字段映射。</para>
/// </summary>
public class CoreDataModelTests(ITestOutputHelper output)
{
    static CoreDataModelTests() => FunGameRuntime.EnsureInitialized();

    [Fact]
    public void Character_RoundTripsThroughCoreJson()
    {
        Character source = FunGameService.Characters[0].Copy();
        source.Level = 42;
        source.EXP = 1234.5;
        source.LevelBreak = 1;
        source.Promotion = 150;
        source.NickName = "往返测试";

        string json = JsonService.GetString(source);
        Character? restored = JsonService.GetObject<Character>(json);

        Assert.NotNull(restored);
        Assert.Equal(source.Guid, restored!.Guid);
        Assert.Equal(42, restored.Level);
        Assert.Equal(1234.5, restored.EXP);
        Assert.Equal(1, restored.LevelBreak);
        Assert.Equal(150, restored.Promotion);
        Assert.Equal("往返测试", restored.NickName);
        output.WriteLine($"character json length={json.Length}, equip slots={(restored.EquipSlot is null ? "null" : "ok")}");
    }

    [Fact]
    public void Item_RoundTripsThroughCoreJson()
    {
        Item source = FunGameService.AllItems[0].Copy();
        source.QualityType = QualityType.Red;
        source.Price = 12345.6;
        source.IsLock = true;

        string json = JsonService.GetString(source);
        Item? restored = JsonService.GetObject<Item>(json);

        Assert.NotNull(restored);
        Assert.Equal(source.Guid, restored!.Guid);
        Assert.Equal(QualityType.Red, restored.QualityType);
        Assert.Equal(12345.6, restored.Price);
        Assert.True(restored.IsLock);
        output.WriteLine($"item json length={json.Length}");
    }

    [Fact]
    public void CoreConstants_KeepTwoCurrenciesAndSevenQualities()
    {
        // 证据：Core v3 未移除「两种资源」与「7 档品质」，迁移应直接复用。
        // 注意：Oshima 内容包把第二资源（Inventory.Materials）显示为「钻石」，
        // 但字段与语义仍是 Core 的 Materials（double），不是另一种货币。
        Assert.Equal("金币", General.GameplayEquilibriumConstant.InGameCurrency);
        Assert.False(string.IsNullOrWhiteSpace(General.GameplayEquilibriumConstant.InGameMaterial));
        Assert.Equal(7, Enum.GetValues<QualityType>().Length);
        Assert.Equal(60, General.GameplayEquilibriumConstant.MaxLevel);
        Assert.Contains(10, General.GameplayEquilibriumConstant.LevelBreakList);
        output.WriteLine($"currency={General.GameplayEquilibriumConstant.InGameCurrency}, " +
                         $"material={General.GameplayEquilibriumConstant.InGameMaterial}");
        output.WriteLine($"qualities: {string.Join(",", Enum.GetNames<QualityType>())}");
        output.WriteLine($"level break list: {string.Join(",", General.GameplayEquilibriumConstant.LevelBreakList)}");
    }

    [Fact]
    public void EquipSlot_HasSixSlots()
    {
        EquipSlot slot = new();
        // 6 槽：魔法卡包 / 武器 / 防具 / 鞋子 / 饰品1 / 饰品2
        Assert.Null(slot.MagicCardPack);
        Assert.Null(slot.Weapon);
        Assert.Null(slot.Armor);
        Assert.Null(slot.Shoes);
        Assert.Null(slot.Accessory1);
        Assert.Null(slot.Accessory2);
    }

    [Fact]
    public void Inventory_UsesDoubleCurrencyAndFourManSquad()
    {
        Inventory inventory = new(new User(1, "test"));
        inventory.Credits = 1234.56;
        inventory.Materials = 78.9;
        Assert.Equal(1234.56, inventory.Credits);
        Assert.Equal(78.9, inventory.Materials);
        Assert.IsType<HashSet<long>>(inventory.Squad);
    }
}
