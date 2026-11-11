using OshimaBot.Content.Effects.OpenEffects;

namespace OshimaBot.Content.Others
{
    /// <summary>
    /// 回合奖励特效池（模组公共区域）
    /// <para/>· 由 WebAPI 的 <c>FunGameService.GetRoundRewards</c> 迁入模组层：模组自身（技能 / 被动 / 状态特效）现在可直接引用本池
    /// <para/>· <paramref name="random"/> 必须由调用方下传（通常是本局队列的随机源），否则奖励数值每局不同，整局模拟无法复现
    /// </summary>
    public static class RoundRewardPool
    {
        /// <summary>
        /// 构造回合奖励特效池：Id → 构造参数<para/>
        /// 注意：每次调用都会重新掷点，请只调用一次并把返回值存下来（Keys 与取值必须来自同一份实例）
        /// </summary>
        /// <param name="random">本局随机源</param>
        public static Dictionary<EffectID, Dictionary<string, object>> Create(Random random) => new()
        {
            {
                EffectID.ExATK,
                new()
                {
                    { "exatk", random.Next(40, 80) }
                }
            },
            {
                EffectID.ExCritRate,
                new()
                {
                    { "excr", Math.Clamp(random.NextDouble(), 0.25, 0.5) }
                }
            },
            {
                EffectID.ExCritDMG,
                new()
                {
                    { "excrd", Math.Clamp(random.NextDouble(), 0.5, 1) }
                }
            },
            {
                EffectID.ExATK2,
                new()
                {
                    { "exatk", Math.Clamp(random.NextDouble(), 0.15, 0.3) }
                }
            },
            {
                EffectID.ExMaxMP2,
                new()
                {
                    { "exmp", 5 }
                }
            },
            {
                EffectID.AccelerationCoefficient,
                new()
                {
                    { "exacc", 1 }
                }
            },
            {
                EffectID.IgnoreEvade,
                new()
                {
                    { "p", 1 }
                }
            },
            {
                EffectID.RecoverHP,
                new()
                {
                    { "hp", random.Next(160, 640) }
                }
            },
            {
                EffectID.RecoverMP,
                new()
                {
                    { "mp", random.Next(140, 490) }
                }
            },
            {
                EffectID.RecoverHP2,
                new()
                {
                    { "hp", Math.Clamp(random.NextDouble(), 0.04, 0.08) }
                }
            },
            {
                EffectID.RecoverMP2,
                new()
                {
                    { "mp", Math.Clamp(random.NextDouble(), 0.09, 0.18) }
                }
            },
            {
                EffectID.GetEP,
                new()
                {
                    { "ep", random.Next(20, 40) }
                }
            }
        };

        /// <summary>
        /// 该特效是否属于「主动奖励」：主动奖励在发放时立即释放，被动奖励则挂载为状态特效
        /// </summary>
        /// <param name="id">特效数字标识符</param>
        public static bool IsActive(EffectID id) => (long)id > (long)EffectID.Active_Start;

        /// <summary>
        /// 把特效池的键集合转换为 <see cref="FunGame.Core.Interface.Base.IGamingQueue.InitRoundRewards"/> 所需的 effects 映射
        /// </summary>
        /// <param name="effectIds">特效数字标识符集合</param>
        public static Dictionary<long, bool> BuildEffectMap(IEnumerable<EffectID> effectIds)
        {
            Dictionary<long, bool> effects = [];
            foreach (EffectID id in effectIds)
            {
                effects[(long)id] = IsActive(id);
            }
            return effects;
        }
    }
}
