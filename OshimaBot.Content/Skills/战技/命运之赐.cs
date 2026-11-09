using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 命运之赐：由 <see cref="命运馈赠"/> 绑定到友方未来行动回合的【回合奖励】内容
    /// <para/>· 作为主动奖励被发放时，由队列立即释放（<see cref="FunGame.Core.Model.Queue.GamingQueue"/> 的 <c>BindAndRelease</c>）
    /// <para/>· 效果：回复自身最大生命值，并获得一段时间的攻击力提升
    /// </summary>
    public class 命运之赐 : Skill
    {
        public override long Id => (long)SkillID.命运之赐;
        public override string Name => "命运之赐";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 命运之赐(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 命运之赐特效(this));
        }
    }

    public class 命运之赐特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"立即回复自身 {回复比例 * 100:0.##}% 最大生命值，并获得 {攻击提升 * 100:0.##}% 攻击力，持续 {持续回合} 回合。";
        public override string DispelDescription => "被驱散性：不可驱散";
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        private double 回复比例 => 0.15;
        private double 攻击提升 => 0.15;
        private int 持续回合 => 2;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character owner) return;

            double heal = owner.MaxHP * 回复比例;
            HealToTarget(owner, owner, heal);

            ExATK2 攻击加成 = new(Skill, new() { { "exatk", 攻击提升 } }, owner)
            {
                Name = Name,
                Durative = false,
                DurationTurn = 持续回合,
                EffectType = EffectType.DamageBoost
            };
            攻击加成.AddToCharacter(owner);
            GamingQueue?.AddApplyEffects(owner, EffectType.DamageBoost);

            WriteLine($"[ {owner} ] 获得了命运之赐：回复了 {heal:0.##} 点生命值，攻击力提升 {攻击提升 * 100:0.##}% ，持续 {持续回合} 回合！");
        }
    }
}
