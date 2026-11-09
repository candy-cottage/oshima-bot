using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 通用被动 · 辅助型【战地巡诊】—— 周期治疗 + 净化
    /// <para/>设计意图：五类辅助原型中的「战场医疗」。不依赖任何事件，纯周期巡视，最稳定的一种辅助。
    /// <para/>触发：时间流逝（<see cref="Effect.OnTimeElapsed"/>），每 40 秒巡视一次
    /// <para/>效果：为生命值最低的队友回复生命值；若其处于重伤（低于阈值）则额外弱驱散其减益
    /// <para/>标尺（手册 §6.4 被动·回复 ≤2–3%/秒）：8.4% 最大生命值 / 40 秒 ⇒ 约 0.21%/秒 ✓
    /// </summary>
    public class 战地巡诊 : Skill
    {
        public override long Id => (long)PassiveID.战地巡诊;
        public override string Name => "战地巡诊";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 战地巡诊(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 战地巡诊特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 战地巡诊特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        /// <summary>
        /// 声明弱驱散能力：<see cref="Effect.Dispel"/> 在 <see cref="DispelType"/> == None 时会直接返回
        /// </summary>
        public override DispelType DispelType => DispelType.Weak;
        public override string Description =>
            $"每 {巡诊间隔:0.##} {GameplayEquilibriumConstant.InGameTime}为生命值最低的队友回复 {回复系数 * 100:0.##}% 最大生命值；" +
            $"若该队友生命值低于 {重伤阈值 * 100:0.##}%，额外对其执行一次弱驱散（清除减益）。";

        private double 剩余间隔 = 0;

        private double 回复系数 => Skill.Character != null ? 0.06 + Skill.Character.Level * 0.0004 : 0.06;
        private double 重伤阈值 => 0.35;
        /// <summary>
        /// 巡诊间隔：30 → 40 秒（2026-09-23 平衡修订，Admin 指出 30 秒一次「稍廉价」）。
        /// 8.4% 最大生命值 / 40 秒 ⇒ 约 0.21%/秒，比 2–3%/秒 的上限低一个数量级。
        /// </summary>
        private const double 巡诊间隔 = 40;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            剩余间隔 = 巡诊间隔;
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            剩余间隔 -= ctx.Elapsed;
            if (剩余间隔 > 0) return;
            Character? target = 最低生命队友(character);
            if (target == null)
            {
                剩余间隔 = 巡诊间隔;
                return;
            }
            剩余间隔 = 巡诊间隔;
            double heal = target.MaxHP * 回复系数;
            WriteLine($"[ {character} ] 展开了战地巡诊，为 [ {target} ] 回复 {heal:0.##} 点生命值！");
            HealToTarget(character, target, heal);
            if (target.HP / Math.Max(1, target.MaxHP) < 重伤阈值)
            {
                WriteLine($"[ {character} ] 的战地巡诊对重伤的 [ {target} ] 执行了弱驱散！");
                Dispel(character, target, false);
            }
        }

        /// <summary>
        /// 生命值百分比最低的存活队友；若其生命值已接近满值（≥<see cref="有效治疗阈值"/>）则视为无需治疗，返回 null
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

        /// <summary>有效治疗阈值：队友生命值低于该比例才展开巡诊，避免满血空放</summary>
        private const double 有效治疗阈值 = 0.95;
    }
}
