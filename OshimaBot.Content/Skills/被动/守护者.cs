using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 守护者 : Skill
    {
        public override long Id => (long)PassiveID.守护者;
        public override string Name => "守护者";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 守护者(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 守护者特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 守护者特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"在 {观察窗口:0.##} {GameplayEquilibriumConstant.InGameTime}内累计受到超过最大生命值 {触发阈值 * 100:0.##}% 的伤害时，立即获得 {护盾系数 * 100:0.##}% 最大生命值 [ {Skill.Character?.MaxHP * 护盾系数:0.##} ] 的混合护盾和行动加速，持续 {强化持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。触发后进入 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}冷却。";

        private double 累计伤害 = 0;
        private double 剩余窗口 = 0;
        private double 剩余冷却 = 0;

        private double 观察窗口 => 2.5;
        private double 触发阈值 => Skill.Character != null ? 0.08 + Skill.Character.Level * 0.001 : 0.08;
        private double 护盾系数 => Skill.Character != null ? 0.12 + Skill.Character.Level * 0.002 : 0.12;
        private double 强化持续时间 => Skill.Character != null ? 4 + Skill.Character.Level * 0.1 : 4;
        private double 行动速度提升 => Skill.Character != null ? 0.2 + Skill.Character.Level * 0.003 : 0.2;
        // 平衡调整（2026-09-22）：护盾冷却 Lv60 11s → 25s（标尺 ≥25s）
        private double 冷却时间 => Skill.Character != null ? 37 - Skill.Character.Level * 0.2 : 37;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character attacker || ctx.Enemy is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (attacker == character) return;
            if (ctx.ActualDamage <= 0) return;
            if (剩余冷却 > 0) return;
            // 受击窗口内累计
            剩余窗口 = 观察窗口;
            累计伤害 += ctx.ActualDamage;
            if (累计伤害 >= character.MaxHP * 触发阈值)
            {
                累计伤害 = 0;
                剩余窗口 = 0;
                剩余冷却 = 冷却时间;
                double shield = character.MaxHP * 护盾系数;
                WriteLine($"[ {character} ] 发动了守护者！获得 {shield:0.##} 点混合护盾和行动加速！");
                character.Shield.Mix += shield;
                GamingQueue?.AddApplyEffects(character, EffectType.Shield);
                Effect e = new DynamicsEffect(Skill, new Dictionary<string, object>()
                {
                    { "exac", 行动速度提升 },
                    { "exacc", 行动速度提升 }
                }, character)
                {
                    Name = nameof(守护者) + "·守护",
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
                    累计伤害 = 0;
                }
            }
            if (剩余冷却 > 0)
            {
                剩余冷却 -= ctx.Elapsed;
            }
        }
    }
}
