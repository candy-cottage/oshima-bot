using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 冰狱冥嚎 : Skill
    {
        public override long Id => (long)MagicID.冰狱冥嚎;
        public override string Name => "冰狱冥嚎";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override double MPCost => Level > 0 ? 55 + (55 * (Level - 1)) : 55;
        public override double CD => 32;
        public override double CastTime => 10;
        public override double HardnessTime { get; set; } = 5;
        public override int CanSelectTargetCount => 3;
        public override double MagicBottleneck => 20 + 22 * (Level - 1);

        public 冰狱冥嚎(Character? character = null) : base(SkillType.Magic, character)
        {
            Effects.Add(new 基于属性的伤害(this, PrimaryAttribute.INT, 30, 35, 0.2, 0.2));
        }
    }
}
