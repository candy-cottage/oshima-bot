using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 不灭之握 : Skill
    {
        public override long Id => (long)PassiveID.不灭之握;
        public override string Name => "不灭之握";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        /// <summary>
        /// 永久提升的最大生命值累计值。[ 永久成长数据存放在技能本体上 ]：死亡/复活时角色会被重建，
        /// 依赖 <see cref="OnCharacterRespawn(Skill)"/> 把累计值迁移给新技能实例，特效重挂后全量施加。
        /// </summary>
        public double 永久提升累计 { get; set; } = 0;

        public 不灭之握(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 不灭之握特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }

        public override void OnCharacterRespawn(Skill newSkill)
        {
            if (newSkill is 不灭之握 s)
            {
                s.永久提升累计 = 永久提升累计;
            }
        }
    }

    public class 不灭之握特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"每 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}，下一次普通攻击将额外造成自身最大生命值 {伤害系数 * 100:0.##}% [ {Skill.Character?.MaxHP * 伤害系数:0.##} ] 的伤害，并回复自身 {回复系数 * 100:0.##}% [ {Skill.Character?.MaxHP * 回复系数:0.##} ] 最大生命值，同时永久提升 {永久生命提升:0.##} 点最大生命值（累计上限 {永久提升上限:0.##} 点，为基础生命值的 10%）。" +
            (CSkill?.永久提升累计 > 0 ? $"（当前已累计提升 {CSkill.永久提升累计:0.##} 点最大生命值）" : "");

        public 不灭之握? CSkill => Skill as 不灭之握;

        private double 已应用 = 0;
        private double 剩余冷却 = 0;

        private double 冷却时间 => Skill.Character != null ? 8 - Skill.Character.Level * 0.03 : 8;
        private double 伤害系数 => Skill.Character != null ? 0.04 + Skill.Character.Level * 0.0006 : 0.04;
        private double 回复系数 => Skill.Character != null ? 0.02 + Skill.Character.Level * 0.0003 : 0.02;
        private double 永久生命提升 => Skill.Character != null ? 8 + Skill.Character.Level * 0.3 : 8;

        /// <summary>
        /// 重标：永久提升的硬上限 = 基础生命值(BaseHP) 的 10%（原为无上限永久成长）
        /// </summary>
        private double 永久提升上限 => Skill.Character != null ? Skill.Character.BaseHP * 0.1 : 0;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            // 复活/重挂时：把技能本体上继承到的累计值整体施加一次
            刷新(character);
        }

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (!ctx.IsNormalAttack) return;
            if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
            if (剩余冷却 > 0) return;
            剩余冷却 = 冷却时间;
            double 额外伤害 = character.MaxHP * 伤害系数;
            double 回复量 = character.MaxHP * 回复系数;
            double 永久提升 = 永久生命提升;
            if (CSkill != null)
            {
                // 重标：受「基础生命值 10%」硬上限约束，超出后不再累积
                double 剩余额度 = Math.Max(0, 永久提升上限 - CSkill.永久提升累计);
                永久提升 = Math.Min(永久提升, 剩余额度);
                CSkill.永久提升累计 += 永久提升;
            }
            刷新(character);
            WriteLine($"[ {character} ] 发动了不灭之握！额外造成 {额外伤害:0.##} 点伤害，回复 {回复量:0.##} 点生命值，并永久提升了 {永久提升:0.##} 点最大生命值（累计 {CSkill?.永久提升累计:0.##} 点）！");
            HealToTarget(character, character, 回复量);
            DamageToEnemy(character, enemy, ctx.DamageType, ctx.MagicType, 额外伤害, new(character)
            {
                TriggerEffects = false
            });
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (剩余冷却 > 0)
            {
                剩余冷却 -= ctx.Elapsed;
            }
            // 自愈式幂等刷新：复活等场景下若 OnEffectGained 未重放，首个时间流逝即补挂全量累计
            刷新(character);
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (已应用 <= 0) return;
            // 只回收本实例已施加的加成；累计值保留在技能本体上，交由复活迁移/重挂后重新施加
            character.ExHP2 -= 已应用;
            已应用 = 0;
        }

        /// <summary>
        /// 以技能本体的累计值为准做幂等刷新：差值增量施加，避免重复叠加或漏加。
        /// </summary>
        private void 刷新(Character character)
        {
            double target = CSkill?.永久提升累计 ?? 0;
            double delta = target - 已应用;
            if (Math.Abs(delta) < 1e-9) return;
            character.ExHP2 += delta;
            已应用 = target;
        }
    }
}
