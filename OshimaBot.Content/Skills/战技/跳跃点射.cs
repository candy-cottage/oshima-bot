using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 跳跃点射 : Skill
    {
        public override long Id => (long)SkillID.跳跃点射;
        public override string Name => "跳跃点射";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 跳跃点射破甲特效).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 跳跃点射破甲特效).ExemptionDescription : "";
        public override double EPCost => 70;
        public override double CD => 26;
        public override double HardnessTime { get; set; } = 9;

        public 跳跃点射(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 65, 55, 0.095, 0.045, DamageType.Physical));
            Effects.Add(new 跳跃点射破甲特效(this));
            Effects.Add(new 打断施法(this));
        }
    }

    public class 跳跃点射破甲特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"使{Skill.TargetDescription()}的物理护甲降低 {破甲比例 * 100:0.##}%，持续 2 回合。";
        public override string DispelDescription => "被驱散性：需强驱散";
        public override string ExemptionDescription => $"物理护甲降低{SkillSet.GetExemptionDescription(EffectType.Weaken)}";
        public override EffectType EffectType => EffectType.Weaken;
        public override DispelledType DispelledType => DispelledType.Strong;
        public override bool IsDebuff => true;

        private double 破甲比例 => Level > 0 ? 0.7 + 0.1 * (Level - 1) : 0.7;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            foreach (Character target in ctx.Targets)
            {
                if (target.HP <= 0) continue;
                WriteLine($"[ {target} ] 的物理护甲降低了 {破甲比例 * 100:0.##}%！");
                ExDEF2 def = new(Skill, new() { { "exdef", -破甲比例 } }, caster)
                {
                    Name = Name,
                    Durative = false,
                    Duration = 0,
                    DurationTurn = 2,
                    IsDebuff = true,
                    EffectType = EffectType.Weaken,
                    DispelledType = DispelledType.Strong
                };
                target.Effects.Add(def);
                def.OnEffectGained(new HookContext(GamingQueue, target));
                GamingQueue?.AddApplyEffects(target, EffectType.Weaken);
            }
        }
    }
}
