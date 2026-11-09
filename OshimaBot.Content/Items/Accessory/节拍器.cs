using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using OshimaBot.Content.Skills;

namespace OshimaBot.Content.Items
{
    /// <summary>
    /// 装备 · 饰品【节拍器】—— 击杀回响，把「战果」转成「节奏」
    /// <para/>设计意图：决策点的常规来源是每回合恢复，本装备把它接到**击杀**上——
    /// 击杀是低频、高价值事件，天然自带限流，不会退化成常驻增益（对比"每回合 +N"那类）。
    /// <para/>触发：<see cref="Effect.AfterDeathCalculation"/>（全队列广播）中，自己为击杀者
    /// ⇒ 记入待兑现；到**下一个自身回合开始**时一次性发放（避免在他人的回合里改动自己的决策点）。
    /// <para/>效果：+N 决策点（Orange Lv4 = 2 / Red Lv7 = 2 / Gold Lv10 = 3），**带累积上限**防止连续击杀溢出。
    /// <para/>⚠ 召唤物 / 随从的死亡不计（`DeathContext.HasMaster`）。
    /// </summary>
    public abstract class 节拍器 : Item
    {
        protected 节拍器(Character? character, int 技能等级) : base(ItemType.Accessory)
        {
            Skills.Passives.Add(new 节拍器技能(character, this) { Level = 技能等级 });
        }

        public override string Description => Skills.Passives.Count > 0 ? Skills.Passives.First().Description : "";
        public override string BackgroundStory => "摆在腰侧的小小节拍器，指针只随倒下的人跳动一次。";
    }

    public class 节拍器1 : 节拍器
    {
        public override long Id => (long)AccessoryID.节拍器1;
        public override string Name => "节拍器 Lv.4";
        public override QualityType QualityType => QualityType.Orange;
        public 节拍器1(Character? character = null) : base(character, 4) { }
    }

    public class 节拍器2 : 节拍器
    {
        public override long Id => (long)AccessoryID.节拍器2;
        public override string Name => "节拍器 Lv.7";
        public override QualityType QualityType => QualityType.Red;
        public 节拍器2(Character? character = null) : base(character, 7) { }
    }

    public class 节拍器3 : 节拍器
    {
        public override long Id => (long)AccessoryID.节拍器3;
        public override string Name => "节拍器 Lv.10";
        public override QualityType QualityType => QualityType.Gold;
        public 节拍器3(Character? character = null) : base(character, 10) { }
    }

    public class 节拍器技能 : Skill
    {
        public override long Id => (long)ItemPassiveID.节拍器;
        public override string Name => "节拍器";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";

        public 节拍器技能(Character? character = null, Item? item = null) : base(SkillType.Passive, character)
        {
            Level = 4;   // 最低档兜底；三档装备分别写入 4 / 7 / 10
            Item = item;
            Effects.Add(new 节拍器特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    public class 节拍器特效(Skill skill) : Effect(skill)
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"每次击杀为其积累 {单次补充} 点决策点，在下一个自身回合开始时发放（最多累积 {累积上限} 点）。";

        /// <summary>待兑现的决策点（击杀时累积，自身回合开始时发放）</summary>
        private int _待兑现 = 0;

        /// <summary>单次击杀补充量（Lv4–7 = 2 / Lv10 = 3）</summary>
        private int 单次补充 => 1 + Skill.Level / 4;

        /// <summary>累积上限：防止连续击杀一次性灌入过多决策点</summary>
        private int 累积上限 => 单次补充 * 2;

        public override void AfterDeathCalculation(DeathContext ctx)
        {
            if (Skill.Character is null) return;
            if (ctx.Killer != Skill.Character) return;
            if (ctx.Trigger == Skill.Character) return;
            if (ctx.HasMaster) return;   // 召唤物 / 随从的死亡不计
            _待兑现 = Math.Min(_待兑现 + 单次补充, 累积上限);
            WriteLine($"[ {Skill.Character} ] 的节拍器记下了战果，将在下个自身回合兑现 {_待兑现} 点决策点。");
        }

        public override void OnTurnStart(TurnContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (Skill.Character is null || Skill.Character != character) return;
            if (_待兑现 <= 0) return;
            if (GamingQueue is null) return;
            if (!GamingQueue.CharacterDecisionPoints.TryGetValue(character, out DecisionPoints? dp) || dp is null) return;
            int before = dp.CurrentDecisionPoints;
            dp.CurrentDecisionPoints = Math.Min(before + _待兑现, dp.MaxDecisionPoints);
            int actual = dp.CurrentDecisionPoints - before;
            WriteLine($"[ {character} ] 的节拍器兑现了战果：获得 {actual} 点决策点（{before} → {dp.CurrentDecisionPoints} / {dp.MaxDecisionPoints}）。");
            _待兑现 = 0;
        }
    }
}
