using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 风之鞭 : Skill
    {
        public override long Id => (long)SkillID.风之鞭;
        public override string Name => "风之鞭";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 80;
        public override double CD => 25;
        public override double HardnessTime { get; set; } = 8;
        public override int CanSelectTargetCount => 5;

        public 风之鞭(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 45, 50, 0.09, 0.03, DamageType.Physical));
        }
    }
}
