using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 战技 · 节奏型【过载】—— 用生命换决策点
    /// <para/>设计意图：本项目首次把「续航」换算成「节奏」。决策点的常规来源只有每回合恢复
    /// （`max(1, Max/2)`），本技能给出一条**主动透支**的获取途径：立刻用当前生命换一次行动机会。
    /// <para/>效果：消耗自身当前生命的 {生命消耗比例}（**保底至少剩 1 点生命，不会自杀**），自身 +3 决策点。
    /// <para/>标尺（战技）：EP 40–80 → 50｜CD 18–45 → 45｜硬直 7–10 → 8；无伤害、无控制，不占控制点预算。
    /// <para/>强度依据：+3 决策点按 DP→行动转换率（约 1:5）折算 ≈ +0.6 次行动 —— 代价是 15% 当前生命。
    /// </summary>
    public class 过载 : Skill
    {
        public override long Id => (long)SkillID.过载;
        public override string Name => "过载";
        public override string Description => string.Join("", Effects.Select(e => e.Description));
        public override double EPCost => 50;
        public override double CD => 45;
        public override double HardnessTime { get; set; } = 8;
        public override bool CanSelectSelf => true;
        public override bool CanSelectTeammate => false;
        public override bool CanSelectEnemy => false;
        public override int CanSelectTargetCount => 1;

        public 过载(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 过载特效(this));
        }
    }

    public class 过载特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"消耗自身当前生命值的 {生命消耗比例 * 100:0.##}%（至少保留 1 点生命），立即获得 {补充量} 点决策点。";

        private const double 生命消耗比例 = 0.15;
        private const int 补充量 = 3;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            if (GamingQueue is null) return;
            // 保底：本次消耗不会把施法者打死（最多扣到剩 1 点）
            double cost = Math.Min(caster.HP * 生命消耗比例, Math.Max(0, caster.HP - 1));
            if (cost > 0) caster.HP -= cost;
            if (!GamingQueue.CharacterDecisionPoints.TryGetValue(caster, out DecisionPoints? dp) || dp is null)
            {
                WriteLine($"[ {caster} ] 发动过载，消耗了 {cost:0.##} 点生命，但决策点未就绪。");
                return;
            }
            int before = dp.CurrentDecisionPoints;
            dp.CurrentDecisionPoints = Math.Min(before + 补充量, dp.MaxDecisionPoints);
            int actual = dp.CurrentDecisionPoints - before;
            string tail = actual < 补充量 ? $"（决策点接近上限，溢出 {补充量 - actual} 点未生效）" : "";
            WriteLine($"[ {caster} ] 发动过载，消耗 {cost:0.##} 点生命换取 {actual} 点决策点（{before} → {dp.CurrentDecisionPoints} / {dp.MaxDecisionPoints}）{tail}");
        }
    }
}
