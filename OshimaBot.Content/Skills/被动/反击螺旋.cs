using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 反击螺旋 : Skill
    {
        public override long Id => (long)PassiveID.反击螺旋;
        public override string Name => "反击螺旋";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 反击螺旋(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 反击螺旋特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 反击螺旋特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"受到伤害时，有 {触发概率 * 100:0.##}% 概率发动反击螺旋，对攻击者造成 {反击伤害:0.##} 点真实伤害，并对 {额外目标数} 个随机敌人同样造成 {反击伤害:0.##} 点真实伤害。";

        private double 触发概率 => Skill.Character != null ? 0.3 + Skill.Character.Level * 0.002 : 0.3;
        private double 反击伤害 => Skill.Character != null ? 50 + Skill.Character.Level * 5 + Skill.Character.ATK * 0.3 : 50;
        private int 额外目标数 => 2;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character attacker || ctx.Enemy is not Character character) return;
            if (Skill.Character == null || Skill.Character != character) return;
            if (attacker == character) return;
            if (ctx.ActualDamage <= 0) return;
            if (Random.NextDouble() > 触发概率) return;
            WriteLine($"[ {character} ] 发动了反击螺旋！");
            DamageToEnemy(character, attacker, DamageType.True, MagicType.None, 反击伤害, new(character)
            {
                TriggerEffects = false
            });
            foreach (Character target in GamingQueue?.AllCharacters
                         .Where(c => c != character && c != attacker && c.HP > 0)
                         .OrderBy(_ => Random.Next())
                         .Take(额外目标数) ?? [])
            {
                DamageToEnemy(character, target, DamageType.True, MagicType.None, 反击伤害, new(character)
                {
                    TriggerEffects = false
                });
            }
        }
    }
}
