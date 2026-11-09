using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Content.Items;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    /// <summary>
    /// 装备【共鸣核心】的监听标记 —— 穿戴时由穿戴者挂到每名队友的状态栏上。
    /// <para/>与【巡诊标记】同理：框架中大部分细粒度钩子只通知「触发者 + 目标」的特效，
    /// 穿戴者无法直接监听队友的出手，必须把本标记挂进队友状态栏，
    /// 让标记以宿主的身份收到 <see cref="Effect.AfterDamageCalculation"/>，再转发给【共鸣核心特效】。
    /// <para/>本标记自身不产生任何收益，是内部簿记用标记，不可被驱散；
    /// 卸装 / 换装时由控制器统一撤回，保证幂等。
    /// </summary>
    public class 共鸣标记 : Effect
    {
        public override long Id => (long)PassiveEffectID.共鸣标记;
        public override string Name => "共鸣标记";
        public override string Description => $"此角色与 [ {Source} ] 的共鸣核心产生了共鸣。";
        public override EffectType EffectType => EffectType.Mark;

        /// <summary>友方标记，非负面效果</summary>
        public override bool IsDebuff => false;

        /// <summary>无具体时长，随装备存续</summary>
        public override bool DurativeWithoutDuration => true;

        /// <summary>内部簿记标记，不可被任何形态的驱散移除</summary>
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        public override Character Source => _sourceCharacter;

        private readonly Character _sourceCharacter;
        private readonly 共鸣核心特效 _controller;

        /// <summary>标记宿主（由 <see cref="OnEffectGained"/> 捕获；标记总是通过 <see cref="Effect.AddToCharacter"/> 挂载）</summary>
        private Character? _host;

        public 共鸣标记(Skill skill, Character sourceCharacter, 共鸣核心特效 controller) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _sourceCharacter = sourceCharacter;
            _controller = controller;
        }

        public override void OnEffectGained(HookContext ctx)
        {
            _host = ctx.Trigger;
        }

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            // 本标记会随宿主收到「宿主参与的所有伤害结算」，只关心宿主作为攻击方造成伤害的那一次
            Character? host = _host;
            if (host is null || ctx.Trigger != host) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (ctx.ActualDamage <= 0) return;
            _controller.TryGainEnergy(host);
        }
    }
}
