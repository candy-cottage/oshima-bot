using FunGame.Core.Entity;

namespace OshimaBot.Content.Effects
{
    /// <summary>
    /// 统一的「魔法效能 → 命中率」口径（2026-09-23 建立）。
    /// <para/>⚙ 背景：`Skill.MagicEfficacy` 是**乘算系数**
    /// （`clamp(1 + (施法者INT − 魔法瓶颈) / 魔法瓶颈, 0.01, 2.0)`）。
    /// 此前各处写法不一：`施加概率负面/增益`、`银色荆棘` 只把效能乘在**等级成长项**上
    /// （`基础 + 成长×(Lv−1)×效能`）⇒ **效能对基础概率完全无效**，最终命中率算不直观；
    /// 而纯机制魔法（削决策点、施加状态）根本不吃效能乘算，瓶颈形同虚设。
    /// <para/>✅ 统一口径（唯一实现点）：
    /// <code>
    /// 命中率 = clamp( (基础概率 + 概率等级成长 × (Level − 1)) × 魔法效能 , 0, 1 )
    /// </code>
    /// ⇒ 效能 100% 时命中率 = 基础概率（**与旧版 Lv6 的数值一致，不改变既有平衡**）；
    /// 效能 200% 时翻倍（封顶 100%）；效能 → 0% 时 → 0%。
    /// <para/>✅ **被非魔法技能混用也安全（无需分流）**：全库 55 处重写 `MagicBottleneck` 的技能
    /// **清一色是魔法**，战技 / 爆发技 / 物品 / 被动都不重写 ⇒ 瓶颈取默认 **0**
    /// ⇒ `MagicEfficacy` 在 `瓶颈 == 0` 时**直接返回 1.0**（精确值）
    /// ⇒ 本口径对它们退化成**恒等变换**（`(基础+成长×(Lv−1)) × 1.0` ≡ 旧写法）。
    /// 因此「必中型控制」在非魔法技能上也不会被拉低，**一个类型判断分支都不需要**。
    /// <para/>📌 反过来说：**只有重写了 `MagicBottleneck` 的技能**才会真正吃到效能检定。
    /// <para/>📌 **纯机制魔法**（无自身概率，如"削 N 点决策点"）取
    /// <see cref="默认基础概率"/> = 0.5、成长 0 ⇒ 恰好得到
    /// <b>效能 200% 必中 / 100% 命中 50% / 0% 命中 0%</b>。
    /// </summary>
    public static class EfficacyHit
    {
        /// <summary>纯机制魔法的默认基础概率（效能 100% 时命中 50%）</summary>
        public const double 默认基础概率 = 0.5;

        /// <summary>统一命中率：基础概率（含等级成长）再乘魔法效能，封顶 [0, 1]。</summary>
        public static double 命中率(double 基础概率, double 概率等级成长, double 指定等级, double 魔法效能)
        {
            int level = (int)指定等级;
            double baseProb = level > 0 ? 基础概率 + 概率等级成长 * (level - 1) : 基础概率;
            return Math.Clamp(baseProb * 魔法效能, 0.0, 1.0);
        }

        /// <summary>按 <see cref="Skill.MagicEfficacy"/> 求命中率（自动取技能等级）。</summary>
        public static double 命中率(Skill skill, double 基础概率 = 默认基础概率, double 概率等级成长 = 0)
            => 命中率(基础概率, 概率等级成长, skill.Level, skill.MagicEfficacy);

        /// <summary>
        /// 命中检定：**效能 ≥ 200%（命中率封顶 100%）时视为必中，且不消耗随机数** ——
        /// 避免无谓推进随机序列，保持整局确定性稳定。
        /// </summary>
        public static bool 检定(double 命中率, System.Random random)
            => 命中率 >= 1.0 || random.NextDouble() <= 命中率;

        /// <summary>日志用：统一的效能/命中率文本。</summary>
        public static string 文本(Skill skill, double 命中率)
            => $"魔法效能 {skill.MagicEfficacy * 100:0.##}%，命中率 {命中率 * 100:0.##}%";
    }
}
