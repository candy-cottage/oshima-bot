using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;

namespace OshimaBot.Content.Skills
{
    public class 心灵之弦 : Skill
    {
        public override long Id => (long)PassiveID.心灵之弦;
        public override string Name => "心灵之弦";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";

        public 心灵之弦(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 心灵之弦特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 心灵之弦特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"普通攻击硬直时间减少 20%。每次使用普通攻击时，额外再发动一次普通攻击，伤害特效可叠加，但伤害折减一半，冷却 {基础冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}。额外普通攻击立即发动，不占用决策点配额。" +
            (冷却时间 > 0 ? $"（正在冷却：剩余 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}）" : "");

        public double 冷却时间 { get; set; } = 0;
        public double 基础冷却时间 { get; set; } = 10;
        private bool 是否是嵌套普通攻击 = false;

        public override AlterActualDamageResult AlterActualDamageAfterCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return default;
            double damage = ctx.Damage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageType damageType = ctx.DamageType;
            MagicType magicType = ctx.MagicType;
            DamageResult damageResult = ctx.DamageResult;
            Dictionary<Effect, double> totalDamageBonus = ctx.TotalDamageBonus;
            if (character == Skill.Character && 是否是嵌套普通攻击 && isNormalAttack && damage > 0)
            {
                return new AlterActualDamageResult { DamageDelta = -(damage / 2) };

            }
            return default;

        }

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            double damage = ctx.Damage;
            double actualDamage = ctx.ActualDamage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageType damageType = ctx.DamageType;
            MagicType magicType = ctx.MagicType;
            DamageResult damageResult = ctx.DamageResult;
            if (character == Skill.Character && isNormalAttack && 冷却时间 == 0 && !是否是嵌套普通攻击 && GamingQueue != null && enemy.HP > 0)
            {
                WriteLine($"[ {character} ] 发动了心灵之弦！额外进行一次普通攻击！");
                冷却时间 = 基础冷却时间;
                是否是嵌套普通攻击 = true;
                character.NormalAttack.Attack(GamingQueue, character, null, enemy);
            }

            if (character == Skill.Character && 是否是嵌套普通攻击)
            {
                是否是嵌套普通攻击 = false;
            }
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            double elapsed = ctx.Elapsed;
            if (冷却时间 > 0)
            {
                冷却时间 -= elapsed;
                if (冷却时间 <= 0)
                {
                    冷却时间 = 0;
                }
            }
        }

        public override AlterHardnessTimeResult AlterHardnessTimeAfterNormalAttack(HardnessContext ctx)
        {
            if (ctx.Trigger is not Character character) return default;
            return new AlterHardnessTimeResult { Factor = -0.2 };
        }
    }
}
