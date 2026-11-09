using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.PrefabricatedEntity;

namespace OshimaBot.Content.Skills
{
    public class 卫星激光 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.卫星激光;
        public override string Name => "卫星激光";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double CD => 65;
        public override double HardnessTime { get; set; } = 10;
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

        public 卫星激光(Character? character = null) : base(character)
        {
            Effects.Add(new 卫星激光特效(this));
        }
    }

    public class 卫星激光特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"基于{Skill.SkillOwner()}的 {ATKCoefficient * 100:0.##}% 攻击力 [ {Damage:0.##} ] 对{Skill.TargetDescription()}造成{CharacterSet.GetDamageTypeName(DamageType.Magical)}" +
            (Improvement > 0 ? $"，灵魂绑定伤害加成： {Improvement * 100:0.##}% [ {ImprovementDamage:0.##} ] 点，总伤害 {Damage + ImprovementDamage:0.##} 点；" : "；") +
            $"造成伤害后，目标每{GameplayEquilibriumConstant.InGameTime}受到 {持续伤害:0.##} 点{CharacterSet.GetDamageTypeName(DamageType.True)}，持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。";

        public double ATKCoefficient => 0.5 + 0.12 * (Skill.Level - 1);
        public double Damage => (Skill.Character?.ATK ?? 0) * ATKCoefficient;
        public double ImprovementDamage => Improvement > 0 ? Damage * Improvement : 0;
        public double 持续伤害 => 20 + 8 * (Skill.Level - 1);
        public double 持续时间 => 8 + 2 * (Skill.Level - 1);

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            Dictionary<string, object> others = ctx.Others;
            foreach (Character target in targets)
            {
                DamageCalculationOptions options = new(caster);
                if (DamageToEnemy(caster, target, DamageType.Magical, MagicType.None, Damage + ImprovementDamage, options).ActualDamage > 0)
                {
                    Effect e = new 施加卫星灼烧(Skill, 持续时间, 持续伤害);
                    if (!CheckExemption(caster, target, e))
                    {
                        e.Activate(caster, [target], grids, others);
                    }
                }
            }
        }
    }

    public class 施加卫星灼烧 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"对{Skill.TargetDescription()}施加灼烧：每{GameplayEquilibriumConstant.InGameTime}受到 {伤害:0.##} 点{CharacterSet.GetDamageTypeName(DamageType.True)}，持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。";
        public override EffectType EffectType => EffectType.Burn;
        public override DispelledType DispelledType => DispelledType.Weak;
        public override bool ExemptDuration => true;

        private readonly double _duration;
        private readonly double 伤害;
        private double 持续时间 => _duration;

        public 施加卫星灼烧(Skill skill, double duration, double damage) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _duration = duration;
            伤害 = damage;
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character target in targets)
            {
                if (target.HP <= 0) continue;
                卫星灼烧 e = new(Skill, target, caster, _duration, 伤害);
                if (!CheckExemption(caster, target, e))
                {
                    WriteLine($"[ {caster} ] 对 [ {target} ] 施加了卫星灼烧！每{GameplayEquilibriumConstant.InGameTime}受到 {伤害:0.##} 点{CharacterSet.GetDamageTypeName(DamageType.True)}，持续 {_duration:0.##} {GameplayEquilibriumConstant.InGameTime}！");
                    e.AddToCharacter(target);
                    GamingQueue?.AddApplyEffects(target, e.EffectType);
                }
            }
        }
    }

    public class 卫星灼烧 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name + "·灼烧";
        public override string Description => $"此角色正受到卫星灼烧，每{GameplayEquilibriumConstant.InGameTime}受到 {伤害:0.##} 点{CharacterSet.GetDamageTypeName(DamageType.True)}。来自：[ {Source} ] 的 [ {Skill.Name} ]";
        public override EffectType EffectType => EffectType.Burn;
        public override DispelledType DispelledType => DispelledType.Weak;
        public override bool IsDebuff => true;
        public override bool ExemptDuration => true;
        public override Character Source => _sourceCharacter;
        public override bool Durative => true;
        public override double Duration => _duration;

        private readonly Character _targetCharacter;
        private readonly Character _sourceCharacter;
        private readonly double _duration;
        private readonly double 伤害;

        public 卫星灼烧(Skill skill, Character targetCharacter, Character sourceCharacter, double duration, double damage) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _targetCharacter = targetCharacter;
            _sourceCharacter = sourceCharacter;
            _duration = duration;
            伤害 = damage;
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (character == _targetCharacter && character.HP > 0)
            {
                DamageToEnemy(Source, character, DamageType.True, MagicType.None, 伤害 * ctx.Elapsed, null);
            }
        }

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (RemainDuration == 0)
            {
                RemainDuration = Duration;
            }
            AddEffectTypeToCharacter(character, [EffectType.Burn]);
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            RemoveEffectTypesFromCharacter(character);
        }
    }
}
