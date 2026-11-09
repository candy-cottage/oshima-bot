using FunGame.Core.Api;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;
using Milimoe.FunGameTesting.Others;

namespace OshimaBot.Content.Effects.PassiveEffects
{
    /// <summary>
    /// 强运：被动状态（由【十二宫星环】等来源施加，按回合计时）
    /// <para/>· 所属角色每个回合结束时，随机生成 2~3 份绑定自身的【回合奖励】
    /// <para/>· 奖励取自模组公共的 <see cref="RoundRewardPool"/>（与队列内置奖励同构）：
    /// 用 <see cref="Factory.OpenFactory"/> 构造奖励技能（Skill 由 OpenSkill 承载、Effect 按 EffectID 构造），
    /// 再经 <see cref="FunGame.Core.Interface.Base.IGamingQueue.AddRoundReward"/> 绑定到角色名下
    /// <para/>· 仅在启用「角色绑定的回合奖励」时才能真正生成（否则 <c>AddRoundReward</c> 返回 false）
    /// </summary>
    public class 强运 : Effect
    {
        public override long Id => (long)PassiveEffectID.强运;
        public override string Name => "强运";
        public override string Description =>
            $"此角色处于强运状态：每个回合结束时，随机生成 {最小生成数量}~{最大生成数量} 份绑定自身的回合奖励，并顺延到其下一个行动回合。持续 {DurationTurn} 回合。来自：[ {Source} ] 的 [ {Skill.Name} ]";
        public override bool IsDebuff => false;
        public override Character? Source => _sourceCharacter;
        public override bool Durative => _durative;
        public override double Duration => _duration;
        public override int DurationTurn => _durationTurn;
        public override DispelledType DispelledType => DispelledType.CannotBeDispelled;

        /// <summary>每次触发最少生成的奖励数量</summary>
        public const int 最小生成数量 = 2;

        /// <summary>每次触发最多生成的奖励数量</summary>
        public const int 最大生成数量 = 3;

        private readonly Character _targetCharacter;
        private readonly Character _sourceCharacter;
        private readonly bool _durative;
        private readonly double _duration;
        private readonly int _durationTurn;

        public 强运(Skill skill, Character targetCharacter, Character sourceCharacter, bool durative = false, double duration = 0, int durationTurn = 1) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _targetCharacter = targetCharacter;
            _sourceCharacter = sourceCharacter;
            _durative = durative;
            _duration = duration;
            _durationTurn = durationTurn;
        }

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (_durative && RemainDuration == 0)
            {
                RemainDuration = Duration;
            }
            else if (RemainDurationTurn == 0)
            {
                RemainDurationTurn = DurationTurn;
            }
        }

        public override void OnTurnEnd(TurnContext ctx)
        {
            if (ctx.Trigger is not Character character || character != _targetCharacter || character.HP <= 0) return;
            if (GamingQueue is null) return;

            Dictionary<EffectID, Dictionary<string, object>> 奖励池 = RoundRewardPool.Create(Random);
            EffectID[] ids = [.. 奖励池.Keys];
            int count = Random.Next(最小生成数量, 最大生成数量 + 1);
            List<string> generatedNames = [];
            for (int i = 0; i < count; i++)
            {
                EffectID id = ids[Random.Next(ids.Length)];
                Skill reward = 构造奖励(id, 奖励池[id]);
                if (!GamingQueue.AddRoundReward(character, 1, reward))
                {
                    break;
                }
                generatedNames.Add(reward.Name);
            }

            if (generatedNames.Count > 0)
            {
                WriteLine($"[ {character} ] 受到强运眷顾，为下一行动回合生成了 {generatedNames.Count} 份回合奖励：{string.Join("、", generatedNames)}！");
            }
            else
            {
                WriteLine($"[ {character} ] 的强运未能生成回合奖励（未启用角色绑定的回合奖励）。");
            }
        }

        /// <summary>
        /// 从公共奖励池构造一份奖励技能（与队列的 CreateRoundRewardSkill 同构）
        /// </summary>
        private static Skill 构造奖励(EffectID id, Dictionary<string, object> effectArgs)
        {
            Dictionary<string, object> skillArgs = [];
            if (RoundRewardPool.IsActive(id))
            {
                skillArgs.Add("active", true);
                skillArgs.Add("self", true);
                skillArgs.Add("enemy", false);
            }

            Skill skill = Factory.OpenFactory.GetInstance<Skill>((long)id, "", skillArgs);
            Effect effect = Factory.OpenFactory.GetInstance((long)id, "", skill, new(effectArgs));
            skill.Effects.Add(effect);
            skill.Name = $"[R] {effect.Name}";
            return skill;
        }
    }
}
