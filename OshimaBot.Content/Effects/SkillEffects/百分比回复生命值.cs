using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Effects.SkillEffects
{
    public class 百分比回复生命值 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"为{Skill.TargetDescription()}回复 {百分比 * 100:0.##}% 最大生命值。{(CanRespawn ? "如果目标已死亡，将复活目标。" : "")}";

        private double 基础回复 { get; set; } = 0.03;
        private double 回复成长 { get; set; } = 0.03;
        private double 百分比 => Skill.Level > 0 ? 基础回复 + 回复成长 * (Skill.Level - 1) : 基础回复;
        private bool CanRespawn { get; set; } = false;

        public 百分比回复生命值(Skill skill, double 基础回复, double 回复成长, bool canRespawn = false) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            this.基础回复 = 基础回复;
            this.回复成长 = 回复成长;
            CanRespawn = canRespawn;
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character target in targets)
            {
                double heal = 百分比 * target.MaxHP;
                HealToTarget(caster, target, heal, CanRespawn);
            }
        }
    }
}
