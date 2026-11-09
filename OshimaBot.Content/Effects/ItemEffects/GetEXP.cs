using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.ItemEffects
{
    public class GetEXP : Effect
    {
        public override long Id => (long)EffectID.GetEXP;
        public override string Name => "立即获得经验值";
        public override string Description => $"{Skill.TargetDescription()}立即获得 {实际获得:0.##} 点经验值。" + (Source != null && (Skill.Character != Source || Skill is not OpenSkill) ? $"来自：[ {Source} ]" + (Skill.Item != null ? $" 的 [ {Skill.Item.Name} ]" : (Skill is OpenSkill ? "" : $" 的 [ {Skill.Name} ]")) : "");
        public override EffectType EffectType { get; set; } = EffectType.Item;

        private readonly double 实际获得 = 0;

        public GetEXP(Skill skill, Dictionary<string, object> args, Character? source = null) : base(skill, args)
        {
            GamingQueue = skill.GamingQueue;
            Source = source;
            if (Values.Count > 0)
            {
                string key = Values.Keys.FirstOrDefault(s => s.Equals("exp", StringComparison.CurrentCultureIgnoreCase)) ?? "";
                if (key.Length > 0 && double.TryParse(Values[key].ToString(), out double exp) && exp > 0)
                {
                    实际获得 = exp;
                }
            }
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character target in targets)
            {
                target.EXP += 实际获得;
                WriteLine($"[ {target} ] 获得了 {实际获得:0.##} 点经验值！");
            }
        }

        public override void OnSkillCastedOutside(SkillCastContext ctx)
        {
            if (ctx.User is not User user) return;
            List<Character> targets = ctx.Targets;
            base.OnSkillCastedOutside(ctx);
            foreach (Character target in targets)
            {
                target.EXP += 实际获得;
            }
        }
    }
}