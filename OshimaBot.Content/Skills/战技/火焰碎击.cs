using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 火焰碎击 : Skill
    {
        public override long Id => (long)SkillID.火焰碎击;
        public override string Name => "火焰碎击";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).ExemptionDescription : "";
        public override double EPCost => 75;
        public override double CD => 30;
        public override double HardnessTime { get; set; } = 10;

        public 火焰碎击(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 4;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 80, 60, 0.1, 0.05, DamageType.Physical));
            Effects.Add(new 施加概率负面(this, EffectType.Bleed, true, 6, 0, 1.5, 1, 0, false, 12.0, 0.0, 6.0));
            Effects.Add(new 打断施法(this));
        }
    }
}
