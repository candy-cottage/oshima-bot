using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 精准射击 : Skill
    {
        public override long Id => (long)SkillID.精准射击;
        public override string Name => "精准射击";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 65;
        public override double CD => 24;
        public override double HardnessTime { get; set; } = 8;

        public 精准射击(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 70, 55, 0.095, 0.045, DamageType.Physical));
            Effects.Add(new 打断施法(this));
        }
    }
}
