using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 灾难一掷 : Skill
    {
        public override long Id => (long)SkillID.灾难一掷;
        public override string Name => "灾难一掷";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).ExemptionDescription : "";
        public override double EPCost => 70;
        public override double CD => 28;
        public override double HardnessTime { get; set; } = 9;

        public 灾难一掷(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            // 调整：L6 基础值 405→350（留噪声余量，目标 ≤1.13×普攻）
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 80, 54, 0.11, 0.055, DamageType.Physical));
            // 调整：战斗不能概率 L6 50% → 40%（强控上限 40%）
            Effects.Add(new 施加概率负面(this, EffectType.Cripple, false, 0, 1, 0, 0.30, 0.02));
        }
    }
}
