using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    public class 石化(Skill skill, Character sourceCharacter, bool durative = false, double duration = 0, int durationTurn = 1) : 完全行动不能(nameof(石化), EffectType.Petrify, skill, sourceCharacter, durative, duration, durationTurn)
    {
        public override long Id => (long)PassiveEffectID.石化;
    }
}
