using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using OshimaBot.Content.Skills;

namespace OshimaBot.Content.Items
{
    /// <summary>
    /// 装备 · 饰品【万能钥匙】—— 周期性的「全能决策点配额」
    /// <para/>设计意图：`TempActionQuota` 的 **全能档**（`AddTempActionQuota(effect, null, n)`）
    /// 是本项目**完全空白**的维度——现有 3 处配额用法全都指定了具体行动类型
    /// （`疾走`/`闪现` 给战技、`咒怨洪流` 给普攻）。全能配额意味着"这一回合任何行动都能多做一次"。
    /// <para/>⚠ 关键：**配额必须与决策点一起给**。实测表明决策点才是硬约束（AI 行动循环持续到
    /// `CurrentDecisionPoints` 耗尽），只给配额而不给 DP 等于空给（`咒怨洪流` 即受此限制）
    /// ⇒ 本装备每次都**同时**发放「+1 全能配额」与「+1 决策点」。
    /// <para/>品质决定技能等级（Orange 4 / Red 7 / Gold 10），等级只影响发放间隔（39 / 34.5 / 30 秒）。
    /// </summary>
    public abstract class 万能钥匙 : Item
    {
        protected 万能钥匙(Character? character, int 技能等级) : base(ItemType.Accessory)
        {
            Skills.Passives.Add(new 万能钥匙技能(character, this) { Level = 技能等级 });
        }

        public override string Description => Skills.Passives.Count > 0 ? Skills.Passives.First().Description : "";
        public override string BackgroundStory => "黄铜齿面被磨得发亮。它不保证你打开哪扇门，只保证你总多一次机会去试。";
    }

    public class 万能钥匙1 : 万能钥匙
    {
        public override long Id => (long)AccessoryID.万能钥匙1;
        public override string Name => "万能钥匙 Lv.4";
        public override QualityType QualityType => QualityType.Orange;
        public 万能钥匙1(Character? character = null) : base(character, 4) { }
    }

    public class 万能钥匙2 : 万能钥匙
    {
        public override long Id => (long)AccessoryID.万能钥匙2;
        public override string Name => "万能钥匙 Lv.7";
        public override QualityType QualityType => QualityType.Red;
        public 万能钥匙2(Character? character = null) : base(character, 7) { }
    }

    public class 万能钥匙3 : 万能钥匙
    {
        public override long Id => (long)AccessoryID.万能钥匙3;
        public override string Name => "万能钥匙 Lv.10";
        public override QualityType QualityType => QualityType.Gold;
        public 万能钥匙3(Character? character = null) : base(character, 10) { }
    }

    public class 万能钥匙技能 : Skill
    {
        public override long Id => (long)ItemPassiveID.万能钥匙;
        public override string Name => "万能钥匙";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";

        public 万能钥匙技能(Character? character = null, Item? item = null) : base(SkillType.Passive, character)
        {
            Level = 4;   // 最低档兜底；三档装备分别写入 4 / 7 / 10
            Item = item;
            Effects.Add(new 万能钥匙特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 万能钥匙特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"每 {间隔:0.##} {GameplayEquilibriumConstant.InGameTime}获得一次「全能决策点配额」与 {决策点补充} 点决策点 —— " +
            $"本回合内任何行动（普攻 / 战技 / 魔法 / 爆发技 / 物品）都能多做一次。";

        private double _剩余间隔 = 0;

        /// <summary>发放间隔（秒）：技能等级越高越短（Lv4 = 39 / Lv7 = 34.5 / Lv10 = 30）</summary>
        private double 间隔 => 45 - Skill.Level * 1.5;

        /// <summary>随配额一起发放的决策点（保证配额能被兑现）</summary>
        private const int 决策点补充 = 1;

        public override void OnEffectGained(HookContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character is null || Skill.Character != character) return;
            _剩余间隔 = 间隔;
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character is null || Skill.Character != character) return;
            if (GamingQueue is null) return;
            _剩余间隔 -= ctx.Elapsed;
            if (_剩余间隔 > 0) return;
            _剩余间隔 = 间隔;
            if (!GamingQueue.CharacterDecisionPoints.TryGetValue(character, out DecisionPoints? dp) || dp is null) return;
            int before = dp.CurrentDecisionPoints;
            dp.CurrentDecisionPoints = Math.Min(before + 决策点补充, dp.MaxDecisionPoints);
            dp.AddTempActionQuota(this, null, 1);   // type = null ⇒ 全能配额
            WriteLine($"[ {character} ] 的万能钥匙生效：获得 {dp.CurrentDecisionPoints - before} 点决策点与一次全能行动配额！");
        }
    }
}
