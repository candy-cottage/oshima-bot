using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 拘束之鞭 : Skill
    {
        public override long Id => (long)SkillID.拘束之鞭;
        public override string Name => "拘束之鞭";
        public override string Description => string.Join("", Effects.Select(e => e.Description));
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 打断施法).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 打断施法).ExemptionDescription : "";
        public override double EPCost => 60;
        public override double CD => 22;
        public override double HardnessTime { get; set; } = 7;

        public 拘束之鞭(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 60, 50, 0.09, 0.04, DamageType.Physical));
            Effects.Add(new 打断施法(this));
        }
    }
}
