using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 月华掌 : Skill
    {
        public override long Id => (long)SkillID.月华掌;
        public override string Name => "月华掌";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).ExemptionDescription : "";
        public override double EPCost => 70;
        public override double CD => 26;
        public override double HardnessTime { get; set; } = 9;

        public 月华掌(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 4;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 60, 50, 0.09, 0.045, DamageType.Physical));
            Effects.Add(new 施加概率负面(this, EffectType.Confusion, false, 0, 2, 0, 0.3, 0.03));
        }
    }
}
