using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 战技 · 辅助型【战术部署】—— 把「节奏」转移给队友
    /// <para/>设计意图：本项目此前的技能只作用于「生命 / 能量 / 属性 / 位置」，
    /// 从未触碰**决策点**这一层。而实测（2759 回合）显示每回合行动数中位仅 **1.00**，
    /// 且 AI 的行动循环会持续到 `CurrentDecisionPoints` 耗尽
    /// （`GamingQueue.cs:1201`）⇒ **决策点才是硬约束**，配额只是软约束。
    /// 因此"给人多做事"的正确做法是**给决策点**，而非给配额。
    /// <para/>效果：目标 +2 决策点；若目标不是自己，额外给自己 +1 决策点（保证施法者不完全亏）。
    /// <para/>标尺：战技（支援类）EP 40–80 / CD 55–60 → 取 EP 60 / CD 45（本技能无伤害，
    /// 不占"控制点预算"；决策点补充按 §5.1 的团队折算，单次团队净 +3 点 ≈ 3 次普攻）。
    /// <para/>⚠ 决策点补充**必须封顶到 `MaxDecisionPoints`**：超出部分会在下一回合恢复时
    /// 被 `Math.Min(Current + pointsToAdd, Max)` 截掉，属于无效溢出。
    /// </summary>
    public class 战术部署 : Skill
    {
        public override long Id => (long)SkillID.战术部署;
        public override string Name => "战术部署";
        public override string Description => string.Join("", Effects.Select(e => e.Description));
        public override double EPCost => 60;
        public override double CD => 45;
        public override double HardnessTime { get; set; } = 8;
        public override bool CanSelectSelf => true;
        public override bool CanSelectTeammate => true;
        public override bool CanSelectEnemy => false;
        public override int CanSelectTargetCount => 1;

        public 战术部署(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 战术部署特效(this));
        }
    }

    public class 战术部署特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"为{Skill.TargetDescription()}补充 {队友补充量} 点决策点；若目标不是自己，施法者额外获得 {自身补充量} 点决策点。";

        /// <summary>给目标补充的决策点（≈ 让目标多做 1–2 件事）</summary>
        private const int 队友补充量 = 2;

        /// <summary>给施法者自己的补偿（仅当目标不是自己时生效，避免"自己给自己"重复计算）</summary>
        private const int 自身补充量 = 1;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            foreach (Character target in ctx.Targets)
            {
                if (target.HP <= 0) continue;
                补充决策点(caster, target, 队友补充量);
                if (target != caster)
                {
                    补充决策点(caster, caster, 自身补充量);
                }
            }
        }

        /// <summary>
        /// 为角色补充决策点。<para/>
        /// ⚠ 必须封顶到 <see cref="DecisionPoints.MaxDecisionPoints"/> —— 超出的部分会在
        /// 下一回合恢复时被截掉（`GamingQueue.cs:4243/4249` 的 `Math.Min`），属于无效溢出。
        /// </summary>
        private void 补充决策点(Character caster, Character target, int amount)
        {
            if (GamingQueue is null) return;
            if (!GamingQueue.CharacterDecisionPoints.TryGetValue(target, out DecisionPoints? dp) || dp is null) return;
            int before = dp.CurrentDecisionPoints;
            dp.CurrentDecisionPoints = Math.Min(before + amount, dp.MaxDecisionPoints);
            int actual = dp.CurrentDecisionPoints - before;
            string tail = actual < amount ? $"（上限 {dp.MaxDecisionPoints}，溢出 {amount - actual} 点未生效）" : "";
            WriteLine($"[ {caster} ] 实施了战术部署：[ {target} ] 获得 {actual} 点决策点（{before} → {dp.CurrentDecisionPoints} / {dp.MaxDecisionPoints}）{tail}");
        }
    }
}
