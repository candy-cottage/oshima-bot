using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 无相飞刀 : Skill
    {
        public override long Id => (long)SkillID.无相飞刀;
        public override string Name => "无相飞刀";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 施加概率负面).ExemptionDescription : "";
        public override double EPCost => 45;
        public override double CD => 30;
        public override double HardnessTime { get; set; } = 6;

        public 无相飞刀(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 5;
            Effects.Add(new 施加概率负面(this, EffectType.Silence, false, 0, 2, 0, 1, 0));
        }
    }
}
