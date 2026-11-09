using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;
using OshimaBot.Content.Effects.SkillEffects;

namespace OshimaBot.Content.Skills
{
    public class 导力装甲 : Skill
    {
        public override long Id => (long)SkillID.导力装甲;
        public override string Name => "导力装甲";
        public override string Description => Effects.Count > 0 ? ((导力装甲特效)Effects.First()).通用描述 : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 80;
        public override double CD => 60;
        public override double HardnessTime { get; set; } = 6;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => false;

        public 导力装甲(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 导力装甲特效(this));
        }
    }

    public class 导力装甲特效 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string DispelDescription => "被驱散性：不可驱散";
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;
        public override EffectType EffectType => EffectType.DefenseBoost;

        // 必须声明持续回合，否则队列不会递减 RemainDurationTurn，装甲状态将永不自然到期
        public override bool Durative => false;
        public override int DurationTurn => 持续回合;

        /// <summary>
        /// 状态栏描述，随装甲状态动态切换：生效中为 <see cref="装甲状态描述"/>，未启动/已解除为 <see cref="通用描述"/>
        /// </summary>
        public override string Description { get; set; } = "";

        /// <summary>
        /// 技能卡描述，固定为释放前景文案
        /// </summary>
        public string 通用描述 => $"启动导力装甲：提升自身 {攻击提升 * 100:0.##}% 攻击力、{护甲提升 * 100:0.##}% 物理护甲和{魔抗提升 * 100:0.##}% 魔法抗性，并立即缩短自身 35% 的行动等待时间，持续 {持续回合} 回合。装甲期间自身的行动硬直大幅缩短。";

        /// <summary>
        /// 装甲生效期间的状态栏描述
        /// </summary>
        public string 装甲状态描述 => $"该角色处于导力装甲状态：提升自身 {攻击提升 * 100:0.##}% 攻击力、{护甲提升 * 100:0.##}% 物理护甲和{魔抗提升 * 100:0.##}% 魔法抗性，持续 {持续回合} 回合，行动硬直大幅缩短。";

        private double 攻击提升 => Level > 0 ? 0.35 + 0.05 * (Level - 1) : 0.35;
        private double 护甲提升 => Level > 0 ? 0.35 + 0.05 * (Level - 1) : 0.35;
        private double 魔抗提升 => Level > 0 ? 0.35 + 0.05 * (Level - 1) : 0.35;
        private int 持续回合 => 4;

        /// <summary>
        /// 属性加成由本特效自行结算，不使用 ExATK2 / ExDEF2 / ExMDF 等外部效果：
        /// 那些效果的 OnEffectLost 是直接扣减常量、非幂等，一旦被重复触发就会把属性多扣一份。
        /// 这里记录实际生效的数值并配合标志位，保证重复触发时增减都只结算一次。
        /// </summary>
        private bool 加成已应用 = false;
        private double 已应用攻击提升 = 0;
        private double 已应用护甲提升 = 0;
        private double 已应用魔抗提升 = 0;

        public 导力装甲特效(Skill skill) : base(skill)
        {
            Description = 通用描述;
        }

        /// <summary>
        /// 应用属性加成，重复调用只生效一次
        /// </summary>
        private void 应用加成(Character caster)
        {
            if (加成已应用) return;
            已应用攻击提升 = 攻击提升;
            已应用护甲提升 = 护甲提升;
            已应用魔抗提升 = 魔抗提升;
            caster.ExATKPercentage += 已应用攻击提升;
            caster.ExDEFPercentage += 已应用护甲提升;
            caster.MDF[MagicType.None] += 已应用魔抗提升;
            加成已应用 = true;
        }

        /// <summary>
        /// 还原属性加成，重复调用只生效一次
        /// </summary>
        private void 还原加成(Character caster)
        {
            if (!加成已应用) return;
            caster.ExATKPercentage -= 已应用攻击提升;
            caster.ExDEFPercentage -= 已应用护甲提升;
            caster.MDF[MagicType.None] -= 已应用魔抗提升;
            已应用攻击提升 = 0;
            已应用护甲提升 = 0;
            已应用魔抗提升 = 0;
            加成已应用 = false;
        }

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            if (!caster.Skills.Any(s => s is 回复原状))
            {
                caster.Skills.Add(new 回复原状(caster)
                {
                    Level = Skill.Level,
                    Source = SkillSource.Reward
                });
            }
            foreach (Skill dlzj in caster.Skills.Where(s => s is 导力装甲))
            {
                dlzj.Enable = false;
                dlzj.IsInEffect = true;
            }
            // 封技是标准的控制状态效果，仍交给框架效果处理
            造成封技 e = new(Skill, durationTurn: 持续回合)
            {
                DurativeWithoutDuration = true,
                DispelledType = DispelledType.CannotBeDispelled
            };
            e.Activate(caster, [caster]);
            WriteLine($"[ {caster} ] 启动了导力装甲！");
            应用加成(caster);
            GamingQueue?.AddApplyEffects(caster, EffectType.DamageBoost, EffectType.DefenseBoost);
            GamingQueue?.ChangeCharacterHardnessTime(caster, -0.35, true, false);
            Description = 装甲状态描述;
        }

        /// <summary>
        /// 提前解除装甲：移除封技状态，属性还原与技能状态恢复由 <see cref="OnEffectLost"/> 统一处理
        /// </summary>
        public void 解除装甲(Character caster)
        {
            List<Effect> armored = [.. caster.Effects.Where(e => e != this && e is 封技 fj && fj.Skill is 导力装甲 && fj.Source == caster)];
            foreach (Effect e in armored)
            {
                e.RemoveFromCharacter(caster);
            }
            RemoveFromCharacter(caster);
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            还原加成(caster);
            foreach (Skill dlzj in caster.Skills.Where(s => s is 导力装甲))
            {
                if (dlzj.CurrentCD == 0)
                {
                    dlzj.Enable = true;
                }
                dlzj.IsInEffect = false;
            }
            // 注意：此处不能移除导力装甲技能本体，否则角色将永久失去该技能
            caster.Skills.RemoveWhere(s => s is 回复原状);
            Description = 通用描述;
            WriteLine($"[ {caster} ] 解除了导力装甲，恢复了通常的战斗姿态！");
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            RemainDurationTurn = 持续回合;
            if (!caster.Effects.Contains(this))
            {
                AddToCharacter(caster);
            }
        }
    }
}
