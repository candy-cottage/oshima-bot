using FunGame.Core.Entity;
using FunGame.Core.Interface.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Content.Skills;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    public class 宫监手标记 : Effect
    {
        public override long Id => (long)PassiveEffectID.宫监手标记;
        public override string Name => "宫监手标记";
        public override string Description => $"{放监.任务要求}。来自：[ {Source} ]";
        public override EffectType EffectType => EffectType.Mark;
        public override bool IsDebuff => true;
        public override Character Source => _sourceCharacter;
        public override DispelledType DispelledType { get; set; } = DispelledType.CannotBeDispelled;

        private readonly Character _sourceCharacter;
        private readonly Character _targetCharacter;
        private readonly 放监特效 放监;
        private bool 已完成普攻任务 = false;
        private bool 已完成指向性技能任务 = false;

        public 宫监手标记(Skill skill, Character sourceCharacter, Character targetCharacter, 放监特效 effect) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _sourceCharacter = sourceCharacter;
            _targetCharacter = targetCharacter;
            放监 = effect;
        }

        public void 普攻任务完成(Character character)
        {
            WriteLine($"[ {character} ] 的「放监」任务 [ 对友方角色普通攻击 ] 完成！");
            已完成普攻任务 = true;
            CheckComplete(character);
        }

        public void 指向性技能任务完成(Character character)
        {
            WriteLine($"[ {character} ] 的「放监」任务 [ 对{Source}释放指向性技能 ] 完成！");
            已完成指向性技能任务 = true;
            CheckComplete(character);
        }

        public void CheckComplete(Character character)
        {
            if (已完成普攻任务 && 已完成指向性技能任务)
            {
                character.Effects.Remove(this);
                WriteLine($"[ {character} ] 已消除宫监手标记！");
            }
        }

        public override void AlterSelectListBeforeSelection(SelectionContext ctx)
        {
            ISkill? skill = ctx.Skill;
            List<Character> enemys = ctx.Enemys;
            List<Character> teammates = ctx.Teammates;
            if (skill is NormalAttack)
            {
                enemys.AddRange(teammates);
            }
        }

        public override BeforeCriticalCheckResult BeforeCriticalCheck(DamageContext ctx)
        {
            if (ctx.Trigger is not Character actor) return default;
            bool isNormalAttack = ctx.IsNormalAttack;
            if (actor == _targetCharacter && isNormalAttack)
            {
                return new BeforeCriticalCheckResult { ThrowingBonusDelta = 300 };
            }
            return default;
        }

        public override BeforeEvadeCheckResult BeforeEvadeCheck(DamageContext ctx)
        {
            if (ctx.Trigger is not Character actor) return default;
            if (actor == _targetCharacter)
            {
                return new BeforeEvadeCheckResult { SkipEvadeCheck = true };
            }
            return default;
        }

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (ctx.Enemy is not Character enemy) return;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageResult damageResult = ctx.DamageResult;
            if (character == _targetCharacter && isNormalAttack && (damageResult == DamageResult.Normal || damageResult == DamageResult.Critical))
            {
                if (GamingQueue != null && GamingQueue.IsTeammate(character, enemy))
                {
                    普攻任务完成(character);
                }
            }
        }

        public override void AfterDeathCalculation(DeathContext ctx)
        {
            if (ctx.Trigger is not Character death) return;
            if (death == _targetCharacter)
            {
                death.Effects.Remove(this);
            }
            if (death == _sourceCharacter)
            {
                _targetCharacter.Effects.Remove(this);
            }
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (!已完成普攻任务 || !已完成指向性技能任务)
            {
                放监.造成伤害(character, !已完成普攻任务 && !已完成指向性技能任务 ? 2 : 1);
            }
        }
    }
}
