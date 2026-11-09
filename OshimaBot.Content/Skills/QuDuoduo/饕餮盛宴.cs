using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    public class 饕餮盛宴 : Skill
    {
        public override long Id => (long)SuperSkillID.饕餮盛宴;
        public override string Name => "饕餮盛宴";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 100;
        public override double CD => 45 + 1 * (Level - 1);
        public override double HardnessTime { get; set; } = 7;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;

        public 饕餮盛宴(Character? character = null) : base(SkillType.SuperSkill, character)
        {
            Effects.Add(new 饕餮盛宴特效(this));
        }
    }

    public class 饕餮盛宴特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"获得 {吸血系数 * 100:0.##}% 吸血，持续 {Duration:0.##} {GameplayEquilibriumConstant.InGameTime}。";
        public override bool Durative => true;
        public override double Duration => 30;
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        private double 吸血系数 => 0.2 + 0.05 * (Level - 1);

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            double damage = ctx.Damage;
            double actualDamage = ctx.ActualDamage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageType damageType = ctx.DamageType;
            MagicType magicType = ctx.MagicType;
            DamageResult damageResult = ctx.DamageResult;
            if (character == Skill.Character && (damageResult == DamageResult.Normal || damageResult == DamageResult.Critical) && character.HP < character.MaxHP)
            {
                double 实际吸血 = 吸血系数 * damage;
                HealToTarget(character, character, 实际吸血);
            }
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            Dictionary<string, object> others = ctx.Others;
            RemainDuration = Duration;
            if (!caster.Effects.Contains(this))
            {
                caster.Effects.Add(this);
                OnEffectGained(new HookContext(GamingQueue, caster));
            }
            GamingQueue?.AddApplyEffects(caster, EffectType.Lifesteal);
        }
    }
}
