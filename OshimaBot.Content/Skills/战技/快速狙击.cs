using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 快速狙击 : Skill
    {
        public override long Id => (long)SkillID.快速狙击;
        public override string Name => "快速狙击";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override double EPCost => 60;
        public override double CD => 18;
        // 平衡调整（2026-09-22）：硬直 6 → 7（战技区间 7–10）
        public override double HardnessTime { get; set; } = 7;

        public 快速狙击(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 9;
            Effects.Add(new 快速狙击特效(this));
        }
    }

    public class 快速狙击特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"随机对{Skill.TargetDescription()}造成 {BaseDamage:0.##} + {ATKCoefficient * 100:0.##}% 攻击力 [ {Damage:0.##} ] 点物理伤害。";

        // 调整：L6 基础值 410→375，实测 1.26×普攻 压至 ≤1.20
        // 调整：L6 基础值 410→325（留噪声余量，目标 ≤1.13×普攻）
        private double BaseDamage => Skill.Level > 0 ? 85 + 48 * (Skill.Level - 1) : 85;
        private double ATKCoefficient => Skill.Level > 0 ? 0.12 + 0.06 * (Skill.Level - 1) : 0.12;
        private double Damage => BaseDamage + ATKCoefficient * (Skill.Character?.ATK ?? 0);

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> enemies = [];
            if (GamingQueue != null)
            {
                enemies.AddRange(GamingQueue.GetEnemies(caster).Where(c => c.HP > 0));
            }
            if (enemies.Count == 0)
            {
                enemies.AddRange(ctx.Targets.Where(c => c.HP > 0));
            }
            if (enemies.Count == 0) return;
            Character target = enemies[Random.Next(enemies.Count)];
            WriteLine($"[ {caster} ] 使用[快速狙击]，命中了随机的 [ {target} ]！");
            DamageToEnemy(caster, target, DamageType.Physical, MagicType.None, Damage);
        }
    }
}
