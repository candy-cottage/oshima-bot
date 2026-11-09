using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.PrefabricatedEntity;

namespace OshimaBot.Content.Skills
{
    public class 八叶灭杀 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.八叶灭杀;
        public override string Name => "八叶灭杀";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double CD => 60;
        public override double HardnessTime { get; set; } = 9;

        public 八叶灭杀(Character? character = null) : base(character)
        {
            Effects.Add(new 八叶灭杀特效(this));
        }
    }

    public class 八叶灭杀特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"基于{Skill.SkillOwner()}的 {ATKCoefficient * 100:0.##}% 攻击力 [ {Damage:0.##} ] 对{Skill.TargetDescription()}造成{CharacterSet.GetDamageTypeName(DamageType.Physical)}" +
            (Improvement > 0 ? $"，灵魂绑定伤害加成： {Improvement * 100:0.##}% [ {ImprovementDamage:0.##} ] 点，总伤害 {Damage + ImprovementDamage:0.##} 点；" : "；") +
            $"造成伤害后，基于目标已损失生命值的 {LostHPCoefficient * 100:0.##}% 再次造成{CharacterSet.GetDamageTypeName(DamageType.True)}额外伤害。";

        // 平衡调整（2026-09-22）：L6 总量由「100% 攻击力 + 12% 已损失生命真伤」降至「85% 攻击力 + 8% 已损失生命真伤」，
        // 对齐爆发技上限「≤0.90–1.50×攻击力」(≈1.11×普攻)。实测原 1.13×普攻（超上限 2%）。
        public double ATKCoefficient => 0.5 + 0.07 * (Skill.Level - 1);
        public double Damage => (Skill.Character?.ATK ?? 0) * ATKCoefficient;
        public double ImprovementDamage => Improvement > 0 ? Damage * Improvement : 0;
        // 重标：已损失生命值真伤系数 45% → 12%（L6）→ 本次再降至 8%
        public double LostHPCoefficient => 0.03 + 0.01 * (Skill.Level - 1);
        public double ExtraTrueDamage(Character target) => Math.Max(0, target.MaxHP - target.HP) * LostHPCoefficient * (Improvement > 0 ? 1 + Improvement : 1);

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character target in targets)
            {
                DamageCalculationOptions options = new(caster);
                DamageToEnemy(caster, target, DamageType.Physical, MagicType.None, Damage + ImprovementDamage, options);
                if (target.HP <= 0) continue;
                double extra = ExtraTrueDamage(target);
                if (extra > 0)
                {
                    WriteLine($"[ {caster} ] 发动了八叶灭杀！基于 [ {target} ] 已损失的生命值再次造成 {extra:0.##} 点{CharacterSet.GetDamageTypeName(DamageType.True)}额外伤害！");
                    DamageToEnemy(caster, target, DamageType.True, MagicType.None, extra, new(caster) { TriggerEffects = false });
                }
            }
        }
    }
}
