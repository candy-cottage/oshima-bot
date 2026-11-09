using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.ItemEffects
{
    public class RecoverMP2 : Effect
    {
        public override long Id => (long)EffectID.RecoverMP2;
        public override string Name => "立即回复魔法值";
        public override string Description => $"立即回复{Skill.TargetDescription()} {回复比例 * 100:0.##}% 最大魔法值。" + (Source != null && (Skill.Character != Source || Skill is not OpenSkill) ? $"来自：[ {Source} ]" + (Skill.Item != null ? $" 的 [ {Skill.Item.Name} ]" : (Skill is OpenSkill ? "" : $" 的 [ {Skill.Name} ]")) : "");
        public override EffectType EffectType { get; set; } = EffectType.Item;

        private readonly double 回复比例 = 0;

        public RecoverMP2(Skill skill, Dictionary<string, object> args, Character? source = null) : base(skill, args)
        {
            GamingQueue = skill.GamingQueue;
            Source = source;
            if (Values.Count > 0)
            {
                string key = Values.Keys.FirstOrDefault(s => s.Equals("mp", StringComparison.CurrentCultureIgnoreCase)) ?? "";
                if (key.Length > 0 && double.TryParse(Values[key].ToString(), out double mp) && mp > 0)
                {
                    回复比例 = mp;
                }
            }
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character target in targets)
            {
                double mp = 回复比例 * target.MaxMP;
                target.MP += mp;
                WriteLine($"[ {target} ] 回复了 {mp:0.##} 点魔法值！");
            }
        }

        public override void OnSkillCastedOutside(SkillCastContext ctx)
        {
            if (ctx.User is not User user) return;
            List<Character> targets = ctx.Targets;
            base.OnSkillCastedOutside(ctx);
            foreach (Character target in targets)
            {
                target.MP += 回复比例 * (target?.MaxHP ?? 0);
            }
        }
    }
}
