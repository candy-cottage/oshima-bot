using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 勇气之霎 : Skill
    {
        public override long Id => (long)PassiveID.勇气之霎;
        public override string Name => "勇气之霎";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 勇气之霎(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 勇气之霎特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 勇气之霎特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"受到伤害时，有 {触发概率 * 100:0.##}% 概率在刹那之间获得勇气：提升 {生命偷取 * 100:0.##}% 生命偷取和 {攻击力提升 * 100:0.##}% 攻击力，持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。";

        private double 触发概率 => Skill.Character != null ? 0.3 + Skill.Character.Level * 0.002 : 0.3;
        private double 生命偷取 => Skill.Character != null ? 0.2 + Skill.Character.Level * 0.002 : 0.2;
        private double 攻击力提升 => Skill.Character != null ? 0.2 + Skill.Character.Level * 0.002 : 0.2;
        private double 持续时间 => Skill.Character != null ? 4 + Skill.Character.Level * 0.05 : 4;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character attacker || ctx.Enemy is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (attacker == character) return;
            if (ctx.ActualDamage <= 0) return;
            if (Random.NextDouble() > 触发概率) return;
            // 刷新勇气 buff（不叠加）
            List<Effect> olds = character.Effects.Where(e => e is DynamicsEffect && e.Name == nameof(勇气之霎) + "·勇气").ToList();
            foreach (Effect e in olds)
            {
                e.RemoveFromCharacter(character);
            }
            WriteLine($"[ {character} ] 勇气之霎触发！获得了生命偷取与攻击力提升！");
            Effect buff = new DynamicsEffect(Skill, new Dictionary<string, object>()
            {
                { "exls", 生命偷取 },
                { "exatk2", 攻击力提升 }
            }, character)
            {
                Name = nameof(勇气之霎) + "·勇气",
                Durative = true,
                Duration = 持续时间
            };
            buff.AddToCharacter(character);
        }
    }
}
