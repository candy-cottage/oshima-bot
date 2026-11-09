using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    public class 宿命时律 : Skill
    {
        public override long Id => (long)SuperSkillID.宿命时律;
        public override string Name => "宿命时律";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 100;
        public override double CD => 60 - 4 * (Level - 1);
        public override double HardnessTime { get; set; } = 10;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;

        public 宿命时律(Character? character = null) : base(SkillType.SuperSkill, character)
        {
            Effects.Add(new 宿命时律特效(this));
        }
    }

    public class 宿命时律特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"{Duration:0.##} {GameplayEquilibriumConstant.InGameTime}内，提升自身 25% 物理伤害减免和魔法抗性，普通攻击转为魔法伤害，且硬直时间减少 30%，并基于 {智力系数 * 100:0.##}% 智力 [ {智力加成:0.##} ] 强化普通攻击伤害。";
        public override bool Durative => true;
        public override double Duration => 40;
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        private double 智力系数 => 1.4 + 0.4 * (Level - 1);
        private double 智力加成 => 智力系数 * Skill.Character?.INT ?? 0;
        private double 物理伤害减免 => 0.25;
        private double 魔法抗性 => 0.25;
        private double 实际物理伤害减免 = 0;
        private double 实际魔法抗性 = 0;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            character.NormalAttack.SetMagicType(new(this, true, MagicType.None, 999), GamingQueue);
            实际物理伤害减免 = 物理伤害减免;
            实际魔法抗性 = 魔法抗性;
            character.ExPDR += 实际物理伤害减免;
            character.MDF[character.MagicType] += 实际魔法抗性;
            WriteLine($"[ {character} ] 提升了 {实际物理伤害减免 * 100:0.##}% 物理伤害减免，{实际魔法抗性 * 100:0.##}% 魔法抗性！！");
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            character.NormalAttack.UnsetMagicType(this, GamingQueue);
            character.ExPDR -= 实际物理伤害减免;
            character.MDF[character.MagicType] -= 实际魔法抗性;
            实际物理伤害减免 = 0;
            实际魔法抗性 = 0;
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
                WriteLine($"[ {character} ] 发动了宿命时律！伤害提升了 {智力加成:0.##} 点！");
                return 智力加成;
            }
            return 0;
        }

        public override AlterHardnessTimeResult AlterHardnessTimeAfterNormalAttack(HardnessContext ctx)
        {
            if (ctx.Trigger is not Character character) return default;
            return new AlterHardnessTimeResult { Factor = -0.7 };
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
            GamingQueue?.AddApplyEffects(caster, EffectType.DamageBoost, EffectType.Haste, EffectType.DefenseBoost);
        }
    }
}
