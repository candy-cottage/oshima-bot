using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 千剑之雨 : Skill
    {
        public override long Id => (long)SkillID.千剑之雨;
        public override string Name => "千剑之雨";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 80;
        public override double CD => 32;
        public override double HardnessTime { get; set; } = 10;
        public override int CanSelectTargetCount => 5;

        public 千剑之雨(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 45, 40, 0.07, 0.035, DamageType.Physical));
        }
    }
}
