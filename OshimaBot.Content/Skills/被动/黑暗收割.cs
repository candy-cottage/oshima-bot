using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 黑暗收割 : Skill
    {
        public override long Id => (long)PassiveID.黑暗收割;
        public override string Name => "黑暗收割";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public int 当前灵魂数量 { get; set; } = 0;

        public 黑暗收割(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 黑暗收割特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }

        public override void OnCharacterRespawn(Skill newSkill)
        {
            if (newSkill is 黑暗收割 s)
            {
                s.当前灵魂数量 = 当前灵魂数量;
            }
        }
    }

    public class 黑暗收割特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"{Skill.SkillOwner()}对一半以下生命值的目标造成伤害时，将收集其灵魂以永久提升自身伤害，每个灵魂提供 {额外伤害提升 * 100:0.##}% 伤害加成。最多收集 {最多灵魂数量} 个灵魂。" +
            $"若{Skill.SkillOwner()}死亡，灵魂将损失一半。（当前灵魂数量：{CSkill?.当前灵魂数量} 个；伤害提升：{CSkill?.当前灵魂数量 * 额外伤害提升 * 100:0.##}%）";

        public 黑暗收割? CSkill => Skill is 黑暗收割 s ? s : null;
        private static double 额外伤害提升 => 0.02;
        private int 最多灵魂数量 => Skill.Character != null ? 10 + Skill.Character.Level / 10 * 5 : 10;
        private bool 触发标记 { get; set; } = false;

        public override double AlterExpectedDamageBeforeCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return 0;
            double damage = ctx.Damage;
            bool isNormalAttack = ctx.IsNormalAttack;
            DamageType damageType = ctx.DamageType;
            MagicType magicType = ctx.MagicType;
            Dictionary<Effect, double> totalDamageBonus = ctx.TotalDamageBonus;
            if (Skill is 黑暗收割 skill && character == Skill.Character && (enemy.HP / enemy.MaxHP) < 0.5)
            {
                触发标记 = true;
                return damage * skill.当前灵魂数量 * 额外伤害提升;
            }
            return 0;
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
            if (触发标记 && character == Skill.Character && (damageResult == DamageResult.Normal || damageResult == DamageResult.Critical) && Skill is 黑暗收割 skill && skill.当前灵魂数量 < 最多灵魂数量)
            {
                触发标记 = false;
                skill.当前灵魂数量++;
                WriteLine($"[ {character} ] 通过黑暗收割收集了一个灵魂！当前灵魂数：{skill.当前灵魂数量}");
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
            if (death == Skill.Character && Skill is 黑暗收割 skill && skill.当前灵魂数量 > 0)
            {
                int lost = skill.当前灵魂数量 / 2;
                if (lost > 0)
                {
                    skill.当前灵魂数量 -= lost;
                    WriteLine($"[ {death} ] 因死亡损失了 [ {lost} ] 个灵魂！");
                }
            }
        }

        public override void OnTurnEnd(TurnContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            触发标记 = false;
        }
    }
}
