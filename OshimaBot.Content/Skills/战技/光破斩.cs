using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 光破斩 : Skill
    {
        public override long Id => (long)SkillID.光破斩;
        public override string Name => "光破斩";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 65;
        public override double CD => 24;
        public override double HardnessTime { get; set; } = 8;
        public override int CanSelectTargetCount => 3;

        public 光破斩(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 5;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 60, 55, 0.09, 0.045, DamageType.Physical));
        }
    }
}
