using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Effects.OpenEffects
{
    public class SkillHardTimeReduce : Effect
    {
        public override long Id => (long)EffectID.SkillHardTimeReduce;
        public override string Name { get; set; } = "主动技能硬直减少";
        public override string Description => $"减少角色的所有主动技能 {实际硬直时间减少:0.##} {GameplayEquilibriumConstant.InGameTime}硬直时间。" + (Source != null && (Skill.Character != Source || Skill is not OpenSkill) ? $"来自：[ {Source} ]" + (Skill.Item != null ? $" 的 [ {Skill.Item.Name} ]" : (Skill is OpenSkill ? "" : $" 的 [ {Skill.Name} ]")) : "");

        private readonly double 实际硬直时间减少 = 0;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Durative && RemainDuration == 0)
            {
                RemainDuration = Duration;
            }
            else if (RemainDurationTurn == 0)
            {
                RemainDurationTurn = DurationTurn;
            }
            foreach (Skill s in character.Skills)
            {
                s.ExHardnessTime -= 实际硬直时间减少;
            }
            foreach (Skill? s in character.Items.Select(i => i.Skills.Active))
            {
                if (s != null)
                    s.ExHardnessTime -= 实际硬直时间减少;
            }
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            foreach (Skill s in character.Skills)
            {
                s.ExHardnessTime += 实际硬直时间减少;
            }
            foreach (Skill? s in character.Items.Select(i => i.Skills.Active))
            {
                if (s != null)
                    s.ExHardnessTime += 实际硬直时间减少;
            }
        }

        public SkillHardTimeReduce(Skill skill, Dictionary<string, object> args, Character? source = null) : base(skill, args)
        {
            EffectType = EffectType.Item;
            GamingQueue = skill.GamingQueue;
            Source = source;
            if (Values.Count > 0)
            {
                string key = Values.Keys.FirstOrDefault(s => s.Equals("shtr", StringComparison.CurrentCultureIgnoreCase)) ?? "";
                if (key.Length > 0 && double.TryParse(Values[key].ToString(), out double shtr) && shtr > 0)
                {
                    实际硬直时间减少 = shtr;
                }
            }
        }
    }
}
