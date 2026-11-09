using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Skills
{
    public class 深海重击 : Skill
    {
        public override long Id => (long)PassiveID.深海重击;
        public override string Name => "深海重击";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 深海重击(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 深海重击特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 深海重击特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"每 {攻击次数} 次普通攻击后的下一次普通攻击造成眩晕 {眩晕时间:0.##} {GameplayEquilibriumConstant.InGameTime}并附带 {额外伤害:0.##} 点额外伤害。";

        private int 当前计数 = 0;

        private int 攻击次数 => 3;
        private double 眩晕时间 => Skill.Character != null ? 1.5 + Skill.Character.Level * 0.05 : 1.5;
        private double 额外伤害 => Skill.Character != null ? 60 + Skill.Character.Level * 6 + Skill.Character.ATK * 0.5 : 60;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (!ctx.IsNormalAttack) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            当前计数++;
            if (当前计数 < 攻击次数) return;
            当前计数 = 0;
            WriteLine($"[ {character} ] 发动了深海重击！[ {enemy} ] 被眩晕并受到额外伤害！");
            DamageToEnemy(character, enemy, ctx.DamageType, ctx.MagicType, 额外伤害, new(character)
            {
                TriggerEffects = false
            });
            Effect stun = new 眩晕(Skill, character, true, 眩晕时间, 0)
            {
                RemainDuration = 眩晕时间
            };
            enemy.Effects.Add(stun);
            stun.OnEffectGained(new HookContext(GamingQueue, enemy));
        }
    }
}
