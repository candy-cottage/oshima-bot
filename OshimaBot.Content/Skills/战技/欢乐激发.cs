using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 欢乐激发 : Skill
    {
        public override long Id => (long)SkillID.欢乐激发;
        public override string Name => "欢乐激发";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 70;
        public override double CD => 55;
        public override double HardnessTime { get; set; } = 9;
        public override bool CanSelectSelf => true;
        public override bool CanSelectTeammate => true;
        public override bool CanSelectEnemy => false;
        public override int CanSelectTargetCount => 4;

        public 欢乐激发(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 5;
            Effects.Add(new 纯数值回复生命(this, 170, 130));
        }
    }
}
