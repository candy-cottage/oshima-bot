using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 雷神脚 : Skill
    {
        public override long Id => (long)SkillID.雷神脚;
        public override string Name => "雷神脚";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 75;
        public override double CD => 28;
        public override double HardnessTime { get; set; } = 9;
        public override int CanSelectTargetCount => 4;

        public 雷神脚(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 5;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 55, 50, 0.085, 0.04, DamageType.Physical));
        }
    }
}
