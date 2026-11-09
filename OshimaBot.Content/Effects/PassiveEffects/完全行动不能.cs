using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    public class 完全行动不能 : Effect
    {
        public override long Id => (long)PassiveEffectID.完全行动不能;
        public override string Name => _name;
        public override string Description => $"此角色被{Name}了，不能行动。来自：[ {Source} ] 的 [ {Skill.Name} ]";
        public override EffectType EffectType => _type;
        public override DispelledType DispelledType => DispelledType.Strong;
        public override bool IsDebuff => true;
        public override Character Source => _sourceCharacter;
        public override bool Durative => _durative;
        public override double Duration => _duration;
        public override int DurationTurn => _durationTurn;
        public override bool ExemptDuration => true;

        private readonly string _name;
        private readonly EffectType _type;
        private readonly Character _sourceCharacter;
        private readonly bool _durative;
        private readonly double _duration;
        private readonly int _durationTurn;

        public 完全行动不能(string name, EffectType type, Skill skill, Character sourceCharacter, bool durative = false, double duration = 0, int durationTurn = 1) : base(skill)
        {
            _name = name;
            _type = type;
            GamingQueue = skill.GamingQueue;
            _sourceCharacter = sourceCharacter;
            _durative = durative;
            _duration = duration;
            _durationTurn = durationTurn;
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
            AddEffectStatesToCharacter(character, [CharacterState.NotActionable]);
            InterruptCasting(character, Source);
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            RemoveEffectStatesFromCharacter(character);
        }
    }
}
