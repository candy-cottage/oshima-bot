using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 无尽剑制 : Skill
    {
        public override long Id => (long)SkillID.无尽剑制;
        public override string Name => "无尽剑制";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override double EPCost => 90;
        public override double CD => 40;
        // 调整：硬直 12 → 10（战技区间 7–10）
        public override double HardnessTime { get; set; } = 10;
        public override bool SelectAllEnemies => true;

        public 无尽剑制(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 55, 50, 0.08, 0.04, DamageType.Physical));
        }
    }
}
