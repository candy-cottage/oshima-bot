using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 陀螺舞 : Skill
    {
        public override long Id => (long)SkillID.陀螺舞;
        public override string Name => "陀螺舞";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).ExemptionDescription : "";
        public override double EPCost => 60;
        public override double CD => 45;
        public override double HardnessTime { get; set; } = 10;
        public override bool CanSelectSelf => false;
        public override bool CanSelectTeammate => false;
        public override int CanSelectTargetCount => 3;

        public 陀螺舞(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 4;
            Effects.Add(new 施加概率负面(this, EffectType.Taunt, false, 0, 2, 0, 1, 0));
        }
    }
}
