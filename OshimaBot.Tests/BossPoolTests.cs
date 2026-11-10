using OshimaBot.Game.Combat;
using OshimaBot.Game.Services;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 对照旧版 <c>FunGameService.GenerateBoss</c> / <c>EnhanceBoss</c> 校验 Boss 池：
/// 等级分档、red 装备、金色卡包、消耗品、随机被动与绝招、DynamicsEffect 数值、共享血量。
/// </summary>
[Collection(BossPoolCollection.Name)]
public sealed class BossPoolTests
{
    static BossPoolTests() => FunGameRuntime.EnsureInitialized();

    [Fact]
    public void Pool_KeepsTenTargetsWithTieredLevels()
    {
        BossPool.Reset();

        IReadOnlyList<BossEntry> all = BossPool.All;
        Assert.Equal(BossPool.PoolSize, all.Count);

        // 等级 = MaxLevel / cutRate，cutRate 1/2/4 → 60 / 30 / 15
        Assert.All(all, boss => Assert.Contains(boss.Level, new[] { 60, 30, 15 }));
        Assert.Contains(all, boss => boss.Level == 60);
        Assert.Contains(all, boss => boss.Level == 30);
        Assert.Contains(all, boss => boss.Level == 15);

        // 生成后回满，且索引唯一
        Assert.All(all, boss => Assert.Equal(boss.Character.MaxHP, boss.Character.HP, 3));
        Assert.Equal(all.Count, all.Select(boss => boss.Index).Distinct().Count());
    }

    [Fact]
    public void Boss_CarriesDynamicsEffectAndFullGear()
    {
        BossPool.Reset();
        var boss = BossPool.All[0].Character;

        // 带 2 的参数是百分比（exhp2/exmp2），不带 2 的是加值（exatk/exdef）
        Assert.Equal(3.4 * BossPool.All[0].Level, boss.ExATK2, 3);
        Assert.Equal(3.4 * BossPool.All[0].Level, boss.ExDEF2, 3);
        Assert.Equal(1.5, boss.ExHPPercentage, 6);
        Assert.Equal(0.8, boss.ExMPPercentage, 6);
        Assert.Equal(0.15 * BossPool.All[0].Level, boss.ExHR, 3);
        Assert.Equal(0.1 * BossPool.All[0].Level, boss.ExMR, 3);

        // 旧版 excr / excrd / excdr / exacc
        Assert.Equal(0.35, boss.ExCritRate, 6);
        Assert.Equal(0.9, boss.ExCritDMG, 6);
        Assert.Equal(0.25, boss.ExCDR, 6);
        Assert.Equal(0.25, boss.ExAccelerationCoefficient, 6);

        // 5 件 Red 装备 + 金色魔法卡包
        Assert.NotNull(boss.EquipSlot.Weapon);
        Assert.NotNull(boss.EquipSlot.Armor);
        Assert.NotNull(boss.EquipSlot.Shoes);
        Assert.NotNull(boss.EquipSlot.Accessory1);
        Assert.NotNull(boss.EquipSlot.Accessory2);
        Assert.NotNull(boss.EquipSlot.MagicCardPack);
        Assert.Equal(FunGame.Core.Library.Constant.QualityType.Gold, boss.EquipSlot.MagicCardPack!.QualityType);

        // 2 个消耗品
        Assert.True(boss.Items.Count >= 2, $"消耗品数量：{boss.Items.Count}");

        // 比模板多了随机被动与随机绝招
        var template = FunGameService.Characters[0];
        Assert.True(boss.Skills.Count > template.Skills.Count,
            $"Boss 技能 {boss.Skills.Count}，模板 {template.Skills.Count}");
    }

    [Fact]
    public void Damage_AccumulatesAcrossAttemptsAndKillReplacesTarget()
    {
        BossPool.Reset();
        BossEntry target = Assert.IsType<BossEntry>(BossPool.Current());
        int index = target.Index;
        double half = target.Character.MaxHP / 2;

        // 未击杀：写回剩余生命
        Assert.False(BossPool.ApplyResult(target, half, target.Character.MaxMP, 0));
        BossEntry again = Assert.IsType<BossEntry>(BossPool.Get(index));
        Assert.Equal(half, again.Character.HP, 3);

        // 下一次挑战从残血开始
        var fighter = BossPool.CreateFighter(again);
        Assert.Equal(half, fighter.HP, 3);
        Assert.Equal(again.Character.MaxHP, fighter.MaxHP, 3);

        // 击杀：移出池并补一只新的，池子仍是 10
        Assert.True(BossPool.ApplyResult(again, 0, 0, 0));
        Assert.Null(BossPool.Get(index));
        Assert.Equal(BossPool.PoolSize, BossPool.All.Count);
    }

    [Fact]
    public void Fighter_IsCopySoPoolStateIsNotMutatedByBattle()
    {
        BossPool.Reset();
        BossEntry target = Assert.IsType<BossEntry>(BossPool.Current());
        double before = target.Character.HP;

        var fighter = BossPool.CreateFighter(target);
        fighter.HP = 1;
        fighter.Name = "被改写的副本";

        Assert.Equal(before, target.Character.HP, 3);
        Assert.NotEqual("被改写的副本", target.Character.Name);
    }
}
