using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    public class 残香凋零 : Skill
    {
        public override long Id => (long)SuperSkillID.残香凋零;
        public override string Name => "残香凋零";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override double EPCost => 100;
        public override double CD => 100 - 4 * (Level - 1);
        public override double HardnessTime { get; set; } = 12;
        public override bool CanSelectSelf => false;
        public override bool CanSelectEnemy => true;

        public 残香凋零(Character? character = null) : base(SkillType.SuperSkill, character)
        {
            CastAnywhere = true;
            Effects.Add(new 残香凋零特效(this));
        }

        public override void OnTurnStart(Character character, List<Character> enemys, List<Character> teammates, List<Skill> skills, List<Item> items)
        {
            if (Character == character)
            {
                CanSelectTargetCount = enemys.Count;
                if (CanSelectTargetCount > 6) CanSelectTargetCount = 6;
            }
        }
    }

    public class 残香凋零特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"{Skill.SkillOwner()}释放能够动荡能量流的深渊之死气。对至多 6 个角色造成 " +
            $"{能量系数 * 100:0.##}% 其现有能量值 + {智力系数 * 100:0.##}% 智力 [ {智力伤害:0.##} ] 的魔法伤害。";

        private double 智力系数 => 0.2 * Level;
        private double 智力伤害 => 智力系数 * Skill.Character?.INT ?? 0;
        private double 能量系数 => 1 * Level;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            Dictionary<string, object> others = ctx.Others;
            foreach (Character c in targets)
            {
                double ep = c.EP;
                DamageToEnemy(caster, c, DamageType.Magical, MagicType, ep * 能量系数 + 智力伤害);
            }
        }
    }
}
