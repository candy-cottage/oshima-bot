using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 通用被动 · 节奏型【战术余裕】—— 自动补充决策点
    /// <para/>设计意图：本项目首个作用于**决策点**的被动。此前所有"多做事"的设计都走「配额」
    /// （`疾走`/`闪现` 是对战技本身消耗的**返还**，`咒怨洪流` 才是真给配额但**需要决策点足够**）。
    /// 而实测表明 **DP 是硬约束、配额是软约束**（AI 行动循环持续到 `CurrentDecisionPoints` 耗尽，
    /// 见 `GamingQueue.cs:1201`）⇒ **给配额而不给 DP 等于空给**。
    /// 本被动直接补 DP，且**不依赖 AI 主动选择**（自动触发），因此是"给 DP"最可靠的载体。
    /// <para/>触发：自身回合开始，每 <see cref="触发周期"/> 个自身回合补一次
    /// <para/>效果：+2 决策点（封顶到 `MaxDecisionPoints`）
    /// <para/>数值依据：稳态每回合恢复 `max(1, Max/2)` = 3 点 ⇒ 本被动折算 2/3 ≈ **+22% 决策点供给**。
    /// 注意它**不是**「每回合 +1」—— `RecoverDecisionPointsPerRound` 是死配置
    /// （恢复逻辑硬编码 `max(1, Max/2)`，从不读该字段），故只能经 `OnTurnStart` 自行补充。
    /// </summary>
    public class 战术余裕 : Skill
    {
        public override long Id => (long)PassiveID.战术余裕;
        public override string Name => "战术余裕";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 战术余裕(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 战术余裕特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 战术余裕特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"每经过 {触发周期} 个自身回合，补充 {补充量} 点决策点（不超过决策点上限）。";

        private int _累计回合 = 0;

        /// <summary>触发周期（自身回合数）</summary>
        private const int 触发周期 = 3;

        /// <summary>每次补充量（≈ 半次战技 / 两次普攻）</summary>
        private const int 补充量 = 2;

        public override void OnTurnStart(TurnContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character is null || Skill.Character != character) return;
            if (++_累计回合 < 触发周期) return;
            _累计回合 = 0;
            if (GamingQueue is null) return;
            if (!GamingQueue.CharacterDecisionPoints.TryGetValue(character, out DecisionPoints? dp) || dp is null) return;
            int before = dp.CurrentDecisionPoints;
            dp.CurrentDecisionPoints = Math.Min(before + 补充量, dp.MaxDecisionPoints);
            int actual = dp.CurrentDecisionPoints - before;
            string tail = actual < 补充量 ? $"（决策点已接近上限，溢出 {补充量 - actual} 点未生效）" : "";
            WriteLine($"[ {character} ] 凭借战术余裕补充了 {actual} 点决策点（{before} → {dp.CurrentDecisionPoints} / {dp.MaxDecisionPoints}）{tail}");
        }
    }
}
