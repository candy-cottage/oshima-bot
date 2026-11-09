using FunGame.Core.Api;
using FunGame.Core.Entity;
using OshimaBot.Content.Effects;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Effects.SkillEffects
{
    public class 施加概率增益 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description
        {
            get
            {
                SetDescription();
                return $"{概率文本}对{Skill.TargetDescription()}施加{GetEffectTypeName(_effectType)} {持续时间}。{(_description != "" ? _description : "")}";
            }
        }
        public override EffectType EffectType => _effectType;
        public override DispelledType DispelledType => _dispelledType;
        public override bool ExemptDuration => true;

        private string 概率文本 => ActualProbability == 1 ? "" : $"{ActualProbability * 100:0.##}% 概率";
        /// <summary>
        /// 实际命中概率 —— 统一走 <see cref="EfficacyHit.命中率"/>（2026-09-23 统一口径）。
        /// <para/>✅ **为什么本效果类被战技 / 爆发技混用也安全**（Admin 指出，已实测核对）：
        /// 全库 **55 处**重写 `MagicBottleneck` 的技能**清一色是魔法**（`SkillType.Magic`），
        /// 战技 / 爆发技 / 物品 / 被动**都不重写** ⇒ 它们的 `MagicBottleneck` 取基类默认 **0**
        /// ⇒ `MagicEfficacy` 在 `瓶颈 == 0` 时**直接返回 1.0**（不是浮点近似，是精确值）
        /// ⇒ 统一公式 `(基础 + 成长×(Lv−1)) × 1.0` 与旧公式 `基础 + 成长×(Lv−1)×1.0` **完全等价**。
        /// <para/>同理，那批「基础概率 ≥ 1 且无成长」的**必中型控制**（石化之矢 / 无相飞刀 / 陀螺舞 /
        /// 鲨鱼锚击 / 裁决塔罗 …）全是战技或爆发技 ⇒ `clamp(1.0 × 1.0) = 1.0` ⇒ **仍然必中**。
        /// <para/>所以**无需按技能类型或必中性分流**，一个分支都不用 —— 这是机制保证的结果，不是巧合。
        /// </summary>
        private double ActualProbability => EfficacyHit.命中率(_probability, _probabilityLevelGrowth, Level, MagicEfficacy);
        private string 持续时间 => _durative && _duration > 0 ? $"{实际持续时间:0.##}" + $" {GameplayEquilibriumConstant.InGameTime}" : (!_durative && _durationTurn > 0 ? 实际持续时间 + " 回合" : $"0 {GameplayEquilibriumConstant.InGameTime}");
        private double 实际持续时间 => _durative && _duration > 0 ? (_duration + _levelGrowth * (Level - 1) * MagicEfficacy) : (!_durative && _durationTurn > 0 ? ((int)Math.Round(_durationTurn + _levelGrowth * (Level - 1) * MagicEfficacy, 0, MidpointRounding.ToPositiveInfinity)) : 0);
        private readonly EffectType _effectType;
        private readonly bool _durative;
        private readonly double _duration;
        private readonly int _durationTurn;
        private readonly double _levelGrowth;
        private readonly double _probability;
        private readonly double _probabilityLevelGrowth;
        private readonly object[] _args;
        private DispelledType _dispelledType = DispelledType.Weak;
        private string _description = "";

        public 施加概率增益(Skill skill, EffectType effectType, bool durative = false, double duration = 0, int durationTurn = 1, double levelGrowth = 0, double probability = 0, double probabilityLevelGrowth = 0, params object[] args) : base(skill)
        {
            GamingQueue = skill.GamingQueue;
            _effectType = effectType;
            _durative = durative;
            _duration = duration;
            _durationTurn = durationTurn;
            _levelGrowth = levelGrowth;
            _probability = probability;
            _probabilityLevelGrowth = probabilityLevelGrowth;
            _args = args;
            SetDescription();
        }

        public override void OnSkillCasted(SkillCastContext ctx)
        {
            if (ctx.Trigger is not Character caster) return;
            List<Character> targets = ctx.Targets;
            foreach (Character target in targets)
            {
                if (target.HP <= 0 || Random.NextDouble() > ActualProbability) continue;
                Effect? e = null;
                double duration = _duration + _levelGrowth * (Level - 1);
                int durationTurn = Convert.ToInt32(_durationTurn + _levelGrowth * (Level - 1));
                string tip = "";
                switch (_effectType)
                {
                    case EffectType.HealOverTime:
                        bool isPercentage = false;
                        double durationHeal = 0;
                        double durationHealPercent = 0;
                        double durationHealLevelGrowth = 0;
                        if (_args.Length > 0 && _args[0] is bool _)
                        {
                            isPercentage = (bool)_args[0];
                        }
                        if (_args.Length > 1 && _args[1] is double _)
                        {
                            durationHeal = (double)_args[1];
                        }
                        if (_args.Length > 2 && _args[2] is double _)
                        {
                            durationHealPercent = (double)_args[2];
                        }
                        if (_args.Length > 3 && _args[3] is double _)
                        {
                            durationHealLevelGrowth = (double)_args[3];
                        }
                        if (isPercentage && durationHealPercent > 0 || !isPercentage && durationHeal > 0)
                        {
                            if (Level > 0)
                            {
                                durationHeal += durationHealLevelGrowth * (Level - 1);
                                durationHealPercent += durationHealLevelGrowth * (Level - 1);
                            }
                            string healString = $"每{GameplayEquilibriumConstant.InGameTime}回复 {(isPercentage ? $"{durationHealPercent * 100:0.##}% 当前生命值" : durationHeal.ToString("0.##"))} 点生命值";
                            tip = $"[ {caster} ] 对 [ {target} ] 施加了{GetEffectTypeName(_effectType)}！ [ {target} ] 每{GameplayEquilibriumConstant.InGameTime}{healString}！持续时间：{持续时间}！";
                            e = new 持续回复(Skill, target, caster, _durative, duration, durationTurn, isPercentage, durationHeal, durationHealPercent)
                            {
                                EffectType = _effectType
                            };
                        }
                        break;
                    default:
                        break;
                }
                if (e != null && !CheckExemption(caster, target, e))
                {
                    WriteLine(tip);
                    target.Effects.Add(e);
                    e.OnEffectGained(new HookContext(GamingQueue, target));
                    GamingQueue?.AddApplyEffects(target, e.EffectType);
                }
            }
        }

        private void SetDescription()
        {
            switch (_effectType)
            {
                case EffectType.HealOverTime:
                    _dispelledType = DispelledType.Weak;
                    bool isPercentage = false;
                    double durationHeal = 0;
                    double durationHealPercent = 0;
                    double durationHealLevelGrowth = 0;
                    if (_args.Length > 0 && _args[0] is bool _)
                    {
                        isPercentage = (bool)_args[0];
                    }
                    if (_args.Length > 1 && _args[1] is double _)
                    {
                        durationHeal = (double)_args[1];
                    }
                    if (_args.Length > 2 && _args[2] is double _)
                    {
                        durationHealPercent = (double)_args[2];
                    }
                    if (_args.Length > 3 && _args[3] is double _)
                    {
                        durationHealLevelGrowth = (double)_args[3];
                    }
                    if (isPercentage && durationHealPercent > 0 || !isPercentage && durationHeal > 0)
                    {
                        if (Level > 0) durationHeal += durationHealLevelGrowth * (Level - 1);
                        if (Level > 0) durationHealPercent += durationHealLevelGrowth * (Level - 1);
                        string healString = $"每{GameplayEquilibriumConstant.InGameTime}回复 {(isPercentage ? $"{durationHealPercent * 100:0.##}% 当前生命值" : durationHeal.ToString("0.##"))} 点生命值";
                        _description = $"{GetEffectTypeName(_effectType)}：每{GameplayEquilibriumConstant.InGameTime}{healString}！持续时间：{持续时间}！";
                    }
                    break;
                default:
                    break;
            }
        }

        private static string GetEffectTypeName(EffectType type)
        {
            return type switch
            {
                _ => SkillSet.GetEffectTypeName(type)
            };
        }
    }
}
