using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 饼干配送 : Skill
    {
        public override long Id => (long)PassiveID.饼干配送;
        public override string Name => "饼干配送";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 饼干配送(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 饼干配送特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 饼干配送特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"每经过 {配送间隔:0.##} {GameplayEquilibriumConstant.InGameTime}获得一块免费饼干，立即回复 {生命回复 * 100:0.##}% [ {Skill.Character?.MaxHP * 生命回复:0.##} ] 最大生命值、{魔法回复 * 100:0.##}% [ {Skill.Character?.MaxMP * 魔法回复:0.##} ] 最大魔法值。";

        private double 剩余时间 = 0;

        private double 配送间隔 => Skill.Character != null ? 50 - Skill.Character.Level * 0.2 : 45;
        private double 生命回复 => Skill.Character != null ? 0.06 + Skill.Character.Level * 0.001 : 0.06;
        private double 魔法回复 => Skill.Character != null ? 0.1 + Skill.Character.Level * 0.001 : 0.1;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            剩余时间 = 配送间隔;
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            剩余时间 -= ctx.Elapsed;
            if (剩余时间 <= 0)
            {
                剩余时间 += 配送间隔;
                double hp = character.MaxHP * 生命回复;
                double mp = character.MaxMP * 魔法回复;
                WriteLine($"[ {character} ] 收到了饼干配送！回复了 {hp:0.##} 点生命值和 {mp:0.##} 点魔法值！");
                HealToTarget(character, character, hp);
                character.MP = Math.Min(character.MP + mp, character.MaxMP);
            }
        }
    }
}
