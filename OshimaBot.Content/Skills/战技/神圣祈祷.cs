using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 神圣祈祷 : Skill
    {
        public override long Id => (long)SkillID.神圣祈祷;
        public override string Name => "神圣祈祷";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 70;
        public override double CD => 60;
        public override double HardnessTime { get; set; } = 8;
        public override bool CanSelectSelf => true;
        public override bool CanSelectTeammate => true;
        public override bool CanSelectEnemy => false;
        public override int CanSelectTargetCount => 2;

        public 神圣祈祷(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 5;
            Effects.Add(new 施加持续性强驱散(this, durative: true, duration: 12, levelGrowth: 3));
            Effects.Add(new 施加概率增益(this, EffectType.HealOverTime, true, 12, 0, 3, 1, 0, false, 10.0, 0.0, 8.0));
        }
    }
}
