using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Effects.SkillEffects
{
    public class 基于攻击力的伤害_带基础伤害 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"对{Skill.TargetDescription()}造成 {BaseDamage:0.##} + {ATKCoefficient * 100:0.##}% 攻击力 [ {Damage:0.##} ] 点{CharacterSet.GetDamageTypeName(DamageType, MagicType)}。";

        private double BaseDamage => Skill.Level > 0 ? 基础数值伤害 + 基础伤害等级成长 * (Skill.Level - 1) : 基础数值伤害;
        private double ATKCoefficient => Skill.Level > 0 ? 基础攻击力系数 + 基础系数等级成长 * (Skill.Level - 1) : 基础攻击力系数;
        private double Damage => BaseDamage + (ATKCoefficient * Skill.Character?.ATK ?? 0);
        private double 基础数值伤害 { get; set; } = 100;
        private double 基础伤害等级成长 { get; set; } = 50;
        private double 基础攻击力系数 { get; set; } = 0.2;
        private double 基础系数等级成长 { get; set; } = 0.2;
        private DamageType DamageType { get; set; } = DamageType.Magical;

        public 基于攻击力的伤害_带基础伤害(Skill skill, double 基础数值伤害, double 基础伤害等级成长, double 基础攻击力系数, double 基础系数等级成长, DamageType damageType = DamageType.Magical, MagicType magicType = MagicType.None) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            this.基础数值伤害 = 基础数值伤害;
            this.基础伤害等级成长 = 基础伤害等级成长;
            this.基础攻击力系数 = 基础攻击力系数;
            this.基础系数等级成长 = 基础系数等级成长;
            DamageType = damageType;
            MagicType = magicType;
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character enemy in targets)
            {
                DamageToEnemy(caster, enemy, DamageType, MagicType, Damage);
            }
        }
    }
}
