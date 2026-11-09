using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;

namespace OshimaBot.Content.Effects.OpenEffects
{
    public class IgnoreEvade : Effect
    {
        public override long Id => (long)EffectID.IgnoreEvade;
        public override string Name { get; set; } = "无视闪避";
        public override string Description => $"普通攻击有 {概率 * 100:0.##}% 概率无视闪避。" + (Source != null && (Skill.Character != Source || Skill is not OpenSkill) ? $"来自：[ {Source} ]" + (Skill.Item != null ? $" 的 [ {Skill.Item.Name} ]" : (Skill is OpenSkill ? "" : $" 的 [ {Skill.Name} ]")) : "");
        public double Value => 概率;

        private readonly double 概率 = 0;

        public override BeforeEvadeCheckResult BeforeEvadeCheck(DamageContext ctx)
        {
            if (ctx.Trigger is not Character actor) return default;
            if (actor == Skill.Character && Random.NextDouble() < 概率)
            {
                if (GamingQueue != null) WriteLine($"[ {actor} ] 的普通攻击无视了 [ {ctx.Enemy} ] 的闪避！");
                return new BeforeEvadeCheckResult { SkipEvadeCheck = true };
            }
            return default;
        }

        public IgnoreEvade(Skill skill, Dictionary<string, object> args, Character? source = null) : base(skill, args)
        {
            EffectType = EffectType.Item;
            GamingQueue = skill.GamingQueue;
            Source = source;
            if (Values.Count > 0)
            {
                string key = Values.Keys.FirstOrDefault(s => s.Equals("p", StringComparison.CurrentCultureIgnoreCase)) ?? "";
                if (key.Length > 0 && double.TryParse(Values[key].ToString(), out double p) && p >= 0 && p <= 1)
                {
                    概率 = p;
                }
            }
        }
    }
}
