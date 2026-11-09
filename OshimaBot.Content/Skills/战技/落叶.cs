using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 落叶 : Skill
    {
        public override long Id => (long)SkillID.落叶;
        public override string Name => "落叶";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).ExemptionDescription : "";
        public override double EPCost => 55;
        public override double CD => 20;
        public override double HardnessTime { get; set; } = 7;

        public 落叶(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 4;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 50, 45, 0.08, 0.04, DamageType.Physical));
            Effects.Add(new 施加概率负面(this, EffectType.Delay, true, 10, 0, 1.5, 0.85, 0.03, 0.3));
        }
    }
}
