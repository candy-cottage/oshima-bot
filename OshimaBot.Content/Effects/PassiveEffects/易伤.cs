using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    public class 易伤 : Effect
    {
        public override long Id => (long)PassiveEffectID.易伤;
        public override string Name => "易伤";
        public override string Description => $"此角色处于易伤状态，额外受到 {_exDamagePercent * 100:0.##}% {CharacterSet.GetDamageTypeName(_damageType)}。来自：[ {Source} ] 的 [ {Skill.Name} ]";
        public override EffectType EffectType => EffectType.Vulnerable;
        public override bool IsDebuff => true;
        public override Character Source => _sourceCharacter;
        public override bool Durative => _durative;
        public override double Duration => _duration;
        public override int DurationTurn => _durationTurn;
        public override bool ExemptDuration => true;

        private readonly DamageType _damageType;
        private readonly Character _targetCharacter;
        private readonly Character _sourceCharacter;
        private readonly bool _durative;
        private readonly double _duration;
        private readonly int _durationTurn;
        private readonly double _exDamagePercent;

        public 易伤(Skill skill, Character targetCharacter, Character sourceCharacter, bool durative = false, double duration = 0, int durationTurn = 1,
            DamageType damageType = DamageType.Magical, double exDamagePercent = 0) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _targetCharacter = targetCharacter;
            _sourceCharacter = sourceCharacter;
            _durative = durative;
            _duration = duration;
            _durationTurn = durationTurn;
            _damageType = damageType;
            _exDamagePercent = exDamagePercent;
        }

        public override AlterActualDamageResult AlterActualDamageAfterCalculation(DamageContext ctx)
        {
            if (ctx.Enemy is not Character enemy) return default;
            double damage = ctx.Damage;
            DamageType damageType = ctx.DamageType;
            if (enemy == _targetCharacter && damageType == _damageType)
            {
                return new AlterActualDamageResult { DamageDelta = damage * _exDamagePercent };

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
            AddEffectTypeToCharacter(character, [EffectType.Vulnerable]);
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            RemoveEffectTypesFromCharacter(character);
        }
    }
}
