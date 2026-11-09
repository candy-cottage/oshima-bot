using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 牺牲之箭 : Skill
    {
        public override long Id => (long)SkillID.牺牲之箭;
        public override string Name => "牺牲之箭";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override double EPCost => 40;
        public override double CD => 45;
        public override double HardnessTime { get; set; } = 8;
        public override bool CanSelectSelf => false;
        public override bool CanSelectTeammate => true;
        public override bool CanSelectEnemy => false;
        public override int CanSelectTargetCount => 3;

        public 牺牲之箭(Character? character = null) : base(SkillType.Skill, character)
        {
            CastRange = 6;
            Effects.Add(new 牺牲之箭特效(this));
        }
    }

    public class 牺牲之箭特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"为除自身外至多 {Skill.CanSelectTargetCount} 个友方角色回复 {能量回复:0.##} 点能量值。";

        private double 能量回复 => Skill.Level > 0 ? 40 + 20 * (Skill.Level - 1) : 40;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            foreach (Character target in ctx.Targets)
            {
                if (target == caster || target.HP <= 0) continue;
                double ep = 能量回复;
                target.EP = Math.Min(target.EP + ep, 200);
                WriteLine($"[ {caster} ] 向 [ {target} ] 射出了牺牲之箭，回复了 {ep:0.##} 点能量值！");
            }
        }
    }
}
