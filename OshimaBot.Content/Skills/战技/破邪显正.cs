using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 破邪显正 : Skill
    {
        public override long Id => (long)SkillID.破邪显正;
        public override string Name => "破邪显正";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 70;
        public override double CD => 26;
        public override double HardnessTime { get; set; } = 9;
        public override int CanSelectTargetCount => 5;

        public 破邪显正(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 40, 40, 0.065, 0.03, DamageType.Physical));
        }
    }
}
