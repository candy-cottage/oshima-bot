using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 通用被动 · 辅助型【援护号令】—— 自身回合开始时的团队机动增益
    /// <para/>设计意图：五类辅助原型中的「指挥号令」。自身每次行动前把机动性分给队友。
    /// <para/>触发：自身回合开始（<see cref="Effect.OnTurnStart"/>），且号令已就绪（内部冷却）
    /// <para/>效果：为全体队友（不含自己）提升**行动系数**，持续到队友下一次出手
    /// <para/>标尺（手册 §6.4 被动·增益 ≤36%）：行动系数 +12%（Lv60）✓
    /// <para/>⚠ 平衡修订（2026-09-23，Admin 指出）：
    /// <list type="bullet">
    /// <item>**不再给行动速度**：`ActionCoefficient = SPD / SPDUpperLimit + ExActionCoefficient`（`SPDUpperLimit = 1500`），
    ///   +12 速度只等于 +0.8% 行动系数，被 +8% 淹没 10 倍 —— 二者本就是同一个指标，重复且无意义。</item>
    /// <item>**持续时间必须远短于内部冷却**：角色约 7.6 秒行动一次，原「30 秒 buff + 每次刷新」必然常驻 ⇒
    ///   改为「12 秒持续 / 35 秒冷却」，覆盖率约 1/3，语义回到"一次号令 = 一波冲锋"。</item>
    /// </list>
    /// </summary>
    public class 援护号令 : Skill
    {
        public override long Id => (long)PassiveID.援护号令;
        public override string Name => "援护号令";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 援护号令(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 援护号令特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 援护号令特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"自身回合开始时，若号令已就绪，全体队友（不含自己）提升 {行动系数提升 * 100:0.##}% 行动系数，" +
            $"持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}；" +
            $"号令每 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}至多下达一次（同名刷新，不叠加）。";

        private double 剩余冷却 = 0;

        /// <summary>行动系数提升（Lv60 = 12%）。只加 `exac`，不再叠加 `exspd` —— 见类注释。</summary>
        private double 行动系数提升 => Skill.Character != null ? 0.06 + Skill.Character.Level * 0.001 : 0.06;

        /// <summary>持续 12 秒 ≈ 覆盖队友一次出手（队友行动间隔 7–10 秒），不足以常驻</summary>
        private const double 持续时间 = 12;

        /// <summary>冷却 35 秒 ≫ 持续 12 秒 ⇒ 覆盖率约 1/3，杜绝「每次自身回合都刷新」的常驻化</summary>
        private const double 冷却时间 = 35;

        public override void OnTurnStart(TurnContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (GamingQueue == null) return;
            if (剩余冷却 > 0) return;
            List<Character> allies = [.. GamingQueue.GetTeammates(character).Where(c => c.HP > 0 && c != character)];
            if (allies.Count == 0) return;
            剩余冷却 = 冷却时间;
            WriteLine($"[ {character} ] 下达了援护号令，队友的行动系数提升了！");
            foreach (Character ally in allies)
            {
                // 同名刷新：先移除旧实例再套用，避免叠加
                List<Effect> olds = [.. ally.Effects.Where(e => e is DynamicsEffect && e.Name == nameof(援护号令) + "·号令")];
                foreach (Effect old in olds) old.RemoveFromCharacter(ally);
                Effect buff = new DynamicsEffect(Skill, new Dictionary<string, object>()
                {
                    { "exac", 行动系数提升 }
                }, character)
                {
                    Name = nameof(援护号令) + "·号令",
                    Durative = true,
                    Duration = 持续时间
                };
                buff.AddToCharacter(ally);
                GamingQueue.AddApplyEffects(ally, EffectType.Haste);
            }
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (剩余冷却 > 0) 剩余冷却 = Math.Max(0, 剩余冷却 - ctx.Elapsed);
        }
    }
}
