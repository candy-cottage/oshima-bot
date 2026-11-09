using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 岩石之息 : Skill
    {
        public override long Id => (long)MagicID.岩石之息;
        public override string Name => "岩石之息";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override double MPCost => Level > 0 ? 40 + (35 * (Level - 1)) : 40;
        public override double CD => 32;
        public override double CastTime => 9;
        public override double HardnessTime { get; set; } = 5;
        public override int CanSelectTargetCount => 3;
        public override SkillRangeType SkillRangeType => SkillRangeType.LinePass;
        public override int CanSelectTargetRange => 2;
        public override double MagicBottleneck => 12 + 13 * (Level - 1);

        public 岩石之息(Character? character = null) : base(SkillType.Magic, character)
        {
            IsNonDirectional = true;
            Effects.Add(new 基于属性的伤害(this, PrimaryAttribute.STR, 30, 10, 0.30, 0.2));
        }
    }
}
