using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Game.Combat;
using OshimaBot.Game.Services;
using CoreCharacter = FunGame.Core.Entity.Character;

namespace OshimaBot.Gameplay;

/// <summary>
/// 把**玩家存档里的角色**变成一场战斗里能用的单位。
/// <para>为什么不能直接用存档快照：存档是 JSON，反序列化后所有技能都变成 <c>OpenSkill</c>，
/// 子类行为（战技/绝招的具体效果）全部丢失；装备的属性倒是能保留。
/// 所以这里走「模板重建 + 回填成长」：</para>
/// <list type="number">
/// <item>按 <c>CatalogId</c> 从内容包重建角色（保证技能是 typed 的）；</item>
/// <item>套回等级 / 经验 / 突破 / 改名 / 普攻等级；</item>
/// <item>挂上该角色的签名技能（<c>AddCharacterSkills</c>），再把存档里的技能等级按 <c>Id</c> 套回去；</item>
/// <item>把关卡快照里的 6 件装备复制到战斗单位（模板里没有装备）。</item>
/// </list>
/// <para>这套口径同时修掉两个旧病：装备与技能等级此前对战斗完全没有影响。</para>
/// </summary>
public static class CombatUnits
{
    /// <summary>主角色的战斗单位；内容包缺模板时返回 null。</summary>
    public static CoreCharacter? FromPlayer(PlayerCharacter player)
    {
        CoreCharacter? battle = CombatCharacterFactory.FromCatalog(
            player.CatalogId, player.Level, player.Exp, player.BreakLevel);
        if (battle is null) return null;

        battle.Name = player.Name;
        battle.NormalAttack.Level = player.Core.NormalAttack.Level;

        AttachSkills(player, battle);
        TransferEquipment(player.Core, battle);

        // 旧版每场战斗前都会 Recovery：满血满蓝进场，EP 归 200。
        battle.Recovery(EP: 200);
        return battle;
    }

    /// <summary>
    /// 技能分两步：先让内容包按角色 Id 挂上 typed 技能（此时全为 0/1 级），
    /// 再把存档里同名技能的等级搬过来。存档里的技能对象本身不能直接用——反序列化后是 <c>OpenSkill</c>。
    /// </summary>
    private static void AttachSkills(PlayerCharacter player, CoreCharacter battle)
    {
        FunGameService.AddCharacterSkills(battle, 1, 0, 0);

        foreach (Skill skill in battle.Skills)
        {
            Skill? stored = player.Core.Skills.FirstOrDefault(s => s.Id == skill.Id);
            if (stored is not null) skill.Level = stored.Level;
        }
    }

    /// <summary>把存档里的 6 件装备复制过去；复制是为了不把 <c>Character</c> 反向指到存档实体上。</summary>
    private static void TransferEquipment(CoreCharacter source, CoreCharacter battle)
    {
        EquipSlot slot = source.EquipSlot;
        Item?[] gear =
        [
            slot.MagicCardPack, slot.Weapon, slot.Armor, slot.Shoes, slot.Accessory1, slot.Accessory2
        ];

        foreach (Item item in gear.OfType<Item>())
        {
            Item copy = item.Copy();
            copy.Guid = Guid.NewGuid();
            battle.Equip(copy, out Item? _);
        }
    }
}
