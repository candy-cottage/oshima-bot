using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 丛刃 : Skill
    {
        public override long Id => (long)PassiveID.丛刃;
        public override string Name => "丛刃";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 丛刃(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 丛刃特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 丛刃特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"每次造成伤害后，在 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}内大幅减少自身的普通攻击与技能硬直时间（每层减少 {每层硬直减少 * 100:0.##}%，最多 {最多层数} 层）。";

        private int 层数 = 0;
        private double 剩余持续时间 = 0;

        private double 每层硬直减少 => Skill.Character != null ? 0.12 + Skill.Character.Level * 0.001 : 0.12;
        private double 持续时间 => Skill.Character != null ? 5 + Skill.Character.Level * 0.05 : 5;
        private int 最多层数 => 3;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            层数 = Math.Min(最多层数, 层数 + 1);
            剩余持续时间 = 持续时间;
            ApplyBuff(character);
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (剩余持续时间 > 0)
            {
                剩余持续时间 -= ctx.Elapsed;
                if (剩余持续时间 <= 0)
                {
                    层数 = 0;
                    ClearBuff(character);
                }
            }
        }

        private void ApplyBuff(Character character)
        {
            ClearBuff(character);
            if (层数 <= 0) return;
            double reduce = 每层硬直减少 * 层数;
            WriteLine($"[ {character} ] 发动了丛刃！{reduce * 100:0.##}% 硬直减免生效，持续 {剩余持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}！");
            Effect e = new DynamicsEffect(Skill, new Dictionary<string, object>()
            {
                { "nahtr2", reduce },
                { "shtr2", reduce }
            }, character)
            {
                Name = nameof(丛刃) + "·硬直减免",
                Durative = true,
                Duration = 剩余持续时间
            };
            character.Effects.Add(e);
            e.OnEffectGained(new HookContext(GamingQueue, character));
        }

        private void ClearBuff(Character character)
        {
            List<Effect> olds = character.Effects.Where(e => e is DynamicsEffect && e.Name == nameof(丛刃) + "·硬直减免").ToList();
            foreach (Effect e in olds)
            {
                character.Effects.Remove(e);
                e.OnEffectLost(new HookContext(GamingQueue, character));
            }
        }
    }
}
