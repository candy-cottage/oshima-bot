using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 抢夺命运：应用【回合奖励】系统的「角色可相互抢夺」能力
    /// <para/>· 夺取目标未来第 1 个行动回合的全部回合奖励，并入自身未来第 1 个行动回合
    /// <para/>· 调用 <see cref="FunGame.Core.Interface.Base.IGamingQueue.StealRoundReward"/>，被夺取项会连同归属一起改写为夺取者
    /// <para/>· 目标身上没有可夺取的奖励时，改为对其造成魔法伤害，保证技能不空放
    /// <para/>· 仅在启用「角色绑定的回合奖励」（<c>InitRoundRewards(..., bindToCharacter: true, ...)</c>）时才能夺取
    /// </summary>
    public class 抢夺命运 : Skill
    {
        public override long Id => (long)SkillID.抢夺命运;
        public override string Name => "抢夺命运";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 50;
        public override double CD => 35;
        public override double HardnessTime { get; set; } = 6;
        public override bool CanSelectSelf => false;
        public override bool CanSelectEnemy => true;
        public override bool CanSelectTeammate => false;
        public override int CanSelectTargetCount => 1;

        public 抢夺命运(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 抢夺命运特效(this));
        }
    }

    public class 抢夺命运特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"夺取{Skill.TargetDescription()}未来第 1 个行动回合的全部回合奖励，并入自身未来第 1 个行动回合；" +
            $"若目标没有可夺取的回合奖励，则对其造成 {伤害系数 * 100:0.##}% 攻击力 [ {Skill.Character?.BaseATK * 伤害系数:0.##} ] 点魔法伤害。";
        public override string DispelDescription => "被驱散性：不可驱散";
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        /// <summary>
        /// 夺取失败时的补偿伤害系数（基于攻击力，无基础伤害）
        /// <para/>· 平衡调整（2026-09-22）：L6 由 245% 降至 125%，对齐战技单体上限「≤1.2×普攻」。
        /// 实测：原 1.47×普攻（超上限 23%），目标 ≤1.20×普攻。
        /// </summary>
        // 调整（二轮）：L6 125% → 110%（留 ±5% 测量噪声余量）
        private double 伤害系数 => Level > 0 ? 0.9 + 0.04 * (Level - 1) : 0.9;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            foreach (Character target in ctx.Targets)
            {
                if (target.HP <= 0) continue;

                List<Skill> stolenItems = [];
                if (GamingQueue is not null
                    && GamingQueue.StealRoundReward(target, 1, caster, 1, out stolenItems)
                    && stolenItems.Count > 0)
                {
                    WriteLine($"[ {caster} ] 夺取了 [ {target} ] 下个行动回合的回合奖励！");
                    foreach (Skill reward in stolenItems)
                    {
                        WriteLine($"　└ 已将 [ {reward.Name} ] 转入 [ {caster} ] 的未来回合奖励");
                    }
                }
                else
                {
                    WriteLine($"[ {caster} ] 未能在 [ {target} ] 身上夺取到回合奖励，转而发动攻击！");
                    DamageToEnemy(caster, target, DamageType.Magical, MagicType.None, caster.ATK * 伤害系数);
                }
            }
        }
    }
}
