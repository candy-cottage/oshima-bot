using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    public class 时雨标记 : Effect
    {
        public override long Id => (long)PassiveEffectID.时雨标记;
        public override string Name => "时雨标记";
        public override string Description => $"此角色持有时雨标记。来自：[ {Source} ]";
        public override EffectType EffectType => EffectType.Mark;
        public override bool IsDebuff => true;
        public override Character Source => _sourceCharacter;
        public override DispelledType DispelledType { get; set; } = DispelledType.Weak;

        private readonly Character _sourceCharacter;

        public 时雨标记(Skill skill, Character sourceCharacter) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _sourceCharacter = sourceCharacter;
        }

        public override void OnTurnStart(TurnContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (GamingQueue is null)
            {
                return;
            }
            List<Character> enemies = GamingQueue.GetEnemies(character);
            if (enemies.Contains(Source) && Random.NextDouble() < 0.65)
            {
                WriteLine($"[ {character} ] 受到了{nameof(时雨标记)}的影响，陷入了混乱！！！");
                Effect e = new 混乱(Skill, character, false, 0, 1);
                character.Effects.Add(e);
                e.OnEffectGained(new HookContext(GamingQueue, character));
            }
        }

        public override AlterActualDamageResult AlterActualDamageAfterCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character) return default;
            if (ctx.Enemy is not Character enemy) return default;
            double damage = ctx.Damage;
            if (GamingQueue is null)
            {
                return default;

            }
            List<Character> teammates = GamingQueue.GetTeammates(character);
            if ((character == Source || teammates.Contains(Source)) && character.Effects.Any(e => e is 时雨标记) && enemy.Effects.Any(e => e is 时雨标记))
            {
                double bonus = damage * 0.25;
                WriteLine($"[ {character} ] 受到了{nameof(时雨标记)}的影响，伤害提高了 {bonus:0.##} 点！");
                return new AlterActualDamageResult { DamageDelta = bonus };

            }
            return default;

        }
    }
}
