using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    public class 长期监视 : Effect
    {
        public override long Id => (long)PassiveEffectID.长期监视;
        public override string Name => "长期监视";
        public override string Description => $"此角色正在被长期监视。来自：[ {Source} ]";
        public override EffectType EffectType => EffectType.Mark;
        public override bool IsDebuff => true;
        public override bool DurativeWithoutDuration => true;
        public override Character Source => _sourceCharacter;
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        private readonly Character _sourceCharacter;
        private readonly Character _targetCharacter;

        public 长期监视(Skill skill, Character sourceCharacter, Character targetCharacter) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _sourceCharacter = sourceCharacter;
            _targetCharacter = targetCharacter;
        }

        public CharacterActionType LastType { get; set; } = CharacterActionType.None;
        public Skill? LastSkill { get; set; } = null;

        public override void OnCharacterActionStart(ActionContext ctx)
        {
            CharacterActionType type = ctx.ActionType;
            if (type == CharacterActionType.NormalAttack)
            {
                LastType = type;
            }
        }

        public override BeforeSkillCastedOnStatusResult BeforeSkillCastedOnStatus(SkillCastContext ctx)
        {
            LastType = CharacterActionType.CastSkill;
            LastSkill = ctx.Skill;
            return default;
        }

        public override void AfterDeathCalculation(DeathContext ctx)
        {
            if (ctx.Trigger is not Character death) return;
            bool hasMaster = ctx.HasMaster;
            Character? killer = ctx.Killer;
            if (GamingQueue != null && !hasMaster && killer != null && killer == _targetCharacter && Source != null && death != Source && GamingQueue.Queue.Contains(Source))
            {
                WriteLine($"[ {Source} ] 正在观察 [ {killer} ] 的情绪。");
                if (LastType == CharacterActionType.NormalAttack)
                {
                    Source.NormalAttack.SetMagicType(new(Skill.Effects.First(), true, MagicType, 999), GamingQueue);
                    Effect e = new IgnoreEvade(Skill, new()
                    {
                        { "p", 1 }
                    }, Source)
                    {
                        Name = Name,
                        Durative = false,
                        DurationTurn = 3,
                        RemainDurationTurn = 3
                    };
                    e.OnEffectGained(new HookContext(GamingQueue, Source));
                    Source.Effects.Add(e);
                    WriteLine($"[ {Source} ] 获得了无视闪避效果，持续 3 回合！");
                }
                else if (LastType == CharacterActionType.CastSkill && LastSkill != null)
                {
                    复制技能 e = new(Skill, Source, LastSkill)
                    {
                        Durative = false,
                        DurationTurn = 3,
                        RemainDurationTurn = 3
                    };
                    e.CopiedSkill.Values[nameof(时雨标记)] = 1;
                    e.CopiedSkill.CurrentCD = 0;
                    e.CopiedSkill.FreeCostEP = true;
                    e.CopiedSkill.FreeCostMP = true;
                    e.CopiedSkill.Enable = true;
                    e.CopiedSkill.IsInEffect = false;
                    e.OnEffectGained(new HookContext(GamingQueue, Source));
                    Source.Effects.Add(e);
                    WriteLine($"[ {Source} ] 复制了 [ {killer} ] 的技能：{LastSkill.Name}！！");
                }
                if (killer.Effects.FirstOrDefault(e => e is 时雨标记 && e.Source == Source) is 时雨标记 e2)
                {
                    e2.RemainDurationTurn = 3;
                }
                else
                {
                    e2 = new 时雨标记(Skill, Source)
                    {
                        Durative = false,
                        DurationTurn = 3,
                        RemainDurationTurn = 3
                    };
                    e2.OnEffectGained(new HookContext(GamingQueue, killer));
                    killer.Effects.Add(e2);
                    WriteLine($"[ {Source} ] 给予了 [ {killer} ] 时雨标记！");
                }
            }
        }
    }
}
