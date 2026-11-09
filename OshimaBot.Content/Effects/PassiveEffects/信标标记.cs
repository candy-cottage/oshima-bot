using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    /// <summary>
    /// 装备【接力信标】的指引标记 —— 装备期间由穿戴者挂到每名队友的状态栏上。
    /// <para/>与前两个标记不同，本标记<b>自身即收益</b>：硬直钩子只通知「行动者自己」的特效，
    /// 因此把标记挂在队友身上，等价于把"硬直减免"当作一层光环施加给队友。
    /// <para/>无状态增减（只在钩子里返回比率），因此天然幂等；
    /// 卸装 / 换装时由【接力信标特效】撤回，撤销光环。
    /// </summary>
    public class 信标标记 : Effect
    {
        public override long Id => (long)PassiveEffectID.信标标记;
        public override string Name => "信标标记";
        public override string Description => $"此角色正受 [ {Source} ] 的接力信标指引，普攻与技能硬直减少 {_硬直减免 * 100:0.##}%。";
        public override EffectType EffectType => EffectType.Mark;

        /// <summary>友方标记，非负面效果</summary>
        public override bool IsDebuff => false;

        /// <summary>无具体时长，随装备存续</summary>
        public override bool DurativeWithoutDuration => true;

        /// <summary>内部簿记标记，不可被任何形态的驱散移除</summary>
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        public override Character Source => _sourceCharacter;

        private readonly Character _sourceCharacter;
        private readonly double _硬直减免;

        /// <summary>标记宿主（由 <see cref="OnEffectGained"/> 捕获；标记总是通过 <see cref="Effect.AddToCharacter"/> 挂载）</summary>
        private Character? _host;

        public 信标标记(Skill skill, Character sourceCharacter, double 硬直减免) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _sourceCharacter = sourceCharacter;
            _硬直减免 = 硬直减免;
        }

        public override void OnEffectGained(HookContext ctx)
        {
            _host = ctx.Trigger;
        }

        public override AlterHardnessTimeResult AlterHardnessTimeAfterNormalAttack(HardnessContext ctx)
        {
            return 计算(ctx);
        }

        public override AlterHardnessTimeResult AlterHardnessTimeAfterCastSkill(HardnessContext ctx)
        {
            return 计算(ctx);
        }

        private AlterHardnessTimeResult 计算(HardnessContext ctx)
        {
            // 硬直钩子按「行动者的特效」分发，本标记只可能挂在行动中的队友身上；仍做一次归属校验
            if (_host is null || ctx.Trigger != _host) return default;
            // Factor 语义：最终硬直 = 基础硬直 × (1 + Factor)，故"减少 N%"传 -N；default(0) 即不干预
            return new AlterHardnessTimeResult { Factor = -_硬直减免 };
        }
    }
}
