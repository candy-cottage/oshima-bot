using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 风花阵 : Skill
    {
        public override long Id => (long)SkillID.风花阵;
        public override string Name => "风花阵";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 55;
        public override double CD => 45;
        public override double HardnessTime { get; set; } = 7;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => false;

        public 风花阵(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 风花阵特效(this));
        }
    }

    public class 风花阵特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"进入风花阵：提升自身 {攻击提升 * 100:0.##}% 攻击力，但物理护甲降低 {护甲降低 * 100:0.##}%，持续 {持续回合} 回合。";
        public override string DispelDescription => "被驱散性：可弱驱散";
        public override EffectType EffectType => EffectType.DamageBoost;
        public override DispelledType DispelledType => DispelledType.Weak;

        private double 攻击提升 => Level > 0 ? 0.45 + 0.08 * (Level - 1) : 0.45;
        private double 护甲降低 => Level > 0 ? 0.3 + 0.05 * (Level - 1) : 0.3;
        private int 持续回合 => 3;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            WriteLine($"[ {caster} ] 摆出了风花阵，攻击力提升但物理护甲降低了！");
            ExATK2 atk = new(Skill, new() { { "exatk", 攻击提升 } }, caster)
            {
                Name = Name,
                Durative = false,
                Duration = 0,
                DurationTurn = 持续回合,
                EffectType = EffectType.DamageBoost,
                DispelledType = DispelledType.Weak
            };
            caster.Effects.Add(atk);
            atk.OnEffectGained(new HookContext(GamingQueue, caster));
            GamingQueue?.AddApplyEffects(caster, EffectType.DamageBoost);
            ExDEF2 def = new(Skill, new() { { "exdef", -护甲降低 } }, caster)
            {
                Name = Name,
                Durative = false,
                Duration = 0,
                DurationTurn = 持续回合,
                IsDebuff = true,
                EffectType = EffectType.Weaken,
                DispelledType = DispelledType.Weak
            };
            caster.Effects.Add(def);
            def.OnEffectGained(new HookContext(GamingQueue, caster));
            GamingQueue?.AddApplyEffects(caster, EffectType.Weaken);
        }
    }
}
