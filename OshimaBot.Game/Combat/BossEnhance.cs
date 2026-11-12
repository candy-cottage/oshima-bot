using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Game.Services;
using CoreCharacter = FunGame.Core.Entity.Character;

namespace OshimaBot.Game.Combat;

/// <summary>
/// 敌人强化：旧版 <c>FunGameService.EnhanceBoss</c> 的那一套（穿 Red 装备、挂金色魔法卡包、
/// 塞消耗品、随机被动与绝招、<c>DynamicsEffect</c> 加成的「专属被动」）。
/// <para>Boss 池与共斗房间共用这一份，避免两处各写一遍再慢慢跑偏。</para>
/// </summary>
public static class BossEnhance
{
    /// <summary>
    /// 旧版把强化做成挂在「BOSS专属被动」上的**技能特效**（`DynamicsEffect` + `ExMDF`），
    /// 而不是直接改角色字段——这样角色信息里能看到加成，且随重建/复活一起恢复，符合 Core 的特效体系。
    /// <para>**带 2 的参数（exhp2/exmp2）是百分比加成**，对应 `Ex*Percentage`；不带 2 的 exatk/exdef 才是加值。</para>
    /// </summary>
    public static void ApplyDynamics(CoreCharacter character, int level, string passiveName = "BOSS专属被动")
    {
        Skill carrier = new OpenSkill(0, passiveName, [], character);

        DynamicsEffect dynamics = new(carrier, new Dictionary<string, object>
        {
            ["exatk"] = 3.4 * level,       // 加值
            ["exdef"] = 3.4 * level,       // 加值
            ["exhp2"] = 1.5,               // 百分比
            ["exmp2"] = 0.8,               // 百分比
            ["exhr"] = 0.15 * level,
            ["exmr"] = 0.1 * level,
            ["excr"] = 0.35,
            ["excrd"] = 0.9,
            ["excdr"] = 0.25,
            ["exacc"] = 0.25
        }, character);
        HookContext context = new(null, character);
        dynamics.OnEffectGained(context);
        carrier.Effects.Add(dynamics);

        // 旧版另挂的 ExMDF：mdftype=0（MagicType.None）、mdfvalue=0.15。
        ExMDF exMdf = new(carrier, new Dictionary<string, object>
        {
            ["mdftype"] = (int)MagicType.None,
            ["mdfvalue"] = 0.15
        }, character);
        exMdf.OnEffectGained(context);
        carrier.Effects.Add(exMdf);

        character.Skills.Add(carrier);
    }

    /// <summary>旧版给 Boss 穿 5 件 Red 品质装备（武器/防具/鞋子/饰品×2），每类随机挑一件。</summary>
    public static void EquipRedGear(CoreCharacter character, Random random)
    {
        ItemType[] slots = [ItemType.Weapon, ItemType.Armor, ItemType.Shoes, ItemType.Accessory, ItemType.Accessory];
        foreach (ItemType type in slots)
        {
            List<Item> pool = [.. FunGameService.AllItems
                .Where(i => i.ItemType == type && i.QualityType == QualityType.Red)];
            if (pool.Count == 0) continue;

            Item gear = pool[random.Next(pool.Count)].Copy();
            gear.Guid = Guid.NewGuid();
            gear.IsLock = true;
            character.Equip(gear);
        }
    }

    /// <summary>旧版给 Boss 挂 5 张魔法卡合成的金色卡包，卡内魔法等级按 <paramref name="magicLevel"/> 折算。</summary>
    public static void EquipMagicCardPack(CoreCharacter character, Random random, int magicLevel)
    {
        List<Item> cards = FunGameService.GenerateMagicCards(random, 5, QualityType.Red);
        Item? pack = FunGameService.ConflateMagicCardPack(random, cards);
        if (pack is null) return;

        pack.QualityType = QualityType.Gold;
        foreach (Skill magic in pack.Skills.Magics) magic.Level = magicLevel;
        pack.Guid = Guid.NewGuid();
        pack.IsLock = true;
        character.Equip(pack);
    }

    /// <summary>旧版在非 Unit 场景下给 Boss 塞 2 个消耗品（仅当背包未满 5 格）。</summary>
    public static void AddConsumables(CoreCharacter character, Random random)
    {
        if (character.Items.Count >= 5) return;

        List<Item> pool = [.. FunGameService.Items.Where(i => i.ItemType == ItemType.Consumable)];
        if (pool.Count == 0) return;

        for (int i = 0; i < 2; i++)
        {
            Item consumable = pool[random.Next(pool.Count)].Copy();
            consumable.Guid = Guid.NewGuid();
            character.Items.Add(consumable);
        }
    }

    /// <summary>旧版随机挂一个被动与一个绝招（等级按 cutRate 折算）。</summary>
    public static void AddBossSkills(CoreCharacter character, Random random, int skillLevel)
    {
        List<Skill> passives = [.. FunGameService.PassiveSkills, .. FunGameService.CommonPassiveSkills];
        if (passives.Count > 0)
        {
            Skill passive = passives[random.Next(passives.Count)].Copy();
            passive.Character = character;
            passive.Level = 1;
            character.Skills.Add(passive);
        }

        List<Skill> supers = [.. FunGameService.SuperSkills, .. FunGameService.CommonSuperSkills];
        if (supers.Count > 0)
        {
            Skill super = supers[random.Next(supers.Count)].Copy();
            super.Character = character;
            super.Level = skillLevel;
            character.Skills.Add(super);
        }
    }
}
