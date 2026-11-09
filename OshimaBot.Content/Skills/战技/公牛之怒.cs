using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 公牛之怒 : Skill
    {
        public override long Id => (long)SkillID.公牛之怒;
        public override string Name => "公牛之怒";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override double EPCost => 30;
        public override double CD => 45;
        public override double HardnessTime { get; set; } = 4;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => false;

        public 公牛之怒(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 公牛之怒特效(this));
        }
    }

    public class 公牛之怒特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"扣除自身 {生命扣除比例 * 100:0.##}% 最大生命值 [ {Skill.Character?.MaxHP * 生命扣除比例:0.##} ]，并回复 {能量回复:0.##} 点能量值。";

        private double 生命扣除比例 => 0.12;
        private double 能量回复 => Skill.Level > 0 ? 50 + 15 * (Skill.Level - 1) : 50;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            double cost = caster.MaxHP * 生命扣除比例;
            double actual = Math.Min(cost, Math.Max(0, caster.HP - 1));
            if (actual > 0)
            {
                caster.HP -= actual;
                WriteLine($"[ {caster} ] 燃烧了 {actual:0.##} 点生命值，发动了公牛之怒！");
            }
            double ep = 能量回复;
            caster.EP = Math.Min(caster.EP + ep, 200);
            WriteLine($"[ {caster} ] 回复了 {ep:0.##} 点能量值！");
        }
    }
}
