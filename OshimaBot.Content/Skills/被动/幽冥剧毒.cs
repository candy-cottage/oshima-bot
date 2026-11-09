using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Skills
{
    public class 幽冥剧毒 : Skill
    {
        public override long Id => (long)PassiveID.幽冥剧毒;
        public override string Name => "幽冥剧毒";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 幽冥剧毒(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 幽冥剧毒特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 幽冥剧毒特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"普通攻击命中后，为目标附加幽冥剧毒：每{GameplayEquilibriumConstant.InGameTime}造成目标已损失生命值 {剧毒比例 * 100:0.##}% 的魔法伤害，持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}。";

        private double 持续时间 => Skill.Character != null ? 3 + Skill.Character.Level * 0.05 : 3;
        // 平衡调整（2026-09-22）：已损失生命真伤 Lv60 7%/秒×6s=42% → 2%/秒×6s=12%（对标八叶灭杀整改后 12%）
        private double 剧毒比例 => Skill.Character != null ? 0.005 + Skill.Character.Level * 0.00025 : 0.005;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (!ctx.IsNormalAttack) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (enemy.HP <= 0) return;
            // 已有剧毒则刷新持续时间
            if (enemy.Effects.Any(e => e is 持续伤害 && ReferenceEquals(e.Skill, Skill)))
            {
                foreach (Effect e in enemy.Effects.Where(e => e is 持续伤害 && ReferenceEquals(e.Skill, Skill)).ToList())
                {
                    e.RemainDuration = 持续时间;
                }
                return;
            }
            // 已损失生命值：以挂上瞬间的差值作为固定毒伤数值
            double missing = enemy.MaxHP - enemy.HP;
            WriteLine($"[ {character} ] 对 [ {enemy} ] 附加了幽冥剧毒！（基于 {missing:0.##} 点已损失生命值）");
            Effect dot = new 持续伤害(Skill, enemy, character, true, 持续时间, 0, false, missing * 剧毒比例, 0, DamageType.Magical)
            {
                RemainDuration = 持续时间
            };
            enemy.Effects.Add(dot);
            dot.OnEffectGained(new HookContext(GamingQueue, enemy));
        }
    }
}
