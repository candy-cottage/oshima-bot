using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 弓刃交错 : Skill
    {
        public override long Id => (long)SkillID.弓刃交错;
        public override string Name => "弓刃交错";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).ExemptionDescription : "";
        public override double EPCost => 65;
        public override double CD => 22;
        public override double HardnessTime { get; set; } = 8;

        public 弓刃交错(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 5;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 60, 55, 0.09, 0.04, DamageType.Physical));
            Effects.Add(new 施加概率负面(this, EffectType.Delay, true, 8, 0, 1.5, 0.8, 0.03, 0.3));
        }
    }
}
