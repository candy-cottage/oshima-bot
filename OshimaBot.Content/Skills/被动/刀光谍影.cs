using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Skills
{
    public class 刀光谍影 : Skill
    {
        public override long Id => (long)PassiveID.刀光谍影;
        public override string Name => "刀光谍影";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 刀光谍影(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 刀光谍影特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 刀光谍影特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"造成伤害后，有 {触发概率 * 100:0.##}% 概率进入不可选中状态（免疫物理与魔法伤害、无法被普通攻击和技能选中），持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。";

        private double 触发概率 => Skill.Character != null ? 0.2 + Skill.Character.Level * 0.002 : 0.2;
        private double 持续时间 => Skill.Character != null ? 2 + Skill.Character.Level * 0.05 : 2;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (Random.NextDouble() > 触发概率) return;
            if (character.Effects.Any(e => e is 完全免疫 && ReferenceEquals(e.Skill, Skill))) return;
            WriteLine($"[ {character} ] 发动了刀光谍影，进入了不可选中状态！");
            Effect e = new 完全免疫(Skill, character, true, 持续时间, 0)
            {
                RemainDuration = 持续时间
            };
            character.Effects.Add(e);
            e.OnEffectGained(new HookContext(GamingQueue, character));
        }
    }
}
