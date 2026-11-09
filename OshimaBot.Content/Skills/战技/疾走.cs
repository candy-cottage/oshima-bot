using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    public class 疾走 : Skill
    {
        public override long Id => (long)SkillID.疾走;
        public override string Name => "疾走";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 25;
        // 调整：CD 15 → 18（战技区间 18–45）
        public override double CD => 18;
        public override double HardnessTime { get; set; } = 3;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;

        public 疾走(Character? character = null) : base(SkillType.Skill, character)
        {
            Effects.Add(new 疾走特效(this));
        }
    }

    public class 疾走特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"本回合内大幅提升移动距离，并附赠一次战技的决策点配额。移动距离提升：{移动距离提升} 格。";
        public override string DispelDescription => "";
        public override EffectType EffectType => EffectType.Haste;
        public override bool Durative => false;
        public override double Duration => 0;
        public override int DurationTurn => 1;

        private int 移动距离提升
        {
            get
            {
                return Skill.Level switch
                {
                    2 or 3 => 2,
                    4 or 5 => 3,
                    6 => 4,
                    _ => 1,
                };
            }
        }
        private int 本次提升 = 0;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            Skill.IsInEffect = true;
            本次提升 = 移动距离提升;
            character.ExMOV += 本次提升;
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            Skill.IsInEffect = false;
            character.ExMOV -= 本次提升;
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            Dictionary<string, object> others = ctx.Others;
            本次提升 = 0;
            if (!caster.Effects.Contains(this))
            {
                GamingQueue?.AddApplyEffects(caster, EffectType);
                RemainDurationTurn = DurationTurn;
                caster.Effects.Add(this);
                OnEffectGained(new HookContext(GamingQueue, caster));
            }
            if (GamingQueue != null && GamingQueue.CharacterDecisionPoints.TryGetValue(caster, out DecisionPoints? dp) && dp != null)
            {
                dp.AddTempActionQuota(this, CharacterActionType.CastSkill);
            }
        }
    }
}
