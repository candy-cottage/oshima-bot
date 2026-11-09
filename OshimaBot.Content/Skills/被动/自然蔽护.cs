using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 自然蔽护 : Skill
    {
        public override long Id => (long)PassiveID.自然蔽护;
        public override string Name => "自然蔽护";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 自然蔽护(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 自然蔽护特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 自然蔽护特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"连续 {启动时间:0.##} {GameplayEquilibriumConstant.InGameTime}未受到伤害时进入自然蔽护：提升 {行动系数提升 * 100:0.##}% 行动系数、{加速系数提升 * 100:0.##}% 加速系数，并令受到的治疗效果提升 {治疗加成 * 100:0.##}%；受到伤害后效果解除。";

        private double 未受击时间 = 0;

        private double 启动时间 => Skill.Character != null ? 6 - Skill.Character.Level * 0.05 : 6;
        private double 行动系数提升 => Skill.Character != null ? 0.15 + Skill.Character.Level * 0.002 : 0.15;
        private double 加速系数提升 => Skill.Character != null ? 0.15 + Skill.Character.Level * 0.002 : 0.15;
        private double 治疗加成 => Skill.Character != null ? 0.12 + Skill.Character.Level * 0.001 : 0.12;
        private bool BuffActive => Skill.Character != null && Skill.Character.Effects.Any(e => e is DynamicsEffect && e.Name == nameof(自然蔽护) + "·遮蔽");

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            未受击时间 += ctx.Elapsed;
            if (未受击时间 >= 启动时间 && !BuffActive)
            {
                WriteLine($"[ {character} ] 进入了自然蔽护状态！");
                Effect e = new DynamicsEffect(Skill, new Dictionary<string, object>()
                {
                    { "exac", 行动系数提升 },
                    { "exacc", 加速系数提升 }
                }, character)
                {
                    Name = nameof(自然蔽护) + "·遮蔽",
                    DurativeWithoutDuration = true
                };
                character.Effects.Add(e);
                e.OnEffectGained(new HookContext(GamingQueue, character));
            }
        }

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character attacker || ctx.Enemy is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (attacker == character || ctx.ActualDamage <= 0) return;
            if (!BuffActive) return;
            List<Effect> olds = character.Effects.Where(e => e is DynamicsEffect && e.Name == nameof(自然蔽护) + "·遮蔽").ToList();
            foreach (Effect e in olds)
            {
                character.Effects.Remove(e);
                e.OnEffectLost(new HookContext(GamingQueue, character));
            }
            未受击时间 = 0;
            WriteLine($"[ {character} ] 受到了伤害，自然蔽护被解除。");
        }

        public override AlterHealValueResult AlterHealValueBeforeHealToTarget(HealContext ctx)
        {
            if (ctx.Trigger is not Character healer) return default;
            Character? target = ctx.Target;
            if (target == null || target != Skill.Character) return default;
            if (!BuffActive) return default;
            double bonus = ctx.Heal * 治疗加成;
            return new AlterHealValueResult { HealDelta = bonus };
        }
    }
}
