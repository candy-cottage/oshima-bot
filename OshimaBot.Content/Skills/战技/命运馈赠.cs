using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 命运馈赠：应用【回合奖励】系统的「绑定角色的回合奖励」能力
    /// <para/>· 为一名友方绑定一份其未来第 1 个行动回合的奖励（<see cref="FunGame.Core.Interface.Base.IGamingQueue.AddRoundReward"/>）
    /// <para/>· 奖励内容由模块自定义（<see cref="命运之赐"/>），在目标行动回合开始时自动发放
    /// <para/>· 仅在启用「角色绑定的回合奖励」时生效
    /// </summary>
    public class 命运馈赠 : Skill
    {
        public override long Id => (long)SkillID.命运馈赠;
        public override string Name => "命运馈赠";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 45;
        public override double CD => 30;
        public override double HardnessTime { get; set; } = 5;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => true;
        public override int CanSelectTargetCount => 1;

        public 命运馈赠(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 命运馈赠特效(this));
        }
    }

    public class 命运馈赠特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"为{Skill.TargetDescription()}绑定一份其未来第 1 个行动回合的回合奖励 [ {nameof(命运之赐)} ]：该奖励会在其行动回合开始时自动发放。";
        public override string DispelDescription => "被驱散性：不可驱散";
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            foreach (Character target in ctx.Targets)
            {
                if (target.HP <= 0) continue;

                // 每次赠予都构造新实例：AddRoundReward 以实例引用去重，重复使用同一实例会被拒绝
                命运之赐 reward = new(target);
                if (GamingQueue is not null && GamingQueue.AddRoundReward(target, 1, reward))
                {
                    WriteLine($"[ {caster} ] 为 [ {target} ] 的下一行动回合绑定了一份命运的馈赠！");
                }
                else
                {
                    WriteLine($"[ {caster} ] 对 [ {target} ] 的命运馈赠未能生效（未启用角色绑定的回合奖励）。");
                }
            }
        }
    }
}
