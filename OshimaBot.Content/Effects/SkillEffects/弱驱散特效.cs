using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Effects.SkillEffects
{
    public class 弱驱散特效 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"弱驱散{Skill.TargetDescription()}。";
        public override DispelType DispelType => DispelType.Weak;

        public 弱驱散特效(Skill skill) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            Dictionary<Character, bool> isTeammateDictionary = GamingQueue?.GetIsTeammateDictionary(caster, targets) ?? [];
            foreach (Character target in targets)
            {
                WriteLine($"[ {caster} ] 弱驱散了 [ {target} ] ！");
                bool isEnemy = true;
                if (isTeammateDictionary.TryGetValue(target, out bool value))
                {
                    isEnemy = !value;
                }
                Dispel(caster, target, isEnemy);
            }
        }
    }
}
