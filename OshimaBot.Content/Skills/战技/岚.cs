using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 岚 : Skill
    {
        public override long Id => (long)SkillID.岚;
        public override string Name => "岚";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 80;
        public override double CD => 30;
        public override double HardnessTime { get; set; } = 9;
        public override int CanSelectTargetCount => 4;

        public 岚(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 5;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 45, 45, 0.07, 0.035, DamageType.Physical));
            Effects.Add(new 打断施法(this));
        }
    }
}
