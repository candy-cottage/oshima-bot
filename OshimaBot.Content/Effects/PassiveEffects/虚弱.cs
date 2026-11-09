using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    public class 虚弱 : Effect
    {
        public override long Id => (long)PassiveEffectID.虚弱;
        public override string Name => "虚弱";
        public override string Description => $"此角色处于虚弱状态，伤害降低 {_damageReductionPercent * 100:0.##}%，" +
            $"物理护甲降低 {_DEFReductionPercent * 100:0.##}%，魔法抗性降低 {_MDFReductionPercent * 100:0.##}%，治疗效果降低 {_healingReductionPercent * 100:0.##}%。来自：[ {Source} ] 的 [ {Skill.Name} ]";
        public override EffectType EffectType => EffectType.Weaken;
        public override DispelledType DispelledType => DispelledType.Weak;
        public override bool IsDebuff => true;
        public override Character Source => _sourceCharacter;
        public override bool Durative => _durative;
        public override double Duration => _duration;
        public override int DurationTurn => _durationTurn;
        public override bool ExemptDuration => true;

        private readonly Character _targetCharacter;
        private readonly Character _sourceCharacter;
        private readonly bool _durative;
        private readonly double _duration;
        private readonly int _durationTurn;
        private readonly double _damageReductionPercent;
        private readonly double _DEFReductionPercent;
        private readonly double _MDFReductionPercent;
        private readonly double _healingReductionPercent;

        public 虚弱(Skill skill, Character targetCharacter, Character sourceCharacter, bool durative = false, double duration = 0, int durationTurn = 1,
            double damageReductionPercent = 0, double DEFReductionPercent = 0, double MDFReductionPercent = 0, double healingReductionPercent = 0) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _targetCharacter = targetCharacter;
            _sourceCharacter = sourceCharacter;
            _durative = durative;
            _duration = duration;
            _durationTurn = durationTurn;
            _damageReductionPercent = damageReductionPercent;
            _DEFReductionPercent = DEFReductionPercent;
            _MDFReductionPercent = MDFReductionPercent;
            _healingReductionPercent = healingReductionPercent;
        }

        public override AlterActualDamageResult AlterActualDamageAfterCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character) return default;
            double damage = ctx.Damage;
            if (character == _targetCharacter)
            {
                return new AlterActualDamageResult { DamageDelta = -(damage * _damageReductionPercent) };

            }
            return default;

        }

        public override AlterHealValueResult AlterHealValueBeforeHealToTarget(HealContext ctx)
        {
            if (ctx.Target is not Character target) return default;
            double heal = ctx.Heal;
            if (target == _targetCharacter)
            {
                return new AlterHealValueResult { HealDelta = -(heal * _healingReductionPercent) };
            }
            return default;
        }

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (_durative && RemainDuration == 0)
            {
                RemainDuration = Duration;
            }
            else if (RemainDurationTurn == 0)
            {
                RemainDurationTurn = DurationTurn;
            }
            character.ExDEFPercentage -= _DEFReductionPercent;
            character.MDF[character.MagicType] -= _MDFReductionPercent;
            AddEffectTypeToCharacter(character, [EffectType.Weaken, EffectType.GrievousWound]);
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            character.ExDEFPercentage += _DEFReductionPercent;
            character.MDF[character.MagicType] += _MDFReductionPercent;
            RemoveEffectTypesFromCharacter(character);
        }
    }
}
