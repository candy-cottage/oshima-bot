using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.PrefabricatedEntity;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 圣洁祝福 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.圣洁祝福;
        public override string Name => "圣洁祝福";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        // 调整：CD 55 → 60（爆发技下限 60）
        public override double CD => 60;
        // 调整：硬直 4 → 8（爆发技区间 8–13）
        public override double HardnessTime { get; set; } = 8;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => true;
        public override bool CanSelectSelf => false;

        public 圣洁祝福(Character? character = null) : base(character)
        {
            Effects.Add(new 圣洁祝福特效(this));
        }
    }

    public class 圣洁祝福特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"对{Skill.TargetDescription()}施加强驱散，并基于{(Skill.Character != null ? CharacterSet.GetPrimaryAttributeName(Skill.Character.PrimaryAttribute) : "核心属性")}的 {PACoefficient * 100:0.##}% 治疗目标，回复 {Heal:0.##} 点生命值，并回复 {EnergyRecovery:0.##} 点能量值。" +
            (Improvement > 0 ? $"灵魂绑定额外效果：治疗和能量回复提升 {Improvement * 100:0.##}%。" : "");

        public double PACoefficient => 0.9 + 0.45 * (Skill.Level - 1);
        public double Heal => (Skill.Character?.PrimaryAttributeValue ?? 0) * PACoefficient;
        public double EnergyRecovery => 15 + 10 * (Skill.Level - 1);
        public double ActualHeal => Heal * (Improvement > 0 ? 1 + Improvement : 1);
        public double ActualEnergyRecovery => EnergyRecovery * (Improvement > 0 ? 1 + Improvement : 1);

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            Dictionary<string, object> others = ctx.Others;
            foreach (Character target in targets)
            {
                if (target.HP <= 0) continue;
                Effect 驱散 = new 强驱散特效(Skill);
                驱散.Activate(caster, [target], grids, others);
                HealToTarget(caster, target, ActualHeal);
                double recovery = ActualEnergyRecovery;
                if (recovery > 0)
                {
                    target.EP += recovery;
                    WriteLine($"[ {caster} ] 为 [ {target} ] 施加了圣洁祝福！回复了 {ActualHeal:0.##} 点生命值和 {recovery:0.##} 点能量值！");
                }
            }
        }
    }
}
