using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 石化之矢 : Skill
    {
        public override long Id => (long)SkillID.石化之矢;
        public override string Name => "石化之矢";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).ExemptionDescription : "";
        public override double EPCost => 80;
        public override double CD => 35;
        public override double HardnessTime { get; set; } = 10;

        public 石化之矢(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 50, 45, 0.075, 0.04, DamageType.Physical));
            Effects.Add(new 施加概率负面(this, EffectType.Petrify, true, 4, 0, 1, 1, 0));
        }
    }
}
