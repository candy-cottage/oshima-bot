using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 零式崩解 : Skill
    {
        public override long Id => (long)PassiveID.零式崩解;
        public override string Name => "零式崩解";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";

        public 零式崩解(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 零式崩解特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 零式崩解特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"{Skill.SkillOwner()}的零式剑法能够轻松命中敌人弱点。{Skill.SkillOwner()}的暴击伤害提升 70%。";

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            character.ExCritDMG += 0.7;
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            character.ExCritDMG -= 0.7;
        }
    }
}
