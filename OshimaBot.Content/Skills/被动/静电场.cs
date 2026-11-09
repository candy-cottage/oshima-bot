using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 静电场 : Skill
    {
        public override long Id => (long)PassiveID.静电场;
        public override string Name => "静电场";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 静电场(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 静电场特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 静电场特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"技能命中敌人时，引发静电场造成 {静电伤害:0.##} 点额外真实伤害。静电场每 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}只能发动一次。";

        private double 剩余冷却 = 0;

        private double 冷却时间 => Skill.Character != null ? 6 - Skill.Character.Level * 0.02 : 6;
        private double 静电伤害 => Skill.Character != null ? 40 + Skill.Character.Level * 4 + Skill.Character.ATK * 0.1 : 40;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (ctx.IsNormalAttack) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (剩余冷却 > 0) return;
            剩余冷却 = 冷却时间;
            WriteLine($"[ {character} ] 发动了静电场！对 [ {enemy} ] 造成了 {静电伤害:0.##} 点真实伤害！");
            DamageToEnemy(character, enemy, DamageType.True, MagicType.None, 静电伤害, new(character)
            {
                TriggerEffects = false
            });
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (剩余冷却 > 0)
            {
                剩余冷却 -= ctx.Elapsed;
            }
        }
    }
}
