using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 镜花水月 : Skill
    {
        public override long Id => (long)SkillID.镜花水月;
        public override string Name => "镜花水月";
        public override string Description => string.Join("", Effects.Select(e => e.Description));
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加免疫).DispelDescription : "";
        public override double EPCost => 70;
        public override double CD => 40;
        public override double HardnessTime { get; set; } = 10;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => false;

        public 镜花水月(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 施加免疫(this, ImmuneType.All, false, 0, 2));
        }
    }
}
