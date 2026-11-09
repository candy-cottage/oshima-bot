using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Skills
{
    public class 竭心光环 : Skill
    {
        public override long Id => (long)PassiveID.竭心光环;
        public override string Name => "竭心光环";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 竭心光环(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 竭心光环特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 竭心光环特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"造成伤害后，令目标进入竭心状态：每{GameplayEquilibriumConstant.InGameTime}受到当前生命值 {灼烧比例 * 100:0.##}% 的真实伤害，持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}，并禁止其一切生命回复。";

        private double 持续时间 => Skill.Character != null ? 4 + Skill.Character.Level * 0.05 : 4;
        // 平衡调整（2026-09-22）：生命流失 Lv60 3.2%/秒 → 3%/秒（对标泰山玄武靠 3% 基线）
        private double 灼烧比例 => Skill.Character != null ? 0.015 + Skill.Character.Level * 0.00025 : 0.015;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (enemy.HP <= 0) return;
            // 已有竭心光环效果则刷新，不叠加
            if (enemy.Effects.Any(e => e is 持续伤害 && ReferenceEquals(e.Skill, Skill)))
            {
                foreach (Effect e in enemy.Effects.Where(e => e is 持续伤害 && ReferenceEquals(e.Skill, Skill)).ToList())
                {
                    e.RemainDuration = 持续时间;
                }
                return;
            }
            WriteLine($"[ {character} ] 对 [ {enemy} ] 施加了竭心光环！");
            Effect dot = new 持续伤害(Skill, enemy, character, true, 持续时间, 0, true, 100, 灼烧比例, DamageType.True)
            {
                RemainDuration = 持续时间
            };
            enemy.Effects.Add(dot);
            dot.OnEffectGained(new HookContext(GamingQueue, enemy));

            if (!enemy.Effects.Any(e => e is 禁止治疗 && ReferenceEquals(e.Skill, Skill)))
            {
                Effect block = new 禁止治疗(Skill, character, false, false, false, true, 持续时间, 0)
                {
                    RemainDuration = 持续时间
                };
                enemy.Effects.Add(block);
                block.OnEffectGained(new HookContext(GamingQueue, enemy));
            }
        }
    }
}
