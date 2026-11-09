using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;
using OshimaBot.Content.Skills;

namespace OshimaBot.Content.Items
{
    /// <summary>
    /// 装备 · 饰品【应急装置】—— 主动型团队应急模块
    /// <para/>设计意图：此前的辅助装备都是「被动监听」，本条补上「主动按钮」这一形态：
    /// 把它挂在身上，就多了一个「全队混合护盾 + 弱驱散」的应急按钮，代价是一次高额能量与较长冷却。
    /// <para/>实现：<c>Skills.Active = new 应急装置技能(character) { Level = f(QualityType) }</c>，
    /// 品质决定技能等级（Orange 4 / Red 5 / Gold 6）。战技等级上限即 6，故 Gold 档刚好拉满。
    /// <para/>效果由 <see cref="增加混合护盾值"/> 与 <see cref="弱驱散特效"/> 提供；
    /// 技能类型为 <c>SkillType.Skill</c>，由 <c>AddCharacterEquipSlotSkills</c> 纳入回合可选列表并消耗能量。
    /// <para/>⚠ 换装约定：主动技能不给他人挂监听器，因此换装无需清理状态；
    /// <see cref="Skill.CurrentCD"/> 属于技能自身，重新抽到即为满冷却状态。
    /// </summary>
    public abstract class 应急装置 : Item
    {
        protected 应急装置(Character? character, int 技能等级) : base(ItemType.Accessory)
        {
            Skills.Active = new 应急装置技能(character, this)
            {
                Level = 技能等级
            };
        }

        public override string Description => Skills.Active?.Description ?? "";

        public override string BackgroundStory => "巴掌大的金属匣，侧面的拉环上结着一层薄霜。匣子从不主动说话——它只在有人快要倒下时，嗡的一声展开。";
    }

    public class 应急装置1 : 应急装置
    {
        public override long Id => (long)AccessoryID.应急装置1;
        public override string Name => "应急装置 Lv.4";
        public override QualityType QualityType => QualityType.Orange;

        public 应急装置1(Character? character = null) : base(character, 4) { }
    }

    public class 应急装置2 : 应急装置
    {
        public override long Id => (long)AccessoryID.应急装置2;
        public override string Name => "应急装置 Lv.5";
        public override QualityType QualityType => QualityType.Red;

        public 应急装置2(Character? character = null) : base(character, 5) { }
    }

    public class 应急装置3 : 应急装置
    {
        public override long Id => (long)AccessoryID.应急装置3;
        public override string Name => "应急装置 Lv.6";
        public override QualityType QualityType => QualityType.Gold;

        public 应急装置3(Character? character = null) : base(character, 6) { }
    }

    /// <summary>
    /// 【应急装置】的主动技能：为全队（含自己）提供混合护盾并弱驱散。
    /// <para/>· 消耗能量、较长冷却，是一次「全队级」的救场
    /// <para/>· 护盾量随等级提升、冷却也随等级变长，等级由装备品质决定
    /// </summary>
    public class 应急装置技能 : Skill
    {
        public override long Id => (long)ItemActiveID.应急装置;
        public override string Name => "应急装置";
        public override string Description => string.Join("", Effects.Select(e => e.Description));
        public override double EPCost => Level > 0 ? 能量消耗基础 + 能量消耗等级成长 * (Level - 1) : 能量消耗基础;

        /// <summary>冷却随等级提升：Orange(Lv.4) 72 → Red(Lv.5) 76 → Gold(Lv.6) 80 秒</summary>
        public override double CD => Level > 0 ? 冷却基础 + 冷却等级成长 * (Level - 1) : 冷却基础;
        public override double HardnessTime { get; set; } = 8;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => true;
        public override bool SelectAllTeammates => true;

        private const double 能量消耗基础 = 70;
        private const double 能量消耗等级成长 = 4;
        private const double 冷却基础 = 60;
        private const double 冷却等级成长 = 4;
        private const double 护盾基础 = 80;
        private const double 护盾等级成长 = 80;

        public 应急装置技能(Character? character = null, Item? item = null) : base(SkillType.Skill, character)
        {
            Item = item;
            Effects.Add(new 增加混合护盾值(this, 护盾基础, 护盾等级成长));
            Effects.Add(new 弱驱散特效(this));
        }
    }
}
