using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 雷索吸缚 : Skill
    {
        public override long Id => (long)SkillID.雷索吸缚;
        public override string Name => "雷索吸缚";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => "被驱散性：愤怒需强驱散，气绝需强驱散";
        public override double EPCost => 55;
        public override double CD => 28;
        public override double HardnessTime { get; set; } = 9;

        public 雷索吸缚(Character? character = null) : base(SkillType.Skill, character)
        {
            ExemptionDescription = $"愤怒{SkillSet.GetExemptionDescription(EffectType.Taunt)}\r\n气绝{SkillSet.GetExemptionDescription(EffectType.Bleed)}";
            CastRange = 4;
            Effects.Add(new 施加概率负面(this, EffectType.Taunt, false, 0, 2, 0, 1, 0));
            Effects.Add(new 施加概率负面(this, EffectType.Bleed, true, 6, 0, 1.5, 0.3, 0.03, false, 10.0, 0.0, 6.0));
        }
    }
}
