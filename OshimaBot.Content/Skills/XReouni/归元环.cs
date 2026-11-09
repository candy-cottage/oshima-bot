using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    public class 归元环 : Skill
    {
        public override long Id => (long)SuperSkillID.归元环;
        public override string Name => "归元环";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 100;
        public override double CD => 60;
        public override double HardnessTime { get; set; } = 8;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;

        public 归元环(Character? character = null) : base(SkillType.SuperSkill, character)
        {
            Effects.Add(new 归元环特效(this));
        }
    }

    public class 归元环特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"{Duration:0.##} {GameplayEquilibriumConstant.InGameTime}内，造成伤害必定暴击；使 [ 八卦阵 ] 不需要检定，直接产生偶数效果；并且每次触发偶数效果时，减少所有主动技能 {冷却时间减少 * 100:0.##}% 冷却时间。";
        public override bool Durative => true;
        public override double Duration => 30;
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        public const double 冷却时间减少 = 0.1;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (character.Effects.Where(e => e is 八卦阵特效 && e.Skill.Character == character).FirstOrDefault() is 八卦阵特效 e)
            {
                e.归元 = true;
            }
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (character.Effects.Where(e => e is 八卦阵特效 && e.Skill.Character == character).FirstOrDefault() is 八卦阵特效 e)
            {
                e.归元 = false;
            }
        }

        public override BeforeCriticalCheckResult BeforeCriticalCheck(DamageContext ctx)
        {
            if (ctx.Trigger is not Character actor || ctx.Enemy is not Character enemy) return default;
            bool isNormalAttack = ctx.IsNormalAttack;
            if (actor == Skill.Character)
            {
                // 归元期间必定暴击：提供极高的暴击检定加值
                return new BeforeCriticalCheckResult { ThrowingBonusDelta = 200 };
            }
            return default;
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            Dictionary<string, object> others = ctx.Others;
            RemainDuration = Duration;
            if (!caster.Effects.Contains(this))
            {
                caster.Effects.Add(this);
                OnEffectGained(new HookContext(GamingQueue, caster));
            }
            RecordCharacterApplyEffects(caster, EffectType.CritBoost, EffectType.DamageBoost, EffectType.DefenseBoost);
        }
    }
}
