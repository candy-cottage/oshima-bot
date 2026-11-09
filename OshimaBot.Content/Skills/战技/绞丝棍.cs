using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 绞丝棍 : Skill
    {
        public override long Id => (long)SkillID.绞丝棍;
        public override string Name => "绞丝棍";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 45;
        // 平衡调整（2026-09-22）：CD 12 → 20（战技区间 18–45）
        public override double CD => 20;
        public override double HardnessTime { get; set; } = 7;

        public 绞丝棍(Character? character = null) : base(SkillType.Skill, character)
        {
            // 调整：L6 基础值 425→385、系数 35%→32%，实测 1.28×普攻 压至 ≤1.20
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 70, 63, 0.07, 0.05, DamageType.Physical));
        }
    }
}
