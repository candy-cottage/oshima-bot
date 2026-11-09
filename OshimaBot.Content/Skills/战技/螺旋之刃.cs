using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Skills
{
    public class 螺旋之刃 : Skill
    {
        public override long Id => (long)SkillID.螺旋之刃;
        public override string Name => "螺旋之刃";
        public override string Description => Effects.Count > 0 ? string.Join("", Effects.Select(e => e.Description)) : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First(e => e is 螺旋之刃迟滞特效).DispelDescription : "";
        public override string ExemptionDescription => Effects.Count > 0 ? Effects.First(e => e is 螺旋之刃迟滞特效).ExemptionDescription : "";
        public override double EPCost => 40;
        public override double CD => 25;
        public override double HardnessTime { get; set; } = 6;

        public 螺旋之刃(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 4;
            Effects.Add(new 螺旋之刃迟滞特效(this));
        }
    }

    public class 螺旋之刃迟滞特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"必定使{Skill.TargetDescription()}的普通攻击和技能的硬直时间、当前行动等待时间延长 {基础迟滞 * 100:0.##}%；" +
            $"{额外概率 * 100:0.##}% 概率额外再延长 {额外迟滞 * 100:0.##}%（合计最高 {(基础迟滞 + 额外迟滞) * 100:0.##}%）。持续时间：{持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。";
        public override string DispelDescription => "被驱散性：需弱驱散";
        public override string ExemptionDescription => $"迟滞{SkillSet.GetExemptionDescription(EffectType.Slow)}";
        public override EffectType EffectType => EffectType.Slow;
        public override DispelledType DispelledType => DispelledType.Weak;
        public override bool IsDebuff => true;
        public override bool ExemptDuration => true;

        /// <summary>
        /// 必定命中的迟滞比例：10% + 2%/级（Lv1 10% → Lv6 20%）
        /// </summary>
        private double 基础迟滞 => Level > 0 ? 0.1 + 0.02 * (Level - 1) : 0.1;

        /// <summary>
        /// 额外迟滞的触发概率：20% + 5%/级（Lv1 20% → Lv6 45%）
        /// </summary>
        private double 额外概率 => Level > 0 ? 0.2 + 0.05 * (Level - 1) : 0.2;

        /// <summary>
        /// 额外迟滞的固定比例：20%（Lv6 合计最高 40%）
        /// </summary>
        private double 额外迟滞 => 0.2;

        private double 持续时间 => Level > 0 ? 12 + 2 * (Level - 1) : 12;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            foreach (Character target in ctx.Targets)
            {
                if (target.HP <= 0) continue;

                double percent = 基础迟滞;
                bool extra = Random.NextDouble() < 额外概率;
                if (extra) percent += 额外迟滞;

                迟滞 e = new(Skill, caster, true, 持续时间, 0, percent);
                if (CheckExemption(caster, target, e)) continue;

                WriteLine($"[ {caster} ] 对 [ {target} ] 造成了迟滞！普通攻击和技能的硬直时间、当前行动等待时间延长了 {percent * 100:0.##}%！" +
                    (extra ? $"（额外触发 {额外迟滞 * 100:0.##}%）" : "") +
                    $"持续时间：{持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}！");

                target.Effects.Add(e);
                e.OnEffectGained(new HookContext(GamingQueue, target));
                GamingQueue?.AddApplyEffects(target, e.EffectType);
                e.ApplyChange(target);
            }
        }
    }
}
