using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;

namespace OshimaBot.Content.Skills
{
    public class 折射 : Skill
    {
        public override long Id => (long)PassiveID.折射;
        public override string Name => "折射";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 折射(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 折射特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 折射特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"受到伤害时，减少 {减伤比例 * 100:0.##}% 的伤害，并将此次实际伤害的 {反弹比例 * 100:0.##}% 反弹给攻击者（真实伤害）。";

        private double 减伤比例 => Skill.Character != null ? 0.12 + Skill.Character.Level * 0.001 : 0.12;
        private double 反弹比例 => Skill.Character != null ? 0.2 + Skill.Character.Level * 0.002 : 0.2;

        public override AlterActualDamageResult AlterActualDamageAfterCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character attacker || ctx.Enemy is not Character character) return default;
            if (Skill.Character == null || Skill.Character != character) return default;
            if (attacker == character) return default;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return default;
            double reduce = ctx.Damage * 减伤比例;
            return new AlterActualDamageResult { DamageDelta = -reduce };
        }

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character attacker || ctx.Enemy is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (attacker == character) return;
            if (ctx.ActualDamage <= 0) return;
            double bounce = ctx.ActualDamage * 反弹比例;
            if (bounce <= 0) return;
            WriteLine($"[ {character} ] 发动了折射！将 {bounce:0.##} 点伤害反弹给了 [ {attacker} ]！");
            DamageToEnemy(character, attacker, DamageType.True, MagicType.None, bounce, new(character)
            {
                TriggerEffects = false
            });
        }
    }
}
