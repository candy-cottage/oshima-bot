using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;

namespace OshimaBot.Content.Skills
{
    public class 骸骨镀层 : Skill
    {
        public override long Id => (long)PassiveID.骸骨镀层;
        public override string Name => "骸骨镀层";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 骸骨镀层(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 骸骨镀层特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 骸骨镀层特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"承受敌方技能伤害后，在 {保护窗口:0.##} {GameplayEquilibriumConstant.InGameTime}内，后续 {减伤次数} 次普通攻击或技能造成的伤害减少 {减伤比例 * 100:0.##}%。";

        private int 剩余次数 = 0;
        private double 剩余窗口 = 0;

        private double 保护窗口 => Skill.Character != null ? 4 + Skill.Character.Level * 0.05 : 4;
        private int 减伤次数 => 3;
        private double 减伤比例 => Skill.Character != null ? 0.25 + Skill.Character.Level * 0.001 : 0.25;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character attacker || ctx.Enemy is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (attacker == character) return;
            if (ctx.ActualDamage <= 0) return;
            if (ctx.IsNormalAttack)
            {
                // 后续普攻/技能伤害减少：属于保护层
                if (剩余次数 > 0 && 剩余窗口 > 0)
                {
                    剩余次数--;
                    if (剩余次数 <= 0) 剩余窗口 = 0;
                }
            }
            else
            {
                // 敌方技能伤害：激活骸骨镀层
                剩余次数 = 减伤次数;
                剩余窗口 = 保护窗口;
                WriteLine($"[ {character} ] 激活了骸骨镀层！接下来的 {减伤次数} 次攻击伤害减少 {减伤比例 * 100:0.##}%。");
            }
        }

        public override AlterActualDamageResult AlterActualDamageAfterCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character attacker || ctx.Enemy is not Character character) return default;
            if (Skill.Character == null || Skill.Character != character) return default;
            if (attacker == character) return default;
            if (剩余次数 <= 0 || 剩余窗口 <= 0) return default;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return default;
            double reduce = ctx.Damage * 减伤比例;
            WriteLine($"[ {character} ] 的骸骨镀层减少了 {reduce:0.##} 点伤害！");
            return new AlterActualDamageResult { DamageDelta = -reduce };
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (剩余窗口 > 0)
            {
                剩余窗口 -= ctx.Elapsed;
                if (剩余窗口 <= 0)
                {
                    剩余次数 = 0;
                }
            }
        }
    }
}
