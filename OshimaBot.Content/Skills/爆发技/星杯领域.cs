using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.PrefabricatedEntity;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Skills
{
    public class 星杯领域 : SoulboundSkill
    {
        public override long Id => (long)SuperSkillID.星杯领域;
        public override string Name => "星杯领域";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double CD => 100;
        public override double HardnessTime { get; set; } = 0;
        public override bool CanSelectEnemy => false;
        public override bool CanSelectTeammate => true;
        public override bool CanSelectSelf => true;
        public override bool SelectAllTeammates => true;
        public override bool AllowSelectDead => false;

        public 星杯领域(Character? character = null) : base(character)
        {
            Effects.Add(new 星杯领域特效(this));
        }
    }

    public class 星杯领域特效(SoulboundSkill skill) : SoulboundEffect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"对{Skill.TargetDescription()}施加完全免疫 {持续回合:0.##} 回合：期间无法被选中为普通攻击和技能的目标（自释放技能除外），并免疫物理伤害和魔法伤害。" +
            $"星杯领域起步消耗 100 能量，每额外消耗 100 能量延长 1 回合。";

        public int 持续回合 => 1;

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            double cost = ctx.EPCost > 0 ? ctx.EPCost : caster.EP;
            int extraTurns = Math.Max(0, (int)((cost - 100) / 100));
            int turns = 1 + extraTurns;
            foreach (Character target in targets)
            {
                if (target.HP <= 0) continue;
                完全免疫 e = new(Skill, caster, false, 0, turns);
                WriteLine($"[ {caster} ] 发动了星杯领域！[ {target} ] 获得了完全免疫，持续 {turns} 回合！");
                e.AddToCharacter(target);
                RecordCharacterApplyEffects(target, EffectType.DefenseBoost);
            }
        }
    }
}
