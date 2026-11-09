using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 弧形日珥 : Skill
    {
        public override long Id => (long)MagicID.弧形日珥;
        public override string Name => "弧形日珥";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override double MPCost => Level > 0 ? 55 + (55 * (Level - 1)) : 55;
        public override double CD => 35;
        public override double CastTime => 8;
        public override double HardnessTime { get; set; } = 6;
        public override int CanSelectTargetCount => 3;
        public override double MagicBottleneck => 12 + 13 * (Level - 1);

        public 弧形日珥(Character? character = null) : base(SkillType.Magic, character)
        {
            Effects.Add(new 基于属性的伤害(this, PrimaryAttribute.AGI, 45, 25, 0.2, 0.12));
        }
    }
}
