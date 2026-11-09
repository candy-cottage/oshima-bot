using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 迅捷步法 : Skill
    {
        public override long Id => (long)PassiveID.迅捷步法;
        public override string Name => "迅捷步法";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 迅捷步法(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 迅捷步法特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 迅捷步法特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"每次造成伤害后，立即回复 {生命回复:0.##} 点生命值，随后在 {持续时间:0.##} {GameplayEquilibriumConstant.InGameTime}内，提升 {行动系数提升 * 100:0.##}% 行动系数和 {加速系数提升 * 100:0.##}% 加速系数、1 格移动距离，该效果最多可叠 3 层。";

        private double 行动系数提升 => Skill.Character != null ? 0.02 + Skill.Character.Level / 10 * 0.005 : 0.02;
        private double 加速系数提升 => Skill.Character != null ? 0.02 + Skill.Character.Level / 10 * 0.005 : 0.02;
        // 平衡调整（2026-09-22）：Lv60 回复 380 → 320（Admin：砍 60）
        private double 生命回复 => Skill.Character != null ? 20 + Skill.Character.Level * 5 : 20;
        private double 持续时间 => Skill.Character != null ? 10 + Skill.Character.Level * 0.1 : 10;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            double damage = ctx.Damage;
            double actualDamage = ctx.ActualDamage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageType damageType = ctx.DamageType;
            MagicType magicType = ctx.MagicType;
            DamageResult damageResult = ctx.DamageResult;
            if (Skill.Character != null && Skill.Character == character && isNormalAttack && (damageResult == DamageResult.Normal || damageResult == DamageResult.Critical))
            {
                HealToTarget(character, character, 生命回复);
                if (character.Effects.Count(e => e.Name == nameof(迅捷步法特效)) < 3)
                {
                    WriteLine($"[ {character} ] 发动了迅捷步法，提升了 {行动系数提升 * 100:0.##}% 行动系数和 {加速系数提升 * 100:0.##}% 加速系数、1 格移动距离！");
                    Effect e = new DynamicsEffect(Skill, new Dictionary<string, object>()
                    {
                        { "exac", 行动系数提升 },
                        { "exacc", 加速系数提升 },
                        { "exmov", 1 }
                    }, character)
                    {
                        Name = nameof(迅捷步法特效),
                        Durative = true,
                        Duration = 持续时间
                    };
                    character.Effects.Add(e);
                    e.OnEffectGained(new HookContext(GamingQueue, character));
                    e.IsDebuff = false;
                    RecordCharacterApplyEffects(character, EffectType.Haste);
                }
            }
        }
    }
}