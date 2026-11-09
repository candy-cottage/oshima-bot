using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 通用被动 · 辅助型【同袍之誓】—— 队友阵亡触发的团队增益
    /// <para/>设计意图：五类辅助原型中的「牺牲回响」。队友倒下时全队化悲愤为力量。
    /// <para/>触发：<see cref="Effect.AfterDeathCalculation"/>（全队列广播 —— 这是辅助被动能观察到「队友出事」的少数钩子之一）
    /// <para/>效果：全体存活队友获得攻击力 / 行动系数 / 加速系数提升并回复少量生命；同名刷新，不叠加
    /// <para/>标尺（手册 §6.4 被动·减益/承伤、增益 ≤36%）：单项 10%，三项均为增益类而非永久成长 ✓
    /// </summary>
    public class 同袍之誓 : Skill
    {
        public override long Id => (long)PassiveID.同袍之誓;
        public override string Name => "同袍之誓";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 同袍之誓(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 同袍之誓特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 同袍之誓特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"每当一名队友阵亡，全体存活队友获得 {攻击提升 * 100:0.##}% 攻击力、{行动系数提升 * 100:0.##}% 行动系数与加速系数，" +
            $"并回复 {回复系数 * 100:0.##}% 最大生命值，持续 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}（同名刷新，不叠加）。";

        private double 攻击提升 => Skill.Character != null ? 0.06 + Skill.Character.Level * 0.0007 : 0.06;
        private double 行动系数提升 => Skill.Character != null ? 0.06 + Skill.Character.Level * 0.0007 : 0.06;
        private double 回复系数 => 0.05;
        private const double 持续时间 = 20;

        public override void AfterDeathCalculation(DeathContext ctx)
        {
            if (ctx.Trigger is not Character died) return;
            if (Skill.Character == null) return;
            Character self = Skill.Character;
            if (died == self) return;
            // 召唤物/随从不计
            if (ctx.HasMaster) return;
            if (GamingQueue == null) return;
            if (!GamingQueue.IsTeammate(self, died)) return;
            WriteLine($"[ {self} ] 的同袍之誓因 [ {died} ] 的阵亡而燃起！全体存活队友获得增益！");
            foreach (Character ally in GamingQueue.GetTeammates(self).Where(c => c.HP > 0 && c != self))
            {
                // 同名刷新：先移除旧实例再套用，避免叠加
                List<Effect> olds = [.. ally.Effects.Where(e => e is DynamicsEffect && e.Name == nameof(同袍之誓) + "·誓约")];
                foreach (Effect old in olds) old.RemoveFromCharacter(ally);
                Effect buff = new DynamicsEffect(Skill, new Dictionary<string, object>()
                {
                    { "exatk2", 攻击提升 },
                    { "exac", 行动系数提升 },
                    { "exacc", 行动系数提升 }
                }, self)
                {
                    Name = nameof(同袍之誓) + "·誓约",
                    Durative = true,
                    Duration = 持续时间
                };
                buff.AddToCharacter(ally);
                double heal = ally.MaxHP * 回复系数;
                HealToTarget(self, ally, heal);
            }
        }
    }
}
