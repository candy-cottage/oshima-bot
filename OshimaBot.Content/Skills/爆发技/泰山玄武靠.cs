using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.PrefabricatedEntity;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 泰山玄武靠 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.泰山玄武靠;
        public override string Name => "泰山玄武靠";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double CD => 70;
        public override double HardnessTime { get; set; } = 9;
        public override int CanSelectTargetCount
        {
            get
            {
                return Level switch
                {
                    1 or 2 => 1,
                    3 or 4 => 2,
                    _ => 3
                };
            }
        }

        public 泰山玄武靠(Character? character = null) : base(character)
        {
            Effects.Add(new 泰山玄武靠特效(this));
        }
    }

    public class 泰山玄武靠特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"基于{Skill.SkillOwner()}的 {ATKCoefficient * 100:0.##}% 攻击力 [ {Damage:0.##} ] 对{Skill.TargetDescription()}造成{CharacterSet.GetDamageTypeName(DamageType.Physical)}" +
            (Improvement > 0 ? $"，灵魂绑定伤害加成： {Improvement * 100:0.##}% [ {ImprovementDamage:0.##} ] 点，总伤害 {Damage + ImprovementDamage:0.##} 点；" : "；") +
            $"造成伤害后，有 {ActualProbability * 100:0.##}% 概率使目标进入气绝 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。气绝：行动受限，且每{GameplayEquilibriumConstant.InGameTime}流失 {气绝流失 * 100:0.##}% 当前生命值（不会致死），需强驱散。";

        public double ATKCoefficient => 0.45 + 0.12 * (Skill.Level - 1);
        public double Damage => (Skill.Character?.ATK ?? 0) * ATKCoefficient;
        public double ImprovementDamage => Improvement > 0 ? Damage * Improvement : 0;
        // 重标：气绝持续 15 → 10 秒（L6）
        public double 持续时间 => 5 + 1 * (Skill.Level - 1);
        public double 概率 => 0.2 + 0.05 * (Skill.Level - 1);
        // 重标：强控概率上限 50% → 40%
        public double ActualProbability => Math.Min(0.4, 概率);
        // 重标：当前生命流失 4.5% → 3%（L6）
        public double 气绝流失 => 0.015 + 0.003 * (Skill.Level - 1);

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            Dictionary<string, object> others = ctx.Others;
            List<Character> valid = [];
            foreach (Character target in targets)
            {
                DamageCalculationOptions options = new(caster);
                if (DamageToEnemy(caster, target, DamageType.Physical, MagicType.None, Damage + ImprovementDamage, options).ActualDamage > 0)
                {
                    valid.Add(target);
                }
            }
            Effect e = new 施加概率负面(Skill, EffectType.Bleed, true, 持续时间, 0, 0, ActualProbability, 0, true, 0, 气绝流失);
            e.Activate(caster, valid, grids, others);
        }
    }
}
