using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Effects.SkillEffects
{
    public class 基于属性的伤害 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"对{Skill.TargetDescription()}造成 {BaseDamage:0.##} + {AttributeCoefficient * 100:0.##}% {CharacterSet.GetPrimaryAttributeName(基于属性)} [ {Damage:0.##} ] 点{CharacterSet.GetDamageTypeName(DamageType, MagicType)}。";
        private double BaseDamage => Skill.Level > 0 ? 基础数值伤害 + 基础伤害等级成长 * (Skill.Level - 1) : 基础数值伤害;
        private double AttributeCoefficient => Skill.Level > 0 ? 基础属性系数 + 基础系数等级成长 * (Skill.Level - 1) : 基础属性系数;
        private double Damage
        {
            get
            {
                if (Skill.Character != null)
                {
                    double paValue = 基于属性 switch
                    {
                        PrimaryAttribute.STR => Skill.Character.STR,
                        PrimaryAttribute.AGI => Skill.Character.AGI,
                        _ => Skill.Character.INT,
                    };
                    return BaseDamage + AttributeCoefficient * paValue;
                }
                return BaseDamage;
            }
        }

        private PrimaryAttribute 基于属性 { get; set; } = PrimaryAttribute.INT;
        private double 基础数值伤害 { get; set; } = 100;
        private double 基础伤害等级成长 { get; set; } = 50;
        private double 基础属性系数 { get; set; } = 0.4;
        private double 基础系数等级成长 { get; set; } = 0.4;
        private DamageType DamageType { get; set; } = DamageType.Magical;

        public 基于属性的伤害(Skill skill, PrimaryAttribute 基于属性, double 基础数值伤害, double 基础伤害等级成长, double 基础属性系数, double 基础系数等级成长, DamageType damageType = DamageType.Magical, MagicType magicType = MagicType.None) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            this.基于属性 = 基于属性;
            this.基础数值伤害 = 基础数值伤害;
            this.基础伤害等级成长 = 基础伤害等级成长;
            this.基础属性系数 = 基础属性系数;
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
