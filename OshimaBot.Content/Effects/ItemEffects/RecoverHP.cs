using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.ItemEffects
{
    public class RecoverHP : Effect
    {
        public override long Id => (long)EffectID.RecoverHP;
        public override string Name => "立即回复生命值";
        public override string Description => $"立即回复{Skill.TargetDescription()} {实际回复:0.##} 点生命值（{(能复活 ? "" : "不")}可用于复活）。" + (Source != null && (Skill.Character != Source || Skill is not OpenSkill) ? $"来自：[ {Source} ]" + (Skill.Item != null ? $" 的 [ {Skill.Item.Name} ]" : (Skill is OpenSkill ? "" : $" 的 [ {Skill.Name} ]")) : "");
        public override EffectType EffectType { get; set; } = EffectType.Item;

        private readonly double 实际回复 = 0;
        private readonly bool 能复活 = false;

        public RecoverHP(Skill skill, Dictionary<string, object> args, Character? source = null) : base(skill, args)
        {
            GamingQueue = skill.GamingQueue;
            Source = source;
            if (Values.Count > 0)
            {
                string key = Values.Keys.FirstOrDefault(s => s.Equals("hp", StringComparison.CurrentCultureIgnoreCase)) ?? "";
                if (key.Length > 0 && double.TryParse(Values[key].ToString(), out double hp) && hp > 0)
                {
                    实际回复 = hp;
                }
                key = Values.Keys.FirstOrDefault(s => s.Equals("respawn", StringComparison.CurrentCultureIgnoreCase)) ?? "";
                if (key.Length > 0 && bool.TryParse(Values[key].ToString(), out bool respawn) && respawn)
                {
                    能复活 = respawn;
                }
            }
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character target in targets)
            {
                HealToTarget(caster, target, 实际回复, 能复活);
            }
        }

        public override void OnSkillCastedOutside(SkillCastContext ctx)
        {
            if (ctx.User is not User user) return;
            List<Character> targets = ctx.Targets;
            base.OnSkillCastedOutside(ctx);
            foreach (Character target in targets)
            {
                target.HP += 实际回复;
            }
        }
    }
}
