using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 霸王疾风 : Skill
    {
        public override long Id => (long)SkillID.霸王疾风;
        public override string Name => "霸王疾风";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 60;
        public override double CD => 22;
        public override double HardnessTime { get; set; } = 8;

        public 霸王疾风(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 4;
            // 调整：L6 基础值 435→385（同时解除「基础值顶格 + 系数顶格」的双顶格）
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 85, 60, 0.12, 0.06, DamageType.Physical));
        }
    }
}
