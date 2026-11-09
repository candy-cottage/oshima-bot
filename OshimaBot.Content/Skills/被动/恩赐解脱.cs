using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;

namespace OshimaBot.Content.Skills
{
    public class 恩赐解脱 : Skill
    {
        public override long Id => (long)PassiveID.恩赐解脱;
        public override string Name => "恩赐解脱";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 恩赐解脱(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 恩赐解脱特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 恩赐解脱特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"在普通攻击暴击时，有 {触发概率 * 100:0.##}% 概率将伤害提升 {暴击伤害提升 * 100:0.##}%。";

        private double 触发概率 => 0.15;
        private double 暴击伤害提升 => Skill.Character != null ? 0.2 + (Skill.Character.Level - 1) * 0.05 : 0.2;

        public override AlterActualDamageResult AlterActualDamageAfterCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Trigger != Skill.Character) return default;
            if (!ctx.IsNormalAttack || ctx.DamageResult != DamageResult.Critical) return default;
            if (Random.NextDouble() > 触发概率) return default;
            double damage = ctx.Damage;
            double exDamage = damage * 暴击伤害提升;
            WriteLine($"[ {character} ] 发动了恩赐解脱！伤害提升了 {暴击伤害提升 * 100:0.##}%，额外造成 {exDamage:0.##} 点伤害！");
            return new()
            {
                DamageDelta = exDamage
            };
        }
    }
}
