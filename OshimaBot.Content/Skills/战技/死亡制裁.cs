using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 死亡制裁 : Skill
    {
        public override long Id => (long)SkillID.死亡制裁;
        public override string Name => "死亡制裁";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 80;
        public override double CD => 30;
        public override double HardnessTime { get; set; } = 10;
        public override int CanSelectTargetCount => 4;

        public 死亡制裁(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 5;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 50, 50, 0.08, 0.04, DamageType.Physical));
        }
    }
}
