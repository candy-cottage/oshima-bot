using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 乱心安魂曲 : Skill
    {
        public override long Id => (long)SkillID.乱心安魂曲;
        public override string Name => "乱心安魂曲";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 75;
        public override double CD => 28;
        public override double HardnessTime { get; set; } = 9;
        public override int CanSelectTargetCount => 4;

        public 乱心安魂曲(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 5;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 50, 45, 0.075, 0.04, DamageType.Physical));
        }
    }
}
