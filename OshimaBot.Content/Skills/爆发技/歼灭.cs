using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.PrefabricatedEntity;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 歼灭 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.歼灭;
        public override string Name => "歼灭";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double CD => 90;
        public override double HardnessTime { get; set; } = 13;
        public override bool SelectAllEnemies => true;

        public 歼灭(Character? character = null) : base(character)
        {
            Effects.Add(new 歼灭特效(this));
        }
    }

    public class 歼灭特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"基于{Skill.SkillOwner()}的 {ATKCoefficient * 100:0.##}% 攻击力 [ {Damage:0.##} ] 对{Skill.TargetDescription()}造成{CharacterSet.GetDamageTypeName(DamageType.Physical)}" +
            (Improvement > 0 ? $"，灵魂绑定伤害加成： {Improvement * 100:0.##}% [ {ImprovementDamage:0.##} ] 点，总伤害 {Damage + ImprovementDamage:0.##} 点；" : "；") +
            $"造成伤害后，有 {ActualProbability * 100:0.##}% 概率使目标进入战斗不能 {持续时间} 回合。战斗不能：无法普通攻击和使用技能（魔法、战技和爆发技）。";

        public double ATKCoefficient => 0.55 + 0.12 * (Skill.Level - 1);
        public double Damage => (Skill.Character?.ATK ?? 0) * ATKCoefficient;
        public double ImprovementDamage => Improvement > 0 ? Damage * Improvement : 0;
        // 重标：全敌强控持续限定 1 回合（原 L5+ 为 2 回合）
        public int 持续时间 => 1;
        public double 概率 => 0.15 + 0.03 * (Skill.Level - 1);
        public double ActualProbability => Math.Min(0.4, 概率);

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
            Effect e = new 施加概率负面(Skill, EffectType.Cripple, false, 0, 持续时间, 0, ActualProbability, 0);
            e.Activate(caster, valid, grids, others);
        }
    }
}
