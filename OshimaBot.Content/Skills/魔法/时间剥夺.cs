using FunGame.Core.Entity;
using OshimaBot.Content.Effects;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 魔法 · 控制型【时间剥夺】—— 直接剥夺对手的决策点
    /// <para/>设计意图：本项目首次把「控制」从**状态层面**下移到**资源层面**。
    /// 实测表明决策点是硬约束（AI 行动循环持续到 `CurrentDecisionPoints` 耗尽，
    /// `GamingQueue.cs:1201`）且每回合被用光（上回合结束仅剩 ~7%）
    /// ⇒ **把对手的决策点削到 0，等于直接终止他本回合的行动**，且不吃"控制状态"的免疫/驱散。
    /// <para/>标尺（魔法）：MP L8 240–900 → 365｜CD 20–65 → L8 = 41｜吟唱 2–12 → 3｜硬直 3–8 → 5；
    /// `MagicBottleneck` 必填 → 103。剥夺量 L1–3=1 / L4–7=2 / L8=3。
    /// <para/>⚠ 归属：这是**新型控制手段**，手册标尺原本没有对应条目，本实现按
    /// 「每次剥夺 2 点 ≈ 目标少做 1 件事、弱于封技 1 回合」定价，并留 41 秒冷却。
    /// </summary>
    public class 时间剥夺 : Skill
    {
        public override long Id => (long)MagicID.时间剥夺;
        public override string Name => "时间剥夺";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override double MPCost => Level > 0 ? 105 + 95 * (Level - 1) : 105;
        public override double CD => Level > 0 ? 45 + 2 * (Level - 1) : 45;
        public override double CastTime => Level > 0 ? 3 + 1 * (Level - 1) : 3;
        public override double HardnessTime { get; set; } = 5;
        public override bool CanSelectEnemy => true;
        public override bool CanSelectTeammate => false;
        public override bool CanSelectSelf => false;
        public override int CanSelectTargetCount => 1;
        public override double MagicBottleneck => 103;

        public 时间剥夺(Character? character = null) : base(SkillType.Magic, character)
        {
            Effects.Add(new 时间剥夺特效(this));
        }
    }

    public class 时间剥夺特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"剥夺目标 {剥夺量} 点决策点（不会使其低于 0）。";

        /// <summary>剥夺量：L1–3 = 1 / L4–7 = 2 / L8 = 3</summary>
        private int 剥夺量 => 1 + Skill.Level / 4;

        /// <summary>
        /// 命中率：统一走 <see cref="EfficacyHit"/>（2026-09-23 统一口径）。
        /// <para/>纯机制魔法没有自身概率 ⇒ 取默认基础概率 0.5、成长 0
        /// ⇒ 效能 200% 必中 / 100% 命中 50% / →0% →0%。
        /// </summary>
        private double 命中率 => EfficacyHit.命中率(Skill);

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            if (GamingQueue is null) return;
            foreach (Character target in ctx.Targets)
            {
                if (target.HP <= 0) continue;
                if (!命中检定(caster, target)) continue;
                if (!GamingQueue.CharacterDecisionPoints.TryGetValue(target, out DecisionPoints? dp) || dp is null) continue;
                int before = dp.CurrentDecisionPoints;
                dp.CurrentDecisionPoints = Math.Max(0, before - 剥夺量);
                int actual = before - dp.CurrentDecisionPoints;
                WriteLine($"[ {caster} ] 对 [ {target} ] 施放了时间剥夺：[ {target} ] 失去 {actual} 点决策点（{before} → {dp.CurrentDecisionPoints} / {dp.MaxDecisionPoints}）{(dp.CurrentDecisionPoints == 0 ? "，本回合已无法行动！" : "")}");
            }
        }

        /// <summary>命中检定：统一走 <see cref="EfficacyHit.检定"/>（效能够高时必中且不消耗随机数）。</summary>
        private bool 命中检定(Character caster, Character target)
        {
            double rate = 命中率;
            string info = EfficacyHit.文本(Skill, rate);
            if (!EfficacyHit.检定(rate, Random))
            {
                WriteLine($"[ {caster} ] 的时间剥夺被 [ {target} ] 抵抗了！（{info}）");
                return false;
            }
            WriteLine($"[ {caster} ] 的时间剥夺命中了 [ {target} ]（{info}）。");
            return true;
        }
    }
}
