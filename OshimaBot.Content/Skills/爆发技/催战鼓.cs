using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using FunGame.Core.Model.PrefabricatedEntity;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 爆发技 · 节奏型【催战鼓】—— 全队额外行动机会
    /// <para/>设计意图：`疾走`/`闪现` 给的配额只是**对战技自身消耗的返还**（用一次战技换回一次战技机会，
    /// 净收益近乎 0）；`咒怨洪流` 才是真给配额，但**要求决策点足够**才有用。
    /// 本技能补上那个缺口：**同时给「配额」和「决策点」**，所以配额一定能兑现成行动。
    /// <para/>效果：全队各 +N 决策点 + 各获得一次额外**战技**配额
    /// <para/>标尺：爆发技 EP 100（满能量 `CostAllEP`）/ CD ≥60 → 取 75（团队级节奏技，留冷却）/
    /// 硬直 8–13 → 取 10；纯辅助无伤害，不占控制点预算。
    /// <para/>⚠ 补充决策点**必须封顶到 `MaxDecisionPoints`**，否则超出部分会在下一回合恢复时被截掉。
    /// </summary>
    public class 催战鼓 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.催战鼓;
        public override string Name => "催战鼓";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double CD => 75;
        public override double HardnessTime { get; set; } = 10;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => true;
        public override bool CanSelectSelf => true;
        public override bool SelectAllTeammates => true;

        public 催战鼓(Character? character = null) : base(character)
        {
            Effects.Add(new 催战鼓特效(this));
        }
    }

    public class 催战鼓特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"为全队补充 {决策点补充} 点决策点，并让全队各获得一次额外的战技决策点配额" +
            (Improvement > 0 ? $"（灵魂绑定额外效果：决策点补充提升 {Improvement * 100:0.##}%）" : "") + "。";

        /// <summary>决策点补充（Lv1–2 = 1 / Lv3–5 = 2 / Lv6 = 3）</summary>
        private int 决策点补充 => 1 + Skill.Level / 3;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            if (GamingQueue is null) return;
            double scale = Improvement > 0 ? 1 + Improvement : 1;
            int amount = Math.Max(1, (int)Math.Round(决策点补充 * scale));
            foreach (Character target in ctx.Targets)
            {
                if (target.HP <= 0) continue;
                if (!GamingQueue.CharacterDecisionPoints.TryGetValue(target, out DecisionPoints? dp) || dp is null) continue;
                int before = dp.CurrentDecisionPoints;
                // ① 补决策点：否则"给了配额也没决策点去做"（本技能区别于疾走/闪现的关键）
                dp.CurrentDecisionPoints = Math.Min(before + amount, dp.MaxDecisionPoints);
                // ② 真给配额：全队各一次额外战技
                dp.AddTempActionQuota(this, CharacterActionType.CastSkill, 1);
                WriteLine($"[ {caster} ] 发动了催战鼓！[ {target} ] 获得 {amount} 点决策点（{before} → {dp.CurrentDecisionPoints} / {dp.MaxDecisionPoints}）与一次额外战技配额！");
            }
        }
    }
}
