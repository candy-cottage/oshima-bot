using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;

namespace OshimaBot.Content.Skills
{
    public class 少女绮想 : Skill
    {
        public override long Id => (long)PassiveID.少女绮想;
        public override string Name => "少女绮想";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";

        public 少女绮想(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 少女绮想特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 少女绮想特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"{Skill.SkillOwner()}进入祈祷的心境。每经过 {时间流逝} {GameplayEquilibriumConstant.InGameTime}，提升 {伤害提升 * 100:0.##}% 所有伤害，无上限。受到伤害时清零；造成伤害后，累计提升减少总量的 30%。（下一次提升在：{下一次提升:0.##} {GameplayEquilibriumConstant.InGameTime}后{(累计伤害 > 0 ? $"，当前总提升：{累计伤害 * 100:0.##}%" : "")}）";

        private readonly double 时间流逝 = 7;
        private readonly double 伤害提升 = 0.21;
        private double 累计伤害 = 0;
        private double 下一次提升 = 7;

        public override AlterActualDamageResult AlterActualDamageAfterCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return default;
            double damage = ctx.Damage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageType damageType = ctx.DamageType;
            MagicType magicType = ctx.MagicType;
            DamageResult damageResult = ctx.DamageResult;
            Dictionary<Effect, double> totalDamageBonus = ctx.TotalDamageBonus;
            if ((damageResult == DamageResult.Normal || damageResult == DamageResult.Critical) && character == Skill.Character)
            {
                double 实际伤害提升 = damage * 累计伤害;
                if (实际伤害提升 > 0) WriteLine($"[ {character} ] 的伤害提升了 {累计伤害 * 100:0.##}% [ {实际伤害提升:0.##} ] 点！");
                累计伤害 *= 0.7;
                return new AlterActualDamageResult { DamageDelta = 实际伤害提升 };

            }
            return default;

        }

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            double damage = ctx.Damage;
            double actualDamage = ctx.ActualDamage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageType damageType = ctx.DamageType;
            MagicType magicType = ctx.MagicType;
            DamageResult damageResult = ctx.DamageResult;
            if ((damageResult == DamageResult.Normal || damageResult == DamageResult.Critical) && enemy == Skill.Character && actualDamage > 0 && !enemy.Effects.Where(e => e is 绝对领域特效 && e.Skill.Character == character).Any())
            {
                累计伤害 = 0;
            }
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            double eapsed = ctx.Elapsed;
            if (GamingQueue != null)
            {
                下一次提升 -= eapsed;
                if (下一次提升 <= 0)
                {
                    累计伤害 += 伤害提升;
                    下一次提升 += 时间流逝;
                    WriteLine($"[ {character} ] 的 [ {Name} ] 效果增加了，当前总提升：{累计伤害 * 100:0.##}%。");
                }
            }
        }
    }
}
