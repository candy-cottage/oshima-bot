using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 通用被动 · 辅助型【生命链接】—— 传导型治疗
    /// <para/>设计意图：填补「通用被动全是给自己加成、缺少给予他人的治疗/增益」的空缺。
    /// 这是五类辅助原型中的「输出转续航」：自身打得越狠，队友回得越多。
    /// <para/>触发：自身造成伤害后（<see cref="Effect.AfterDamageCalculation"/>，本人为攻击方）
    /// <para/>效果：为生命值百分比最低的队友回复生命值，内部冷却。
    /// <para/>标尺（手册 §6.4 被动·回复 ≤2–3%/秒）：回复系数 6.4%（Lv60）× 冷却 8 秒 ⇒ 约 0.8%/秒。
    /// </summary>
    public class 生命链接 : Skill
    {
        public override long Id => (long)PassiveID.生命链接;
        public override string Name => "生命链接";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 生命链接(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 生命链接特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 生命链接特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"自身造成伤害后，为生命值百分比最低的队友回复 {回复系数 * 100:0.##}% 最大生命值（不超过本次伤害的 100%），" +
            $"每 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}至多一次。";

        private double 剩余冷却 = 0;

        private double 回复系数 => Skill.Character != null ? 0.06 + Skill.Character.Level * 0.0004 : 0.06;
        private double 冷却时间 => 8;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (ctx.Enemy is not Character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (ctx.ActualDamage <= 0) return;
            if (剩余冷却 > 0) return;
            Character? target = 最低生命队友(character);
            if (target == null) return;
            double heal = Math.Min(target.MaxHP * 回复系数, ctx.ActualDamage);
            if (heal <= 0) return;
            剩余冷却 = 冷却时间;
            WriteLine($"[ {character} ] 发动了生命链接！将生命涌向 [ {target} ]，回复 {heal:0.##} 点生命值！");
            HealToTarget(character, target, heal);
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (剩余冷却 > 0) 剩余冷却 = Math.Max(0, 剩余冷却 - ctx.Elapsed);
        }

        /// <summary>
        /// 生命值百分比最低的存活队友；若其生命值已接近满值（≥<see cref="有效治疗阈值"/>）则视为无需治疗，返回 null（不消耗冷却）
        /// </summary>
        private Character? 最低生命队友(Character self)
        {
            if (GamingQueue == null) return null;
            Character? target = GamingQueue.GetTeammates(self)
                .Where(c => c.HP > 0 && c != self)
                .OrderBy(c => c.HP / Math.Max(1, c.MaxHP))
                .FirstOrDefault();
            if (target == null) return null;
            if (target.HP / Math.Max(1, target.MaxHP) >= 有效治疗阈值) return null;
            return target;
        }

        /// <summary>有效治疗阈值：队友生命值低于该比例才消耗冷却，避免满血空放</summary>
        private const double 有效治疗阈值 = 0.95;
    }
}
