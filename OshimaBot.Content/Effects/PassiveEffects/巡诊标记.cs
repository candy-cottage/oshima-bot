using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Content.Items;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    /// <summary>
    /// 装备【行军医箱】的监听标记 —— 穿戴时由穿戴者挂到每名队友的状态栏上。
    /// <para/>存在意义：框架中大部分细粒度钩子（如 <see cref="Effect.AfterDamageCalculation"/>）
    /// 只通知「触发者 + 目标」的特效，穿戴者无法直接监听队友的受击事件。
    /// 因此必须像【开宫】挂【长期监视】那样，把本标记挂进队友状态栏，
    /// 让标记以宿主自己的身份收到钩子，再把信号转发给穿戴者侧的【行军医箱特效】。
    /// <para/>本标记自身不产生任何收益，是内部簿记用标记，<b>不可被驱散</b>；
    /// 当装备被卸下 / 被空投替换时，由控制器统一撤回，保证幂等。
    /// </summary>
    public class 巡诊标记 : Effect
    {
        public override long Id => (long)PassiveEffectID.巡诊标记;
        public override string Name => "巡诊标记";
        public override string Description => $"此角色处于 [ {Source} ] 的行军医箱巡诊范围内。";
        public override EffectType EffectType => EffectType.Mark;

        /// <summary>友方标记，非负面效果</summary>
        public override bool IsDebuff => false;

        /// <summary>无具体时长，随装备存续</summary>
        public override bool DurativeWithoutDuration => true;

        /// <summary>内部簿记标记，不可被任何形态的驱散移除</summary>
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        public override Character Source => _sourceCharacter;

        private readonly Character _sourceCharacter;
        private readonly 行军医箱特效 _controller;

        /// <summary>标记宿主（由 <see cref="OnEffectGained"/> 捕获；标记总是通过 <see cref="Effect.AddToCharacter"/> 挂载）</summary>
        private Character? _host;

        public 巡诊标记(Skill skill, Character sourceCharacter, 行军医箱特效 controller) : base(skill)
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
            // 本标记会随宿主收到「宿主参与的所有伤害结算」，只关心宿主作为受击方的那一次
            Character? host = _host;
            if (host is null || ctx.Enemy != host) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (ctx.ActualDamage <= 0) return;
            _controller.TryEmergencyHeal(host);
        }
    }
}
