using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.PrefabricatedEntity;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 圣星光旋 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.圣星光旋;
        public override string Name => "圣星光旋";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double CD => 100;
        // 调整：硬直 16 → 13（爆发技区间 8–13）
        public override double HardnessTime { get; set; } = 13;
        public override bool SelectAllEnemies => true;

        public 圣星光旋(Character? character = null) : base(character)
        {
            Effects.Add(new 圣星光旋特效(this));
        }
    }

    public class 圣星光旋特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"对{Skill.TargetDescription()}造成 {PACoefficient * 100:0.##}% " +
            $"{(Skill.Character != null ? CharacterSet.GetPrimaryAttributeName(Skill.Character.PrimaryAttribute) : "核心属性")} [ {PADamage:0.##} ] + {GeneralDamage:0.##} 点{CharacterSet.GetDamageTypeName(DamageType.True)}。" +
            (Improvement > 0 ? $"灵魂绑定伤害加成： {Improvement * 100:0.##}% [ {ImprovementDamage:0.##} ] 点，" : "") + $"总伤害 {Damage + ImprovementDamage:0.##} 点。" +
            $"随后，有 25% 概率对受到伤害的目标造成眩晕 1 回合。眩晕：进入完全行动不能状态。";

        // 加强：实测仅 0.62×普攻，低于爆发技下限 0.67 —— L6 核心属性 45%→50%、真伤 180→210
        public double PACoefficient => 0.25 + 0.05 * (Skill.Level - 1);
        public double PADamage => (Skill.Character?.PrimaryAttributeValue ?? 0) * PACoefficient;
        public double GeneralDamage => 35 * Skill.Level;
        public double Damage => GeneralDamage + PADamage;
        public double ImprovementDamage => Improvement > 0 ? Damage * Improvement : 0;

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
                if (DamageToEnemy(caster, target, DamageType.True, MagicType.None, Damage + ImprovementDamage, options).ActualDamage > 0)
                {
                    if (Random.NextDouble() < 0.25)
                    {
                        valid.Add(target);
                    }
                }
            }
            Effect e = new 造成眩晕(Skill);
            e.Activate(caster, valid, grids, others);
        }
    }
}
