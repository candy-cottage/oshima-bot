using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Effects.SkillEffects
{
    public class 打断施法 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"对{Skill.TargetDescription()}施加打断施法效果：中断其正在进行的吟唱。";
        public override EffectType EffectType => EffectType.InterruptCasting;

        public 打断施法(Skill skill) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character target in targets)
            {
                if (!CheckExemption(caster, target, this))
                {
                    InterruptCasting(target, caster);
                }
            }
        }
    }
}
