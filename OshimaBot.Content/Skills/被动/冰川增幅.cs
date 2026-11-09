using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 冰川增幅 : Skill
    {
        public override long Id => (long)PassiveID.冰川增幅;
        public override string Name => "冰川增幅";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 冰川增幅(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 冰川增幅特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 冰川增幅特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"普通攻击或技能命中敌人时，减少其 {行动速度减少:0.##} 点行动速度、{行动系数减少 * 100:0.##}% 行动系数与 {行动系数减少 * 100:0.##}% 加速系数，持续 {减速持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}；减速期间，{Skill.SkillOwner()}对其造成的伤害提升 {伤害提升 * 100:0.##}%。";

        private double 行动速度减少 => Skill.Character != null ? 30 + Skill.Character.Level * 1.5 : 30;
        private double 行动系数减少 => Skill.Character != null ? 0.15 + Skill.Character.Level * 0.002 : 0.15;
        private double 减速持续时间 => Skill.Character != null ? 4 + Skill.Character.Level * 0.05 : 4;
        private double 伤害提升 => Skill.Character != null ? 0.08 + Skill.Character.Level * 0.001 : 0.08;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            // 刷新减速（先移除旧减速，再上新的）
            List<Effect> olds = enemy.Effects.Where(e => e is DynamicsEffect && e.Name == nameof(冰川增幅) + "·减速").ToList();
            foreach (Effect e in olds)
            {
                enemy.Effects.Remove(e);
                e.OnEffectLost(new HookContext(GamingQueue, enemy));
            }
            WriteLine($"[ {character} ] 发动了冰川增幅！[ {enemy} ] 的行动速度降低了！");
            Effect debuff = new DynamicsEffect(Skill, new Dictionary<string, object>()
            {
                { "exspd", -行动速度减少 },
                { "exac", -行动系数减少 },
                { "exacc", -行动系数减少 }
            }, character)
            {
                Name = nameof(冰川增幅) + "·减速",
                Durative = true,
                Duration = 减速持续时间
            };
            enemy.Effects.Add(debuff);
            debuff.OnEffectGained(new HookContext(GamingQueue, enemy));
            debuff.IsDebuff = true;
        }

        public override double AlterExpectedDamageBeforeCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return 0;
            if (Skill.Character == null || Skill.Character != character) return 0;
            if (enemy.Effects.Any(e => e is DynamicsEffect && e.Name == nameof(冰川增幅) + "·减速" && e.Source == character))
            {
                return ctx.Damage * 伤害提升;
            }
            return 0;
        }
    }
}
