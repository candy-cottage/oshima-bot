using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;

namespace OshimaBot.Content.Skills
{
    public class 先攻 : Skill
    {
        public override long Id => (long)PassiveID.先攻;
        public override string Name => "先攻";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 先攻(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 先攻特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 先攻特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description => $"对长时间未交手的敌人抢先出手时（双方超过 {重置窗口:0.##} {GameplayEquilibriumConstant.InGameTime}互不攻击视为重置先手），首次造成伤害获得 {先攻伤害:0.##} 点额外真实伤害和 {能量获取:0.##} 点能量。";

        private readonly Dictionary<Character, double> 我方攻击时间 = [];
        private readonly Dictionary<Character, double> 敌方攻击我方时间 = [];

        private double 重置窗口 => Skill.Character != null ? 8 + Skill.Character.Level * 0.1 : 8;
        private double 先攻伤害 => Skill.Character != null ? 45 + Skill.Character.Level * 4.5 : 45;
        private double 能量获取 => Skill.Character != null ? 6 + Skill.Character.Level * 0.2 : 6;

        public override void AfterDamageCalculation(DamageContext ctx)
        {
            if (ctx.Trigger is not Character character || ctx.Enemy is not Character enemy) return;
            if (Skill.Character == null || character == enemy || GamingQueue == null) return;
            double now = GamingQueue.TotalTime;
            if (Skill.Character == character)
            {
                // 出伤侧：先手判定
                if (ctx.DamageResult != DamageResult.Normal && ctx.DamageResult != DamageResult.Critical) return;
                if (IsFirstStrike(enemy, now))
                {
                    WriteLine($"[ {character} ] 发动了先攻！抢先出手造成 {先攻伤害:0.##} 点额外真实伤害并回复能量！");
                    DamageToEnemy(character, enemy, DamageType.True, MagicType.None, 先攻伤害, new(character)
                    {
                        TriggerEffects = false
                    });
                    if (character.EP < 200)
                    {
                        character.EP = Math.Min(character.EP + 能量获取, 200);
                    }
                }
                我方攻击时间[enemy] = now;
            }
            else if (Skill.Character == enemy)
            {
                // 受击侧：记录敌方攻击我方的时间
                敌方攻击我方时间[character] = now;
            }
        }

        private bool IsFirstStrike(Character enemy, double now)
        {
            敌方攻击我方时间.TryGetValue(enemy, out double 敌攻我);
            我方攻击时间.TryGetValue(enemy, out double 我攻敌);
            return now - Math.Max(敌攻我, 我攻敌) >= 重置窗口;
        }
    }
}
