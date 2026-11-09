using FunGame.Core.Api;
using FunGame.Core.Entity;
using OshimaBot.Content.Effects;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using OshimaBot.Content.Effects.PassiveEffects;

namespace OshimaBot.Content.Effects.SkillEffects
{
    public class 施加概率负面 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description
        {
            get
            {
                SetDescription();
                return $"{概率文本}对{Skill.TargetDescription()}造成{GetEffectTypeName(_effectType)} {持续时间}。{(_description != "" ? _description : "")}";
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

        public 施加概率负面(Skill skill, EffectType effectType, bool durative = false, double duration = 0, int durationTurn = 1, double levelGrowth = 0, double probability = 0, double probabilityLevelGrowth = 0, params object[] args) : base(skill)
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
                    case EffectType.Silence:
                        tip = $"[ {caster} ] 对 [ {target} ] 造成了封技和施法解除！持续时间：{持续时间}！";
                        e = new 封技(Skill, caster, _durative, duration, durationTurn);
                        break;
                    case EffectType.Confusion:
                        tip = $"[ {target} ] 陷入了混乱！！持续时间：{持续时间}！";
                        e = new 混乱(Skill, caster, _durative, duration, durationTurn);
                        break;
                    case EffectType.Taunt:
                        tip = $"[ {target} ] 被 [ {caster} ] 嘲讽了！持续时间：{持续时间}！";
                        e = new 愤怒(Skill, caster, caster, _durative, duration, durationTurn);
                        break;
                    case EffectType.Delay:
                        double healingReductionPercent = 0.3;
                        if (_args.Length > 0 && _args[0] is double healingReduce)
                        {
                            healingReductionPercent = healingReduce;
                        }
                        tip = $"[ {caster} ] 对 [ {target} ] 造成了迟滞！普通攻击和技能的硬直时间、当前行动等待时间延长了 {healingReductionPercent * 100:0.##}%！持续时间：{持续时间}！";
                        e = new 迟滞(Skill, caster, _durative, duration, durationTurn, healingReductionPercent);
                        break;
                    case EffectType.Stun:
                        tip = $"[ {caster} ] 对 [ {target} ] 造成了眩晕！持续时间：{持续时间}！";
                        e = new 眩晕(Skill, caster, _durative, duration, durationTurn);
                        break;
                    case EffectType.Freeze:
                        tip = $"[ {caster} ] 对 [ {target} ] 造成了冻结！持续时间：{持续时间}！";
                        e = new 冻结(Skill, caster, _durative, duration, durationTurn);
                        break;
                    case EffectType.Petrify:
                        tip = $"[ {caster} ] 对 [ {target} ] 造成了石化！持续时间：{持续时间}！";
                        e = new 石化(Skill, caster, _durative, duration, durationTurn);
                        break;
                    case EffectType.Vulnerable:
                        DamageType damageType = DamageType.Magical;
                        if (_args.Length > 0 && _args[0] is DamageType dt)
                        {
                            damageType = dt;
                        }
                        double exDamagePercent = 0;
                        if (_args.Length > 1 && _args[1] is double percent)
                        {
                            exDamagePercent = percent;
                        }
                        if (exDamagePercent > 0)
                        {
                            tip = $"[ {caster} ] 对 [ {target} ] 造成了易伤，额外受到 {exDamagePercent * 100:0.##}% {CharacterSet.GetDamageTypeName(damageType)}！持续时间：{持续时间}！";
                            e = new 易伤(Skill, target, caster, _durative, duration, durationTurn, damageType, exDamagePercent);
                        }
                        break;
                    case EffectType.Bleed:
                        bool isPercentage = false;
                        double durationDamage = 0;
                        double durationDamagePercent = 0;
                        double durationDamageLevelGrowth = 0;
                        if (_args.Length > 0 && _args[0] is bool isPerc)
                        {
                            isPercentage = isPerc;
                        }
                        if (_args.Length > 1 && _args[1] is double durDamage)
                        {
                            durationDamage = durDamage;
                        }
                        if (_args.Length > 2 && _args[2] is double durDamagePercent)
                        {
                            durationDamagePercent = durDamagePercent;
                        }
                        if (_args.Length > 3 && _args[3] is double durDamageLevelGrowth)
                        {
                            durationDamageLevelGrowth = durDamageLevelGrowth;
                        }
                        if (isPercentage && durationDamagePercent > 0 || !isPercentage && durationDamage > 0)
                        {
                            if (Level > 0) durationDamage += durationDamageLevelGrowth * (Level - 1);
                            if (Level > 0) durationDamagePercent += durationDamageLevelGrowth * (Level - 1);
                            string damageString = isPercentage ? $"流失 {durationDamagePercent * 100:0.##}% 当前生命值" : $"流失 {durationDamage:0.##} 点生命值";
                            tip = $"[ {caster} ] 对 [ {target} ] 造成了气绝！ [ {target} ] 进入行动受限状态且每{GameplayEquilibriumConstant.InGameTime}{damageString}！持续时间：{持续时间}！";
                            e = new 气绝(Skill, target, caster, _durative, duration, durationTurn, isPercentage, durationDamage, durationDamagePercent);
                        }
                        break;
                    case EffectType.Cripple:
                        tip = $"[ {caster} ] 对 [ {target} ] 造成了战斗不能，禁止普通攻击和使用技能（魔法、战技和爆发技）！持续时间：{持续时间}！";
                        e = new 战斗不能(Skill, caster, _durative, duration, durationTurn);
                        break;
                    case EffectType.Disarm:
                        tip = $"[ {caster} ] 对 [ {target} ] 造成了缴械！持续时间：{持续时间}！";
                        e = new 缴械(Skill, caster, _durative, duration, durationTurn);
                        break;
                    case EffectType.Burn:
                    case EffectType.Poison:
                        isPercentage = false;
                        durationDamage = 0;
                        durationDamagePercent = 0;
                        durationDamageLevelGrowth = 0;
                        damageType = DamageType.Magical;
                        DamageCalculationOptions? options = null;
                        if (_args.Length > 0 && _args[0] is bool _)
                        {
                            isPercentage = (bool)_args[0];
                        }
                        if (_args.Length > 1 && _args[1] is double _)
                        {
                            durationDamage = (double)_args[1];
                        }
                        if (_args.Length > 2 && _args[2] is double _)
                        {
                            durationDamagePercent = (double)_args[2];
                        }
                        if (_args.Length > 3 && _args[3] is double _)
                        {
                            durationDamageLevelGrowth = (double)_args[3];
                        }
                        if (_args.Length > 0 && _args[4] is DamageType _)
                        {
                            damageType = (DamageType)_args[4];
                        }
                        if (_args.Length > 0 && _args[5] is DamageCalculationOptions _)
                        {
                            options = (DamageCalculationOptions)_args[5];
                        }
                        if (isPercentage && durationDamagePercent > 0 || !isPercentage && durationDamage > 0)
                        {
                            if (Level > 0) durationDamage += durationDamageLevelGrowth * (Level - 1);
                            if (Level > 0) durationDamagePercent += durationDamageLevelGrowth * (Level - 1);
                            string damageString = isPercentage ? $"受到 {durationDamagePercent * 100:0.##}% 当前生命值的" : $"受到 {durationDamage:0.##} 点" + CharacterSet.GetDamageTypeName(damageType);
                            tip = $"[ {caster} ] 对 [ {target} ] 造成了{GetEffectTypeName(_effectType)}！ [ {target} ] 每{GameplayEquilibriumConstant.InGameTime}{damageString}！持续时间：{持续时间}！";
                            e = new 持续伤害(Skill, target, caster, _durative, duration, durationTurn, isPercentage, durationDamage, durationDamagePercent, damageType, options)
                            {
                                EffectType = _effectType
                            };
                        }
                        break;
                    case EffectType.InterruptCasting:
                        e = new 打断施法(Skill);
                        break;
                    default:
                        break;
                }
                if (e != null && !CheckExemption(caster, target, e))
                {
                    if (e is 打断施法 ddsf)
                    {
                        ddsf.Activate(caster, [target]);
                        continue;
                    }
                    WriteLine(tip);
                    target.Effects.Add(e);
                    e.OnEffectGained(new HookContext(GamingQueue, target));
                    GamingQueue?.AddApplyEffects(target, e.EffectType);
                    if (e is 迟滞 cz)
                    {
                        cz.ApplyChange(target);
                    }
                }
            }
        }

        private void SetDescription()
        {
            switch (_effectType)
            {
                case EffectType.Silence:
                    _dispelledType = DispelledType.Weak;
                    _description = "封技：不能使用技能（魔法、战技和爆发技），并解除当前施法。";
                    break;
                case EffectType.Confusion:
                    _dispelledType = DispelledType.Strong;
                    _description = "混乱：进入行动受限状态，失控并随机行动，且所有指令均会在所有角色中随机选取目标。";
                    break;
                case EffectType.Taunt:
                    _dispelledType = DispelledType.Strong;
                    _description = "愤怒：进入行动受限状态，失控并随机行动，行动回合内仅能对嘲讽者发起普通攻击。";
                    break;
                case EffectType.Delay:
                    double healingReductionPercent = 0.3;
                    if (_args.Length > 0 && _args[0] is double healingReduce)
                    {
                        healingReductionPercent = healingReduce;
                    }
                    _dispelledType = DispelledType.Weak;
                    _description = $"迟滞：普通攻击和技能的硬直时间、当前行动等待时间延长 {healingReductionPercent * 100:0.##}%。";
                    break;
                case EffectType.Stun:
                    _dispelledType = DispelledType.Strong;
                    _description = "眩晕：进入完全行动不能状态。";
                    break;
                case EffectType.Freeze:
                    _dispelledType = DispelledType.Strong;
                    _description = "冻结：进入完全行动不能状态。";
                    break;
                case EffectType.Petrify:
                    _dispelledType = DispelledType.Strong;
                    _description = "石化：进入完全行动不能状态。";
                    break;
                case EffectType.Vulnerable:
                    DamageType damageType = DamageType.Magical;
                    if (_args.Length > 0 && _args[0] is DamageType dt)
                    {
                        damageType = dt;
                    }
                    double exDamagePercent = 0;
                    if (_args.Length > 1 && _args[1] is double percent)
                    {
                        exDamagePercent = percent;
                    }
                    if (exDamagePercent > 0)
                    {
                        _dispelledType = DispelledType.Weak;
                        _description = $"易伤：额外受到 {exDamagePercent * 100:0.##}% {CharacterSet.GetDamageTypeName(damageType)}。";
                    }
                    break;
                case EffectType.Bleed:
                    _dispelledType = DispelledType.Strong;
                    bool isPercentage = false;
                    double durationDamage = 0;
                    double durationDamagePercent = 0;
                    double durationDamageLevelGrowth = 0;
                    if (_args.Length > 0 && _args[0] is bool isPerc)
                    {
                        isPercentage = isPerc;
                    }
                    if (_args.Length > 1 && _args[1] is double durDamage)
                    {
                        durationDamage = durDamage;
                    }
                    if (_args.Length > 2 && _args[2] is double durDamagePercent)
                    {
                        durationDamagePercent = durDamagePercent;
                    }
                    if (_args.Length > 3 && _args[3] is double durDamageLevelGrowth)
                    {
                        durationDamageLevelGrowth = durDamageLevelGrowth;
                    }
                    if (isPercentage && durationDamagePercent > 0 || !isPercentage && durationDamage > 0)
                    {
                        if (Level > 0) durationDamage += durationDamageLevelGrowth * (Level - 1);
                        if (Level > 0) durationDamagePercent += durationDamageLevelGrowth * (Level - 1);
                        string damageString = isPercentage ? $"流失 {durationDamagePercent * 100:0.##}% 当前生命值" : $"流失 {durationDamage:0.##} 点生命值";
                        _description = $"气绝：进入行动受限状态并每{GameplayEquilibriumConstant.InGameTime}{damageString}，此效果不会导致角色死亡。";
                    }
                    break;
                case EffectType.Cripple:
                    _dispelledType = DispelledType.Strong;
                    _description = "战斗不能：无法普通攻击和使用技能（魔法、战技和爆发技）。";
                    break;
                case EffectType.Disarm:
                    _dispelledType = DispelledType.Weak;
                    _description = "缴械：无法普通攻击。";
                    break;
                case EffectType.Burn:
                case EffectType.Poison:
                    _dispelledType = DispelledType.Weak;
                    isPercentage = false;
                    durationDamage = 0;
                    durationDamagePercent = 0;
                    durationDamageLevelGrowth = 0;
                    damageType = DamageType.Magical;
                    if (_args.Length > 0 && _args[0] is bool _)
                    {
                        isPercentage = (bool)_args[0];
                    }
                    if (_args.Length > 1 && _args[1] is double _)
                    {
                        durationDamage = (double)_args[1];
                    }
                    if (_args.Length > 2 && _args[2] is double _)
                    {
                        durationDamagePercent = (double)_args[2];
                    }
                    if (_args.Length > 3 && _args[3] is double _)
                    {
                        durationDamageLevelGrowth = (double)_args[3];
                    }
                    if (_args.Length > 0 && _args[4] is DamageType _)
                    {
                        damageType = (DamageType)_args[4];
                    }
                    if (isPercentage && durationDamagePercent > 0 || !isPercentage && durationDamage > 0)
                    {
                        if (Level > 0) durationDamage += durationDamageLevelGrowth * (Level - 1);
                        if (Level > 0) durationDamagePercent += durationDamageLevelGrowth * (Level - 1);
                        string damageString = isPercentage ? $"受到 {durationDamagePercent * 100:0.##}% 当前生命值的" : $"受到 {durationDamage:0.##} 点" + CharacterSet.GetDamageTypeName(damageType);
                        _description = $"{GetEffectTypeName(_effectType)}：每{GameplayEquilibriumConstant.InGameTime}{damageString}！持续时间：{持续时间}！";
                    }
                    break;
                case EffectType.InterruptCasting:
                    _description = "打断施法：中断其正在进行的吟唱。";
                    break;
                default:
                    break;
            }
        }

        private static string GetEffectTypeName(EffectType type)
        {
            return type switch
            {
                EffectType.Taunt => "愤怒",
                EffectType.Silence => "封技",
                EffectType.Bleed => "气绝",
                EffectType.Cripple => "战斗不能",
                EffectType.Burn => "燃烧",
                EffectType.Poison => "中毒",
                _ => SkillSet.GetEffectTypeName(type)
            };
        }
    }
}
