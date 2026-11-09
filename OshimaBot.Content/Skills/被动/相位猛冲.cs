using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 相位猛冲 : Skill
    {
        public override long Id => (long)PassiveID.相位猛冲;
        public override string Name => "相位猛冲";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 相位猛冲(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 相位猛冲特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 相位猛冲特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"在 {窗口时间:0.##} {GameplayEquilibriumConstant.InGameTime}内用 {需要命中次数} 个独立的普通攻击或技能命中敌人后，获得 {行动系数提升 * 100:0.##}% 行动系数和 {加速系数提升 * 100:0.##}% 加速系数，持续 {强化持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。触发后进入 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}冷却。";

        private int 命中次数 = 0;
        private double 剩余窗口 = 0;
        private double 剩余冷却 = 0;

        private int 需要命中次数 => 3;
        private double 窗口时间 => Skill.Character != null ? 5 + Skill.Character.Level * 0.1 : 5;
        private double 冷却时间 => Skill.Character != null ? 10 - Skill.Character.Level * 0.05 : 10;
        private double 强化持续时间 => Skill.Character != null ? 6 + Skill.Character.Level * 0.1 : 6;
        private double 行动系数提升 => Skill.Character != null ? 0.25 + Skill.Character.Level * 0.004 : 0.25;
        private double 加速系数提升 => Skill.Character != null ? 0.25 + Skill.Character.Level * 0.004 : 0.25;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (剩余冷却 > 0) return;
            剩余窗口 = 窗口时间;
            命中次数++;
            if (命中次数 >= 需要命中次数)
            {
                命中次数 = 0;
                剩余窗口 = 0;
                剩余冷却 = 冷却时间;
                WriteLine($"[ {character} ] 发动了相位猛冲！获得了 {行动系数提升 * 100:0.##}% 行动系数和 {加速系数提升 * 100:0.##}% 加速系数！");
                Effect e = new DynamicsEffect(Skill, new Dictionary<string, object>()
                {
                    { "exac", 行动系数提升 },
                    { "exacc", 加速系数提升 }
                }, character)
                {
                    Name = nameof(相位猛冲) + "·急速",
                    Durative = true,
                    Duration = 强化持续时间
                };
                character.Effects.Add(e);
                e.OnEffectGained(new HookContext(GamingQueue, character));
            }
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (剩余窗口 > 0)
            {
                剩余窗口 -= ctx.Elapsed;
                if (剩余窗口 <= 0)
                {
                    命中次数 = 0;
                }
            }
            if (剩余冷却 > 0)
            {
                剩余冷却 -= ctx.Elapsed;
            }
        }
    }
}
