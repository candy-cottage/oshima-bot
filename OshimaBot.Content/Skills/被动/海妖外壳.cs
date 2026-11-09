using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;

namespace OshimaBot.Content.Skills
{
    public class 海妖外壳 : Skill
    {
        public override long Id => (long)PassiveID.海妖外壳;
        public override string Name => "海妖外壳";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 海妖外壳(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 海妖外壳特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 海妖外壳特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        /// <summary>
        /// 强驱散：护盾破碎时对自身执行强驱散（参照 <see cref="Effects.SkillEffects.强驱散特效"/> 的规范写法，
        /// 由框架按 <see cref="DispelledType"/> 与 BlockDispel 钩子处理，不手动移除特效）
        /// </summary>
        public override DispelType DispelType => DispelType.Strong;
        public override string Description => $"每 {再生间隔:0.##}{GameplayEquilibriumConstant.InGameTime}生成一次 {护盾值:0.##} 点混合护盾（自身护盾破碎后才再生）；" +
            $"护盾破碎时，立即强驱散自身身上的减益效果（净化冷却 {净化冷却:0.##} {GameplayEquilibriumConstant.InGameTime}）。";

        private double 剩余再生 = 0;
        private bool 有壳 = false;
        private double 剩余净化冷却 = 0;

        /// <summary>
        /// 重标：破碎净化的内部冷却（20–30 秒区间，取 25）
        /// </summary>
        private const double 净化冷却 = 25;

        // 重标：再生间隔固定 35 秒（原 12-0.1×Level，L58 = 6.2 秒，不足一个行动回合；参考饼干配送 38 秒）
        private double 再生间隔 => 35;
        // 重标：护盾量 = 15% 最大生命值（原固定数值 120+12×Level，跨等级与跨角色占比失衡）
        private double 护盾值 => Skill.Character != null ? Skill.Character.MaxHP * 护盾百分比 : 0;
        private const double 护盾百分比 = 0.15;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            剩余再生 = 再生间隔;
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            剩余再生 -= ctx.Elapsed;
            剩余净化冷却 = Math.Max(0, 剩余净化冷却 - ctx.Elapsed);
            if (剩余再生 <= 0 && !有壳)
            {
                有壳 = true;
                剩余再生 = 再生间隔;
                WriteLine($"[ {character} ] 的海妖外壳生成了一层 {护盾值:0.##} 点混合护盾！");
                character.Shield.Mix += 护盾值;
                GamingQueue?.AddApplyEffects(character, EffectType.Shield);
            }
        }

        public override OnShieldBrokenResult OnShieldBroken(ShieldContext ctx)
        {
            if (ctx.Trigger is not Character character) return default;
            if (Skill.Character == null || Skill.Character != character) return default;
            if (!有壳) return default;
            有壳 = false;
            if (剩余净化冷却 > 0)
            {
                WriteLine($"[ {character} ] 的海妖外壳破碎了！净化效果冷却中，剩余 {剩余净化冷却:0.##} {GameplayEquilibriumConstant.InGameTime}。");
                return default;
            }
            // 强驱散：交给框架处理（isEnemy=false → 清除自身减益），自动尊重 DispelledType 与 BlockDispel 钩子（受内部冷却限制）
            Dispel(character, character, false);
            剩余净化冷却 = 净化冷却;
            WriteLine($"[ {character} ] 的海妖外壳破碎了，减益效果被清除！净化进入冷却 {净化冷却:0.##} {GameplayEquilibriumConstant.InGameTime}。");
            return default;
        }
    }
}
