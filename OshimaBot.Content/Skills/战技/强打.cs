using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 强打 : Skill
    {
        public override long Id => (long)SkillID.强打;
        public override string Name => "强打";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).ExemptionDescription : "";
        public override double EPCost => 65;
        public override double CD => 26;
        public override double HardnessTime { get; set; } = 9;

        public 强打(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 4;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 70, 60, 0.095, 0.05, DamageType.Physical));
            Effects.Add(new 施加概率负面(this, EffectType.Bleed, true, 5, 0, 1, 0.4, 0.03, false, 10.0, 0.0, 5.0));
        }
    }
}
