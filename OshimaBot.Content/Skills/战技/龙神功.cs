using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 龙神功 : Skill
    {
        public override long Id => (long)SkillID.龙神功;
        public override string Name => "龙神功";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 50;
        public override double CD => 40;
        public override double HardnessTime { get; set; } = 8;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => false;

        public 龙神功(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 龙神功特效(this));
        }
    }

    public class 龙神功特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"提升自身 {攻击提升 * 100:0.##}% 攻击力、{护甲提升 * 100:0.##}% 物理护甲和{魔抗提升 * 100:0.##}% 魔法抗性，持续 {持续回合} 回合。";
        public override string DispelDescription => "被驱散性：可弱驱散";
        public override EffectType EffectType => EffectType.DamageBoost;
        public override DispelledType DispelledType => DispelledType.Weak;

        private double 攻击提升 => Level > 0 ? 0.25 + 0.05 * (Level - 1) : 0.25;
        private double 护甲提升 => Level > 0 ? 0.25 + 0.05 * (Level - 1) : 0.25;
        private double 魔抗提升 => Level > 0 ? 0.25 + 0.05 * (Level - 1) : 0.25;
        private int 持续回合 => 3;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            WriteLine($"[ {caster} ] 运转龙神功，攻击力、物理护甲和魔法抗性大幅提升了！");
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
            ExDEF2 def = new(Skill, new() { { "exdef", 护甲提升 } }, caster)
            {
                Name = Name,
                Durative = false,
                Duration = 0,
                DurationTurn = 持续回合,
                EffectType = EffectType.DefenseBoost,
                DispelledType = DispelledType.Weak
            };
            caster.Effects.Add(def);
            def.OnEffectGained(new HookContext(GamingQueue, caster));
            GamingQueue?.AddApplyEffects(caster, EffectType.DefenseBoost);
            ExMDF mdf = new(Skill, new() { { "mdftype", 0 }, { "mdfvalue", 魔抗提升 } }, caster)
            {
                Name = Name,
                Durative = false,
                Duration = 0,
                DurationTurn = 持续回合,
                EffectType = EffectType.DefenseBoost,
                DispelledType = DispelledType.Weak
            };
            caster.Effects.Add(mdf);
            mdf.OnEffectGained(new HookContext(GamingQueue, caster));
            GamingQueue?.AddApplyEffects(caster, EffectType.DefenseBoost);
        }
    }
}
