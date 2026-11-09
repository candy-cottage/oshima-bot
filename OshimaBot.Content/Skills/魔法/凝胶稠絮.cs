using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 凝胶稠絮 : Skill
    {
        public override long Id => (long)MagicID.凝胶稠絮;
        public override string Name => "凝胶稠絮";
        public override string Description => Effects.Count > 0 ? string.Join("\r\n", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 造成虚弱).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 造成虚弱).ExemptionDescription : "";
        public override double MPCost => Level > 0 ? 75 + (75 * (Level - 1)) : 75;
        // 调整：L8 CD 19.5 → 20.5（魔法下限 20）
        public override double CD => Level > 0 ? 31 - (1.5 * (Level - 1)) : 31;
        public override double CastTime => 11;
        public override double HardnessTime { get; set; } = 4;
        public override int CanSelectTargetCount => 1;
        public override double MagicBottleneck => 15 + 16 * (Level - 1);

        public 凝胶稠絮(Character? character = null) : base(SkillType.Magic, character)
        {
            Effects.Add(new 造成虚弱(this, false, 0, 3, 0, 0.08, 0.5, 0.05, 0.1,
                0.025, 0.1, 0.015, 0.03));
        }
    }
}
