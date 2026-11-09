using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 【技能制作示范】断罪斩 —— 单目标物理战技
    /// <para>制作一个技能只需三步：
    /// ① 在 <see cref="SkillID"/> 分配一个 Id；
    /// ② 继承 <see cref="Skill"/>，按需 override Id/Name/消耗/冷却/硬直等数值属性；
    /// ③ 在构造里把效果（Effect 子类）加入 <see cref="Skill.Effects"/>，技能的效果与描述即生效。
    /// 数值公式沿用 Oshima 惯例：随 <see cref="Skill.Level"/> 成长（Level 为 0 时视为未学习，用基础值）。</para>
    /// <para>【战技规范】消耗使用 <see cref="Skill.EPCost"/>（能量）；
    /// <b>不要</b> override <see cref="Skill.CastTime"/>——吟唱是魔法专属，<see cref="Skill.RealCastTime"/> 注释明确标注「[ 魔法 ]」，
    /// 战技设置咏唱会违背 GamingQueue 的行动结算流程；魔法才使用 MPCost + CastTime + MagicBottleneck。</para>
    /// </summary>
    public class 断罪斩 : Skill
    {
        public override long Id => (long)SkillID.断罪斩;
        public override string Name => "断罪斩";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        // 战技使用能量（EP）作为消耗；咏唱(CastTime)与魔法消耗(MPCost)属于魔法，战技不使用
        public override double EPCost => 50;
        public override double CD => 25;
        public override double HardnessTime { get; set; } = 8;

        /// <summary>
        /// 继承此构造：传入角色后即可 AddSkillToCharacter 加入战斗
        /// </summary>
        /// <param name="character"></param>
        public 断罪斩(Character? character = null) : base(SkillType.Skill, character)
        {
            // 复用现成的「基于攻击力的伤害」效果（带基础伤害 + 攻击力系数，物理伤害型）
            // 攻击力系数对齐同批战技：0.075 + 0.055×(L-1)（L6 = 35%）
            Effects.Add(new 基于攻击力的伤害_带基础伤害(this, 120, 60, 0.075, 0.055, DamageType.Physical));
        }
    }
}
