using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    public class 愤怒 : Effect
    {
        public override long Id => (long)PassiveEffectID.愤怒;
        public override string Name => "愤怒";
        public override string Description => $"此角色处于愤怒状态，行动受限且失控，行动回合中无法自主行动，仅能对 [ {_targetCharacter} ] 发起普通攻击。来自：[ {Source} ] 的 [ {Skill.Name} ]";
        public override EffectType EffectType => EffectType.Taunt;
        public override DispelledType DispelledType => DispelledType.Strong;
        public override bool IsDebuff => true;
        public override Character Source => _sourceCharacter;
        public override bool Durative => _durative;
        public override double Duration => _duration;
        public override int DurationTurn => _durationTurn;
        public override bool ExemptDuration => true;

        private readonly Character _sourceCharacter;
        private readonly Character _targetCharacter;
        private readonly bool _durative;
        private readonly double _duration;
        private readonly int _durationTurn;

        public 愤怒(Skill skill, Character sourceCharacter, Character targetCharacter, bool durative = false, double duration = 0, int durationTurn = 1) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _sourceCharacter = sourceCharacter;
            _targetCharacter = targetCharacter;
            _durative = durative;
            _duration = duration;
            _durationTurn = durationTurn;
        }

        public override void AlterSelectListBeforeAction(SelectionContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            List<Character> enemys = ctx.Enemys;
            List<Character> teammates = ctx.Teammates;
            // 为了确保角色能够自动化行动，这里需要将角色设置为可行动
            if (character.CharacterState == CharacterState.ActionRestricted)
            {
                GamingQueue?.SetCharactersToAIControl(true, false, character);
                character.CharacterState = CharacterState.Actionable;
            }
            enemys.Clear();
            teammates.Clear();
            if (_targetCharacter.HP > 0)
            {
                enemys.Add(_targetCharacter);
            }
        }

        public override AlterActionTypeResult AlterActionTypeBeforeAction(DecisionContext ctx)
        {
            if (_targetCharacter.HP > 0)
            {
                // 目标存活：强制倾向普通攻击，禁止使用物品与技能
                return new AlterActionTypeResult { ForceAction = true, PNormalAttack = 1, CanUseItem = false, CanCastSkill = false };
            }
            // 如果目标已死亡，则放弃本回合行动，并在回合结束后自动移除愤怒状态
            RemainDuration = 0;
            RemainDurationTurn = 0;
            return new AlterActionTypeResult { ForceAction = true, ActionType = CharacterActionType.EndTurn };
        }

        public override void AfterDeathCalculation(DeathContext ctx)
        {
            if (ctx.Trigger is not Character death) return;
            if (death == _targetCharacter)
            {
                // 如果目标死亡，则在下次时间流逝时自动移除愤怒状态
                RemainDuration = 0;
                RemainDurationTurn = 0;
            }
        }

        public override void OnTurnEnd(TurnContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            character.UpdateCharacterState();
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
            GamingQueue?.SetCharactersToAIControl(true, false, character);
            AddEffectStatesToCharacter(character, [CharacterState.ActionRestricted]);
            AddEffectTypeToCharacter(character, [EffectType.Taunt]);
            InterruptCasting(character, Source);
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            GamingQueue?.SetCharactersToAIControl(true, true, character);
            RemoveEffectStatesFromCharacter(character);
            RemoveEffectTypesFromCharacter(character);
        }
    }
}
