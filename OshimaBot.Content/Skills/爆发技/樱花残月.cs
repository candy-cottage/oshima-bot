using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.PrefabricatedEntity;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 樱花残月 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.樱花残月;
        public override string Name => "樱花残月";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double CD => 60;
        public override double HardnessTime { get; set; } = 8;

        public 樱花残月(Character? character = null) : base(character)
        {
            Effects.Add(new 樱花残月特效(this));
        }
    }

    public class 樱花残月特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"基于{Skill.SkillOwner()}的 {ATKCoefficient * 100:0.##}% 攻击力 [ {Damage:0.##} ] 对{Skill.TargetDescription()}造成{CharacterSet.GetDamageTypeName(DamageType.Physical)}" +
            (Improvement > 0 ? $"，灵魂绑定伤害加成： {Improvement * 100:0.##}% [ {ImprovementDamage:0.##} ] 点，总伤害 {Damage + ImprovementDamage:0.##} 点；" : "；") +
            $"造成伤害后，自身行动速度提升 {ActualActionSpeedPercent * 100:0.##}%、加速系数提升 {AccelerationPercent * 100:0.##}%，且普通攻击和所有技能的硬直时间减少 {硬直减少:0.##} {GameplayEquilibriumConstant.InGameTime}，持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。";

        public double ATKCoefficient => 0.6 + 0.12 * (Skill.Level - 1);
        public double Damage => (Skill.Character?.ATK ?? 0) * ATKCoefficient;
        public double ImprovementDamage => Improvement > 0 ? Damage * Improvement : 0;
        public double 持续时间 => 10 + 2 * (Skill.Level - 1);
        public double 硬直减少 => 1.5 + 0.5 * (Skill.Level - 1);
        public double ActionSpeedPercent => 0.12 + 0.02 * (Skill.Level - 1);
        public double ActualActionSpeedPercent => ActionSpeedPercent;
        public double AccelerationPercent => 0.1 + 0.02 * (Skill.Level - 1);

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character target in targets)
            {
                DamageCalculationOptions options = new(caster);
                DamageToEnemy(caster, target, DamageType.Physical, MagicType.None, Damage + ImprovementDamage, options);
            }
            // 刷新自身残月之姿 buff（不叠加）
            List<Effect> olds = caster.Effects.Where(e => e is DynamicsEffect && e.Name == nameof(樱花残月) + "·残月之姿").ToList();
            foreach (Effect e in olds)
            {
                e.RemoveFromCharacter(caster);
            }
            Effect buff = new DynamicsEffect(Skill, new Dictionary<string, object>()
            {
                { "exspd", caster.SPD * ActionSpeedPercent },
                { "exacc", AccelerationPercent },
                { "shtr", 硬直减少 },
                { "nahtr", 硬直减少 }
            }, caster)
            {
                Name = nameof(樱花残月) + "·残月之姿",
                Durative = true,
                Duration = 持续时间
            };
            buff.AddToCharacter(caster);
            WriteLine($"[ {caster} ] 发动了樱花残月！自身行动速度与加速系数提升，硬直时间减少，持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}！");
            RecordCharacterApplyEffects(caster, EffectType.Haste);
        }
    }
}
