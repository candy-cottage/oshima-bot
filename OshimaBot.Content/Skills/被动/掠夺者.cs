using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Skills
{
    /// <summary>
    /// 掠夺者：围绕【回合奖励】系统打造的被动
    /// <para/>· 成功夺取他人回合奖励时，永久叠加攻击力（对应 <see cref="Effect.OnRoundRewardStolen"/> 的夺取者视角）
    /// <para/>· 自身回合奖励被他人夺走时，获得短暂攻击力与行动速度加成（原持有者视角）
    /// <para/>· 同一份 <see cref="RoundRewardContext"/> 会同时流经原持有者与夺取者的特效，故两种视角都可在本被动中处理
    /// </summary>
    public class 掠夺者 : Skill
    {
        public override long Id => (long)PassiveID.掠夺者;
        public override string Name => "掠夺者";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";
        public override string DispelDescription => Effects.Count > 0 ? Effects.First().DispelDescription : "";

        public 掠夺者(Character? character = null) : base(SkillType.Passive, character)
        {
            Effects.Add(new 掠夺者特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 掠夺者特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"每当自身成功夺取他人的回合奖励时，永久获得 {单层攻击加成 * 100:0.##}% 攻击力（最多 {最大层数} 层，当前 {掠夺层数} 层）；" +
            $"当自身的回合奖励被他人夺走时，获得 {复仇攻击加成 * 100:0.##}% 攻击力与 {复仇速度加成:0.##} 点行动速度，持续 {复仇持续回合} 回合。";
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        private const double 单层攻击加成 = 0.06;
        private const int 最大层数 = 5;
        private const double 复仇攻击加成 = 0.1;
        private const double 复仇速度加成 = 45;
        private const int 复仇持续回合 = 2;

        public int 掠夺层数 { get; private set; } = 0;

        public override void OnRoundRewardStolen(RoundRewardContext ctx)
        {
            if (Skill.Character is not Character self || ctx.Skills.Count == 0) return;

            // 夺取者视角：自己成功夺取了别人的奖励
            if (ctx.Thief == self && ctx.From is not null && ctx.From != self)
            {
                if (掠夺层数 >= 最大层数)
                {
                    WriteLine($"[ {self} ] 的掠夺者层数已达上限（{最大层数} 层）。");
                    return;
                }
                掠夺层数++;
                self.ExATKPercentage += 单层攻击加成;
                GamingQueue?.AddApplyEffects(self, EffectType.DamageBoost);
                WriteLine($"[ {self} ] 发动掠夺者，永久获得 {单层攻击加成 * 100:0.##}% 攻击力（{掠夺层数}/{最大层数} 层）！");
            }
            // 原持有者视角：自己的奖励被别人抢走了
            else if (ctx.From == self && ctx.Thief is not null && ctx.Thief != self)
            {
                ExATK2 攻击加成 = new(Skill, new() { { "exatk", 复仇攻击加成 } }, self)
                {
                    Name = Name,
                    Durative = false,
                    DurationTurn = 复仇持续回合,
                    EffectType = EffectType.DamageBoost
                };
                攻击加成.AddToCharacter(self);

                ExSPD 速度加成 = new(Skill, new() { { "exspd", 复仇速度加成 } }, self)
                {
                    Name = Name,
                    Durative = false,
                    DurationTurn = 复仇持续回合
                };
                速度加成.AddToCharacter(self);

                GamingQueue?.AddApplyEffects(self, EffectType.DamageBoost);
                WriteLine($"[ {self} ] 的回合奖励被 [ {ctx.Thief} ] 夺走，获得复仇：攻击力 +{复仇攻击加成 * 100:0.##}%、行动速度 +{复仇速度加成:0.##}，持续 {复仇持续回合} 回合！");
            }
        }
    }
}
