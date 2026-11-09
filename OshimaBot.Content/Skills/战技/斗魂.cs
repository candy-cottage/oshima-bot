using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 斗魂 : Skill
    {
        public override long Id => (long)SkillID.斗魂;
        public override string Name => "斗魂";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First().ExemptionDescription : "";
        public override double EPCost => 45;
        public override double CD => 30;
        public override double HardnessTime { get; set; } = 8;

        public 斗魂(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 4;
            Effects.Add(new 斗魂特效(this));
        }
    }

    public class 斗魂特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"使{Skill.TargetDescription()}的攻击力降低 {ATKDown * 100:0.##}%、物理护甲降低 {DEFDown * 100:0.##}%，持续 2 回合。";
        public override string DispelDescription => "被驱散性：可弱驱散";
        public override EffectType EffectType => EffectType.Weaken;
        public override DispelledType DispelledType => DispelledType.Weak;
        public override bool IsDebuff => true;

        private double ATKDown => Level > 0 ? 0.15 + 0.05 * (Level - 1) : 0.15;
        private double DEFDown => Level > 0 ? 0.25 + 0.05 * (Level - 1) : 0.25;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            foreach (Character target in ctx.Targets)
            {
                if (target.HP <= 0) continue;
                WriteLine($"[ {target} ] 的攻击力和物理护甲降低了！");
                ExATK2 atk = new(Skill, new() { { "exatk", -ATKDown } }, caster)
                {
                    Name = Name,
                    Durative = false,
                    Duration = 0,
                    DurationTurn = 2,
                    IsDebuff = true,
                    EffectType = EffectType.Weaken,
                    DispelledType = DispelledType.Weak
                };
                target.Effects.Add(atk);
                atk.OnEffectGained(new HookContext(GamingQueue, target));
                GamingQueue?.AddApplyEffects(target, EffectType.Weaken);
                ExDEF2 def = new(Skill, new() { { "exdef", -DEFDown } }, caster)
                {
                    Name = Name,
                    Durative = false,
                    Duration = 0,
                    DurationTurn = 2,
                    IsDebuff = true,
                    EffectType = EffectType.Weaken,
                    DispelledType = DispelledType.Weak
                };
                target.Effects.Add(def);
                def.OnEffectGained(new HookContext(GamingQueue, target));
                GamingQueue?.AddApplyEffects(target, EffectType.Weaken);
            }
        }
    }
}
