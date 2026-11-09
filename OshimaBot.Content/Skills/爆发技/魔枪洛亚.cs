using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.PrefabricatedEntity;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 魔枪洛亚 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.魔枪洛亚;
        public override string Name => "魔枪洛亚";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double CD => 70;
        public override double HardnessTime { get; set; } = 11;
        public override bool SelectAllEnemies => true;

        public 魔枪洛亚(Character? character = null) : base(character)
        {
            Effects.Add(new 魔枪洛亚特效(this));
        }
    }

    public class 魔枪洛亚特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"基于{Skill.SkillOwner()}的 {ATKCoefficient * 100:0.##}% 攻击力 [ {Damage:0.##} ] 对{Skill.TargetDescription()}造成{CharacterSet.GetDamageTypeName(DamageType.Magical)}" +
            (Improvement > 0 ? $"，灵魂绑定伤害加成： {Improvement * 100:0.##}% [ {ImprovementDamage:0.##} ] 点，总伤害 {Damage + ImprovementDamage:0.##} 点；" : "；") +
            $"造成伤害后，解除目标的施法，并施加魔抗削弱 {持续时间:0.##} 回合：全属性魔法抗性降低 {MDFReductionPercent * 100:0.##}%，可弱驱散。";

        public double ATKCoefficient => 0.42 + 0.1 * (Skill.Level - 1);
        public double Damage => (Skill.Character?.ATK ?? 0) * ATKCoefficient;
        public double ImprovementDamage => Improvement > 0 ? Damage * Improvement : 0;
        public int 持续时间 => Skill.Level >= 4 ? 3 : 2;
        public double MDFReductionPercent => 0.15 + 0.03 * (Skill.Level - 1);

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
                    Effect 打断 = new 打断施法(Skill);
                    打断.Activate(caster, [target], grids, others);
                    魔抗削弱 e = new(Skill, target, caster, 持续时间, MDFReductionPercent);
                    if (!CheckExemption(caster, target, e))
                    {
                        WriteLine($"[ {caster} ] 对 [ {target} ] 施加了魔抗削弱，全属性魔法抗性降低 {MDFReductionPercent * 100:0.##}%，持续 {持续时间} 回合！");
                        e.AddToCharacter(target);
                        GamingQueue?.AddApplyEffects(target, e.EffectType);
                    }
                }
            }
        }
    }

    public class 魔抗削弱 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name + "·魔抗削弱";
        public override string Description => $"此角色的全属性魔法抗性降低 {_mdfReductionPercent * 100:0.##}%。来自：[ {Source} ] 的 [ {Skill.Name} ]";
        public override EffectType EffectType => EffectType.MagicResistBreak;
        public override DispelledType DispelledType => DispelledType.Weak;
        public override bool IsDebuff => true;
        public override bool ExemptDuration => true;
        public override Character Source => _sourceCharacter;
        public override bool Durative => false;
        public override double Duration => 0;
        public override int DurationTurn => _durationTurn;

        private readonly Character _targetCharacter;
        private readonly Character _sourceCharacter;
        private readonly int _durationTurn;
        private readonly double _mdfReductionPercent;
        private readonly Dictionary<MagicType, double> _deltas = [];

        public 魔抗削弱(Skill skill, Character targetCharacter, Character sourceCharacter, int durationTurn, double mdfReductionPercent) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _targetCharacter = targetCharacter;
            _sourceCharacter = sourceCharacter;
            _durationTurn = durationTurn;
            _mdfReductionPercent = mdfReductionPercent;
        }

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (RemainDurationTurn == 0)
            {
                RemainDurationTurn = DurationTurn;
            }
            if (character == _targetCharacter)
            {
                character.MDF.AddAllValue(-_mdfReductionPercent);
            }
            AddEffectTypeToCharacter(character, [EffectType.MagicResistBreak]);
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (character == _targetCharacter)
            {
                character.MDF.AddAllValue(_mdfReductionPercent);
            }
            RemoveEffectTypesFromCharacter(character);
        }
    }
}
