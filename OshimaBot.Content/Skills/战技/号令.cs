using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    public class 号令 : Skill
    {
        public override long Id => (long)SkillID.号令;
        public override string Name => "号令";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 50;
        public override double CD => 45;
        public override double HardnessTime { get; set; } = 8;
        public override bool CanSelectSelf => false;
        public override bool CanSelectTeammate => true;
        public override bool CanSelectEnemy => false;
        public override int CanSelectTargetCount => 3;

        public 号令(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 号令特效(this));
        }
    }

    public class 号令特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"提升除自身外至多 {Skill.CanSelectTargetCount} 个友方角色 {ATK * 100:0.##}% 攻击力，持续 {持续回合} 回合。";
        public override string DispelDescription => "被驱散性：可弱驱散";
        public override EffectType EffectType => EffectType.DamageBoost;
        public override DispelledType DispelledType => DispelledType.Weak;

        private double ATK => Level > 0 ? 0.12 + 0.05 * (Level - 1) : 0.12;
        private int 持续回合 => 2;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            foreach (Character target in ctx.Targets)
            {
                if (target == caster || target.HP <= 0) continue;
                WriteLine($"[ {caster} ] 发号施令，[ {target} ] 的攻击力提升了 {ATK * 100:0.##}% [ {target.BaseATK * ATK:0.##} ] 点！");
                ExATK2 e = new(Skill, new() { { "exatk", ATK } }, caster)
                {
                    Name = Name,
                    Durative = false,
                    Duration = 0,
                    DurationTurn = 持续回合,
                    EffectType = EffectType.DamageBoost,
                    DispelledType = DispelledType.Weak
                };
                target.Effects.Add(e);
                e.OnEffectGained(new HookContext(GamingQueue, target));
                GamingQueue?.AddApplyEffects(target, EffectType.DamageBoost);
            }
        }
    }
}
