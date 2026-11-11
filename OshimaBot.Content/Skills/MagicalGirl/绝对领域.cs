using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    public class 绝对领域 : Skill
    {
        public override long Id => (long)SuperSkillID.绝对领域;
        public override string Name => "绝对领域";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => Math.Max(100, Character?.EP ?? 100);
        public override double CD => 60;
        public override double HardnessTime { get; set; } = 5;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;

        public 绝对领域(Character? character = null) : base(SkillType.SuperSkill, character)
        {
            Effects.Add(new 绝对领域特效(this));
        }
    }

    public class 绝对领域特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"{Skill.SkillOwner()}展开绝对领域。在 {Duration:0.##} {GameplayEquilibriumConstant.InGameTime}内，敏捷提升 {系数 * 100:0.##}% [ {敏捷提升:0.##} ]，无法受到任何伤害，但不免疫负面效果。此技能会消耗至少 100 点能量，每额外消耗 10 能量，持续时间提升 1 {GameplayEquilibriumConstant.InGameTime}。";
        public override bool Durative => true;
        public override double Duration => 释放时的能量值 >= 100 ? 13 + (释放时的能量值 - 100) * 0.1 : 14;
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        private double 系数 => 0.2 + 0.015 * (Level - 1);
        private double 敏捷提升 => 系数 * Skill.Character?.BaseAGI ?? 0;
        private double 实际敏捷提升 = 0;
        private double 释放时的能量值 = 0;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            实际敏捷提升 = 敏捷提升;
            character.ExAGI2 += 实际敏捷提升;
            WriteLine($"[ {character} ] 的敏捷提升了 {系数 * 100:0.##}% [ {实际敏捷提升:0.##} ] ！");
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            character.ExAGI2 -= 实际敏捷提升;
        }

        public override AlterActualDamageResult AlterActualDamageAfterCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return default;
            double damage = ctx.Damage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageType damageType = ctx.DamageType;
            MagicType magicType = ctx.MagicType;
            DamageResult damageResult = ctx.DamageResult;
            Dictionary<Effect, double> totalDamageBonus = ctx.TotalDamageBonus;
            if (enemy == Skill.Character && (damageResult == DamageResult.Normal || damageResult == DamageResult.Critical))
            {
                WriteLine($"[ {enemy} ] 发动了绝对领域，巧妙的化解了此伤害！");
                return new AlterActualDamageResult { IsEvaded = true };
            }
            return default;
        }

        public override BeforeApplyTrueDamageResult BeforeApplyTrueDamage(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return default;
            double damage = ctx.Damage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageResult damageResult = ctx.DamageResult;
            if (enemy == Skill.Character && (damageResult == DamageResult.Normal || damageResult == DamageResult.Critical))
            {
                WriteLine($"[ {enemy} ] 发动了绝对领域，巧妙的化解了此伤害！");
                return new BeforeApplyTrueDamageResult { NullifyDamage = true };
            }
            return default;
        }

        public override void OnSkillCasting(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            释放时的能量值 = caster.EP;
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
                实际敏捷提升 = 0;
                caster.Effects.Add(this);
                OnEffectGained(new HookContext(GamingQueue, caster));
            }
            GamingQueue?.AddApplyEffects(caster, EffectType.Invulnerable);
        }
    }
}
