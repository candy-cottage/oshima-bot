using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 通用被动 · 辅助型【共鸣护盾】—— 行动后为队友补护盾
    /// <para/>设计意图：五类辅助原型中的「阵地防护」。自身行动结束后观察队友，为最危险的那个补护盾。
    /// <para/>触发：自身完成一次行动后（<see cref="Effect.OnCharacterActionTaken"/>，本人为行动者；该钩子是全队列广播）
    /// <para/>效果：为生命值最低且「血量低 + 身上没护盾」的队友施加混合护盾，内部冷却
    /// <para/>标尺（手册 §6.4 被动·护盾 ≤20–25% MaxHP、再生间隔 ≥25 秒）：护盾 15% 最大生命值、冷却 40 秒 ✓
    /// <para/>⚠ 平衡修订（2026-09-23，Admin 指出「相当于常驻，稍廉价」）：
    /// **护盾不会随时间衰减**（`Shield.Mix` 是纯数值池，只被伤害消耗），原「25 秒无条件补 15% MaxHP」
    /// 单实例一局可堆到 525% MaxHP，比整改样板 `海妖外壳`（35 秒 / 375%）还多 40%。
    /// 故冷却 25 → 40 秒，并加「目标已有护盾就不补」守卫 —— 让节奏由**被打掉多少**决定。
    /// </summary>
    public class 共鸣护盾 : Skill
    {
        public override long Id => (long)PassiveID.共鸣护盾;
        public override string Name => "共鸣护盾";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 共鸣护盾(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 共鸣护盾特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 共鸣护盾特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"自身行动结束后，若存在生命值低于 {触发阈值 * 100:0.##}% 的队友，且其当前护盾不足最大生命值的 {护盾残留阈值 * 100:0.##}%，" +
            $"则为其中生命值最低者提供 {护盾系数 * 100:0.##}% 最大生命值的混合护盾；" +
            $"每 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}至多一次。";

        private double 剩余冷却 = 0;

        /// <summary>目标生命值低于该比例才算「需要掩护」</summary>
        private double 触发阈值 => 0.7;

        /// <summary>护盾量（Lv60 = 15% 最大生命值）</summary>
        private double 护盾系数 => Skill.Character != null ? 0.12 + Skill.Character.Level * 0.0005 : 0.12;

        /// <summary>
        /// 目标当前护盾达到该比例就**不再补**（防堆积）。
        /// <para/>⚠ 实测结论（2026-09-23，极值 A/B）：把该阈值临时改成 5.0（永远拦下）后，
        /// 3 局同种子的给盾次数 100 → 103 次，**没有区别** —— 在「拉满 + 定态」这种持续挨打的
        /// 高压环境里队友护盾总会被打掉，这道守卫**基本不触发**；本次实际降幅全部来自冷却 25 → 40 秒。
        /// <para/>保留理由：`Shield.Mix` **没有任何时间衰减**（`Shield.cs` 搜不到 Elapsed/Decay），
        /// 低压力场景（对手输出不足 / 队里还有别的治疗源）下，固定冷却补盾确实会无限堆积。
        /// 这道守卫是**上界保障**：单目标护盾不超过「15% MaxHP + 一次未耗尽的残留」。
        /// </summary>
        private double 护盾残留阈值 => 0.10;

        private double 冷却时间 => 40;

        public override void OnCharacterActionTaken(ActionContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (剩余冷却 > 0) return;
            Character? target = 最低生命队友(character);
            if (target == null) return;
            double shield = target.MaxHP * 护盾系数;
            if (shield <= 0) return;
            剩余冷却 = 冷却时间;
            WriteLine($"[ {character} ] 发动了共鸣护盾！[ {target} ] 获得了 {shield:0.##} 点混合护盾！");
            target.Shield.Mix += shield;
            GamingQueue?.AddApplyEffects(target, EffectType.Shield);
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (剩余冷却 > 0) 剩余冷却 = Math.Max(0, 剩余冷却 - ctx.Elapsed);
        }

        /// <summary>
        /// 生命值百分比最低、且同时满足「血量低于阈值」「当前护盾不足」的存活队友（不含自己）。
        /// 任一条不满足都返回 null，且**不消耗冷却** —— 条件一满足即可立刻响应。
        /// </summary>
        private Character? 最低生命队友(Character self)
        {
            if (GamingQueue == null) return null;
            Character? target = GamingQueue.GetTeammates(self)
                .Where(c => c.HP > 0 && c != self)
                .OrderBy(c => c.HP / Math.Max(1, c.MaxHP))
                .FirstOrDefault();
            if (target == null) return null;
            if (target.HP / Math.Max(1, target.MaxHP) >= 触发阈值) return null;
            if (target.Shield.TotalMix >= target.MaxHP * 护盾残留阈值) return null;
            return target;
        }
    }
}
