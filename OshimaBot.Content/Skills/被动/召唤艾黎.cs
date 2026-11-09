using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 召唤艾黎 : Skill
    {
        public override long Id => (long)PassiveID.召唤艾黎;
        public override string Name => "召唤艾黎";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 召唤艾黎(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 召唤艾黎特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 召唤艾黎特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"普通攻击或技能命中敌人时，召唤艾黎飞向目标造成 {艾黎伤害:0.##} 点魔法伤害；当{Skill.SkillOwner()}生命值低于 {护盾生命阈值 * 100:0.##}% 且受到伤害时，艾黎会为{Skill.SkillOwner()}生成 {护盾值:0.##} 点混合护盾（每 {护盾冷却:0.##} {GameplayEquilibriumConstant.InGameTime}至多一次）。";

        private double 剩余护盾冷却 = 0;

        private double 艾黎伤害 => Skill.Character != null ? 45 + Skill.Character.Level * 4 + Skill.Character.PrimaryAttributeValue * 0.35 : 45;
        private double 护盾生命阈值 => 0.5;
        private double 护盾值 => Skill.Character != null ? 60 + Skill.Character.Level * 9 : 60;
        // 平衡调整（2026-09-22）：护盾再生间隔 Lv60 6.2s → 25s（标尺 ≥25s；样板：海妖外壳 35s）
        private double 护盾冷却 => Skill.Character != null ? 31 - Skill.Character.Level * 0.1 : 31;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null) return;
            if (Skill.Character == character && enemy.HP > 0)
            {
                // 出伤侧：艾黎飞向敌人造成额外魔法伤害
                if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
                WriteLine($"[ {character} ] 发动了召唤艾黎！艾黎飞向 [ {enemy} ] 造成了 {艾黎伤害:0.##} 点魔法伤害！");
                DamageToEnemy(character, enemy, DamageType.Magical, ctx.MagicType, 艾黎伤害, new(character)
                {
                    TriggerEffects = false
                });
            }
            else if (Skill.Character == enemy)
            {
                // 受击侧：低血量时艾黎提供护盾
                if (character == enemy) return;
                if (剩余护盾冷却 > 0) return;
                if (enemy.HP / enemy.MaxHP >= 护盾生命阈值) return;
                剩余护盾冷却 = 护盾冷却;
                WriteLine($"[ {enemy} ] 受到了攻击，艾黎为其生成 {护盾值:0.##} 点混合护盾！");
                enemy.Shield.Mix += 护盾值;
                GamingQueue?.AddApplyEffects(enemy, EffectType.Shield);
            }
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (剩余护盾冷却 > 0)
            {
                剩余护盾冷却 -= ctx.Elapsed;
                if (剩余护盾冷却 < 0) 剩余护盾冷却 = 0;
            }
        }
    }
}
