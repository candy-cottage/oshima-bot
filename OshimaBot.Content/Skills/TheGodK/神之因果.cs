using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.EffectResult;
using FunGame.Core.Model.Framework;

namespace OshimaBot.Content.Skills
{
    public class 神之因果 : Skill
    {
        public override long Id => (long)SuperSkillID.神之因果;
        public override string Name => "神之因果";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";
        public override double EPCost => 100;
        public override bool CostAllEP => true;
        public override double CD => 90;
        public override double HardnessTime { get; set; } = 1;
        public override bool CanSelectSelf => true;
        public override bool CanSelectEnemy => false;

        public 神之因果(Character? character = null) : base(SkillType.SuperSkill, character)
        {
            Effects.Add(new 神之因果特效(this));
        }
    }

    public class 神之因果特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"{Skill.SkillOwner()}短暂显现其「神」之本质，开启持续 4 回合的「神之领域」：在持续时间内，{Skill.SkillOwner()}对任意敌方目标造成的伤害，都将记录为「因果伤害值」。" +
            $"在持续时间结束后，所有敌方角色都会受到 [ 基于总因果伤害值的 {系数 * 100:0.##}% 除以当前在场敌方角色数量 ] 的真实伤害。在持续时间内，{Skill.SkillOwner()}可以对任何负面效果进行豁免。" +
            (因果伤害值 > 0 ? $"（当前累计因果：{因果伤害值:0.##} 点）" : "");
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;
        public override bool Durative => false;
        public override int DurationTurn => 4;

        public double 因果伤害值 { get; set; } = 0;
        public double 系数 => 1 + 0.2 * (Skill.Level - 1);

        public override OnExemptionCheckResult OnExemptionCheck(ImmuneContext ctx)
        {
            if (ctx.Trigger is not Character character) return default;
            if (character == Skill.Character)
            {
                return new OnExemptionCheckResult { ThrowingBonusDelta = 300 };
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
            if (character == Skill.Character && (damageResult == DamageResult.Normal || damageResult == DamageResult.Critical))
            {
                因果伤害值 += actualDamage;
            }
        }

        public override void AfterDeathCalculation(DeathContext ctx)
        {
            if (ctx.Trigger is not Character death) return;
            bool hasMaster = ctx.HasMaster;
            Character? killer = ctx.Killer;
            Dictionary<Character, int> continuousKilling = ctx.ContinuousKilling;
            Dictionary<Character, int> earnedMoney = ctx.EarnedMoney;
            Character[] assists = ctx.Assists;
            if (death == Skill.Character)
            {
                因果伤害值 = 0;
            }
        }

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            因果伤害值 = 0;
        }

        public override void OnEffectLost(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (GamingQueue != null && 因果伤害值 > 0)
            {
                WriteLine($"[ {character} ] 发动了神之因果！万象因果，命运既定！！！");
                List<Character> enemies = [.. GamingQueue.GetEnemies(character).Where(GamingQueue.Queue.Contains)];
                double damage = 因果伤害值 * 系数 / enemies.Count;
                foreach (Character enemy in enemies)
                {
                    DamageToEnemy(character, enemy, DamageType.True, MagicType, damage, new(character)
                    {
                        CalculateShield = false,
                        IgnoreImmune = true,
                        TriggerEffects = false
                    });
                }
                因果伤害值 = 0;
            }
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            List<Grid> grids = ctx.Grids;
            Dictionary<string, object> others = ctx.Others;
            RemainDurationTurn = DurationTurn;
            if (!caster.Effects.Contains(this))
            {
                caster.Effects.Add(this);
                OnEffectGained(new HookContext(GamingQueue, caster));
            }
            GamingQueue?.AddApplyEffects(caster, EffectType.Focusing);
        }
    }
}
