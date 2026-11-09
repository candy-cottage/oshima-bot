using FunGame.Core.Entity;
using OshimaBot.Content.Effects;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 十二宫星环：为目标友方附加【强运】被动状态
    /// <para/>· 作用角色数随等级提升：1 / 2 / 3 / 4
    /// <para/>· 强运持续回合数随等级提升：2 / 3 / 4 / 4
    /// <para/>· 强运状态下，角色每个回合结束时随机生成 2~3 份绑定自身的回合奖励
    /// </summary>
    public class 十二宫星环 : Skill
    {
        public override long Id => (long)MagicID.十二宫星环;
        public override string Name => "十二宫星环";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double MPCost => Level > 0 ? 70 + 75 * (Level - 1) : 70;
        public override double CD => Level > 0 ? 50 - (1 * (Level - 1)) : 50;
        public override double CastTime => Level > 0 ? 4 + (0.5 * (Level - 1)) : 4;
        public override double HardnessTime { get; set; } = 5;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => true;
        public override int CanSelectTargetCount
        {
            get
            {
                return Level switch
                {
                    2 => 2,
                    3 => 3,
                    >= 4 => 4,
                    _ => 1
                };
            }
        }

        /// <summary>
        /// 魔法瓶颈（2026-09-23 修订）：此前设为 0（= 放弃该参数，效能恒 100%），
        /// 原因是本技能只施加【强运】状态、不吃效能乘算。
        /// <para/>现改为**把效能当命中率系数**使用（见特效的命中检定）⇒ 需要给出瓶颈才能算出效能：
        /// 效能 = `clamp(1 + (施法者INT − 瓶颈) / 瓶颈, 0.01, 2.0)`，命中率 = 效能 × 0.5。
        /// 取手册的「L8 主流值 103」⇒ 施法者 INT = 103 时命中 50%、INT ≥ 206 时必中。
        /// </summary>
        public override double MagicBottleneck => 103;

        public 十二宫星环(Character? character = null) : base(SkillType.Magic, character)
        {
            Effects.Add(new 十二宫星环特效(this));
        }
    }

    public class 十二宫星环特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"为{(Skill.CanSelectTargetCount > 1 ? $"至多 {Skill.CanSelectTargetCount} 个" : "")}友方角色附加【强运】状态，持续 {持续回合} 回合；" +
            $"强运状态下，角色每个回合结束时随机生成 {强运.最小生成数量}~{强运.最大生成数量} 份绑定自身的回合奖励。";
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        /// <summary>持续回合：2 / 3 / 4 / 4（随等级提升，最高 4）</summary>
        private int 持续回合 => Math.Min(4, Math.Max(2, 2 + (Skill.Level - 1)));

        /// <summary>
        /// 命中率：统一走 <see cref="EfficacyHit"/>（2026-09-23 统一口径）。
        /// <para/>纯机制魔法没有自身概率 ⇒ 取默认基础概率 0.5、成长 0
        /// ⇒ 效能 200% 必中 / 100% 命中 50% / →0% →0%。
        /// </summary>
        private double 命中率 => EfficacyHit.命中率(Skill);

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            foreach (Character target in ctx.Targets)
            {
                if (target.HP <= 0) continue;
                if (!命中检定(caster, target)) continue;

                强运 e = new(Skill, target, caster, false, 0, 持续回合);
                target.Effects.Add(e);
                e.OnEffectGained(new HookContext(GamingQueue, target));
                WriteLine($"[ {target} ] 被十二宫星环笼罩，获得【强运】状态！持续 {持续回合} 回合！");
            }
        }

        /// <summary>命中检定：统一走 <see cref="EfficacyHit.检定"/>（效能够高时必中且不消耗随机数）。</summary>
        private bool 命中检定(Character caster, Character target)
        {
            double rate = 命中率;
            string info = EfficacyHit.文本(Skill, rate);
            if (!EfficacyHit.检定(rate, Random))
            {
                WriteLine($"[ {caster} ] 的十二宫星环未能笼罩 [ {target} ]，被抵抗了！（{info}）");
                return false;
            }
            WriteLine($"[ {caster} ] 的十二宫星环命中了 [ {target} ]（{info}）。");
            return true;
        }
    }
}
