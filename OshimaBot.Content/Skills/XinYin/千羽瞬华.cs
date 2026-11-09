using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    public class 千羽瞬华 : Skill
    {
        public override long Id => (long)SuperSkillID.千羽瞬华;
        public override string Name => "千羽瞬华";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 100;
        public override double CD => 60;
        public override double HardnessTime { get; set; } = 10;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;

        public 千羽瞬华(Character? character = null) : base(SkillType.SuperSkill, character)
        {
            Effects.Add(new 千羽瞬华特效(this));
        }
    }

    public class 千羽瞬华特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"{Skill.SkillOwner()}将世间事物的弱点完全暴露在「千羽」的绚烂之下。{Duration:0.##} {GameplayEquilibriumConstant.InGameTime}内，增加{Skill.SkillOwner()} {攻击力提升系数 * 100:0.##}% 攻击力 [ {攻击力提升:0.##} ]、{物理穿透提升 * 100:0.##}% 物理穿透和 {闪避率提升 * 100:0.##}% 闪避率（不可叠加），普通攻击硬直时间额外减少 20%，基于 {系数 * 100:0.##}% 敏捷 [ {伤害加成:0.##} ] 强化普通攻击的伤害。在持续时间内，[ 心灵之弦 ] 的冷却时间降低至 3 {GameplayEquilibriumConstant.InGameTime}。";
        public override bool Durative => true;
        public override double Duration => 30;
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        private double 系数 => 1.2 * (1 + 0.5 * (Skill.Level - 1));
        private double 伤害加成 => 系数 * Skill.Character?.AGI ?? 0;
        private double 攻击力提升系数 => Skill.Level > 0 ? 0.15 + 0.03 * (Skill.Level - 1) : 0.15;
        private double 攻击力提升 => 攻击力提升系数 * Skill.Character?.BaseATK ?? 0;
        private double 物理穿透提升 => Skill.Level > 0 ? 0.1 + 0.03 * (Skill.Level - 1) : 0.1;
        private double 闪避率提升 => Skill.Level > 0 ? 0.1 + 0.02 * (Skill.Level - 1) : 0.1;

        private double 实际的攻击力提升 = 0;
        private double 实际的物理穿透提升 = 0;
        private double 实际的闪避率提升 = 0;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            实际的攻击力提升 = 攻击力提升;
            实际的物理穿透提升 = 物理穿透提升;
            实际的闪避率提升 = 闪避率提升;
            character.ExATK2 += 实际的攻击力提升;
            character.PhysicalPenetration += 实际的物理穿透提升;
            character.ExEvadeRate += 实际的闪避率提升;
            if (character.Effects.FirstOrDefault(e => e is 心灵之弦特效 && e.Skill.Character == character) is 心灵之弦特效 e)
            {
                e.基础冷却时间 = 3;
                if (e.冷却时间 > e.基础冷却时间) e.冷却时间 = e.基础冷却时间;
            }
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            character.ExATK2 -= 实际的攻击力提升;
            character.PhysicalPenetration -= 实际的物理穿透提升;
            character.ExEvadeRate -= 实际的闪避率提升;
            if (character.Effects.FirstOrDefault(e => e is 心灵之弦特效 && e.Skill.Character == character) is 心灵之弦特效 e)
            {
                e.基础冷却时间 = 10;
            }
        }

        public override AlterActionTypeResult AlterActionTypeBeforeAction(DecisionContext ctx)
        {
            return new AlterActionTypeResult { PNormalAttack = ctx.PNormalAttack + 0.1 };
        }

        public override double AlterExpectedDamageBeforeCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return 0;
            double damage = ctx.Damage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageType damageType = ctx.DamageType;
            MagicType magicType = ctx.MagicType;
            Dictionary<Effect, double> totalDamageBonus = ctx.TotalDamageBonus;
            if (character == Skill.Character && isNormalAttack)
            {
                return 伤害加成;
            }
            return 0;
        }

        public override AlterHardnessTimeResult AlterHardnessTimeAfterNormalAttack(HardnessContext ctx)
        {
            if (ctx.Trigger is not Character character) return default;
            return new AlterHardnessTimeResult { Factor = -0.2 };
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            Dictionary<string, object> others = ctx.Others;
            实际的攻击力提升 = 0;
            实际的物理穿透提升 = 0;
            实际的闪避率提升 = 0;
            RemainDuration = Duration;
            if (!caster.Effects.Contains(this))
            {
                caster.Effects.Add(this);
                OnEffectGained(new HookContext(GamingQueue, caster));
            }
            GamingQueue?.AddApplyEffects(caster, EffectType.DamageBoost, EffectType.PenetrationBoost);
        }
    }
}
