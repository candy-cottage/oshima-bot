using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 狐媚暗随 : Skill
    {
        public override long Id => (long)SkillID.狐媚暗随;
        public override string Name => "狐媚暗随";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 55;
        public override double CD => 22;
        public override double HardnessTime { get; set; } = 7;

        public 狐媚暗随(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 4;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 75, 65, 0.1, 0.05, DamageType.Physical));
        }
    }
}
