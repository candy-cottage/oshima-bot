using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;
using OshimaBot.Content.Skills;

namespace OshimaBot.Content.Items
{
    /// <summary>
    /// 装备 · 饰品【共鸣核心】—— 把队友的出手转成自身能量的「电池」
    /// <para/>设计意图：与【行军医箱】（消耗能量换队友生命）互补，本条是<b>反向</b>的资源流：
    /// 队友的每一次有效伤害都会为穿戴者充能。它把"队友愿不愿意持续输出"变成自身的续航条件，
    /// 因此不需要任何数值增益，也能让通用的普攻/技能变得对团队有价值。
    /// <para/>触发：穿戴者为每名队友挂【共鸣标记】，标记以宿主身份收到
    /// <see cref="Effect.AfterDamageCalculation"/>（宿主为攻击方）后转发给穿戴者；穿戴者回复能量。
    /// <para/>数值：回能量由技能等级决定（Orange 4 级 = 4、Red 7 级 = 8、Gold 10 级 = 12），
    /// 等级 &lt; 4 时取兜底 1（正常用不到）。
    /// <para/>⚠ 只对<b>队友</b>（不含穿戴者本人）生效，因此必须身处一支有输出能力的队伍才有收益。
    /// <para/>⚠ 幂等约定：装备会被周期空投<b>随时替换</b>，因此
    /// <see cref="共鸣核心特效.OnEffectLost"/> 必须把挂在他人状态栏上的【共鸣标记】全部撤回。
    /// </summary>
    public abstract class 共鸣核心 : Item
    {
        protected 共鸣核心(Character? character, int 技能等级) : base(ItemType.Accessory)
        {
            Skills.Passives.Add(new 共鸣核心技能(character, this) { Level = 技能等级 });
        }

        public override string Description => Skills.Passives.Count > 0 ? Skills.Passives.First().Description : "";

        public override string BackgroundStory => "核心内部封着一枚悬停的结晶。每当共鸣的同伴出手，它便泛起一圈微光——那是被传递回来的节拍。";
    }

    public class 共鸣核心1 : 共鸣核心
    {
        public override long Id => (long)AccessoryID.共鸣核心1;
        public override string Name => "共鸣核心 +4";
        public override QualityType QualityType => QualityType.Orange;

        public 共鸣核心1(Character? character = null) : base(character, 4) { }
    }

    public class 共鸣核心2 : 共鸣核心
    {
        public override long Id => (long)AccessoryID.共鸣核心2;
        public override string Name => "共鸣核心 +8";
        public override QualityType QualityType => QualityType.Red;

        public 共鸣核心2(Character? character = null) : base(character, 7) { }
    }

    public class 共鸣核心3 : 共鸣核心
    {
        public override long Id => (long)AccessoryID.共鸣核心3;
        public override string Name => "共鸣核心 +12";
        public override QualityType QualityType => QualityType.Gold;

        public 共鸣核心3(Character? character = null) : base(character, 10) { }
    }

    public class 共鸣核心技能 : Skill
    {
        public override long Id => (long)ItemPassiveID.共鸣核心;
        public override string Name => "共鸣核心";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";

        public 共鸣核心技能(Character? character = null, Item? item = null) : base(SkillType.Passive, character)
        {
            Level = 4;   // 最低档兜底；三档装备分别写入 4 / 7 / 10
            Item = item;
            Effects.Add(new 共鸣核心特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    /// <summary>
    /// 【共鸣核心】的控制器：常驻在穿戴者身上，负责维护队友身上的【共鸣标记】并结算回能。
    /// <para/>冷却集中在这里，因此队友的同一轮多段伤害（含 AoE）只会触发一次回能。
    /// </summary>
    public class 共鸣核心特效 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"共鸣范围内的队友每次造成伤害，你回复 {每次回能:0.##} 点能量" +
            $"（每 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}至多一次）。";

        public override EffectType EffectType => EffectType.Item;

        private double _剩余冷却 = 0;

        /// <summary>
        /// 每次回能：等级 &lt; 4 时取兜底 1（正常用不到）；否则按等级线性增长
        /// （Orange 4 级 = 4、Red 7 级 = 8、Gold 10 级 = 12）
        /// </summary>
        private double 每次回能 => Level < 4 ? 1 : 4 * (Level - 1) / 3.0;

        /// <summary>回能冷却（穿戴者共享，避免 AoE 多段伤害刷爆）</summary>
        private const double 冷却时间 = 2;

        public 共鸣核心特效(Skill skill) : base(skill)
        {
        }

        private Character? 穿戴者 => Skill.Character;

        public override void OnEffectGained(HookContext ctx)
        {
            _剩余冷却 = 0;
            // 开局前队列可能尚未就绪，真正的挂载交给 OnGameStart / OnTimeElapsed 兜底
            发送标记();
        }

        public override void OnGameStart(HookContext ctx)
        {
            发送标记();
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (穿戴者 is null || 穿戴者 != character) return;
            if (_剩余冷却 > 0) _剩余冷却 = Math.Max(0, _剩余冷却 - ctx.Elapsed);
            // 兜底：新入场 / 复活的队友补挂标记（内部去重）
            发送标记();
        }

        public override void OnEffectLost(HookContext ctx)
        {
            // 卸装 / 换装：必须把挂在他人状态栏上的标记全部撤回，保证幂等
            撤回标记();
        }

        /// <summary>为每名存活队友补挂【共鸣标记】（按本控制器的技能实例去重）</summary>
        private void 发送标记()
        {
            Character? wearer = 穿戴者;
            if (GamingQueue is null || wearer is null) return;
            foreach (Character ally in GamingQueue.GetTeammates(wearer))
            {
                if (ally.HP <= 0) continue;
                if (ally.Effects.Any(e => e is 共鸣标记 m && m.Skill == Skill)) continue;
                new 共鸣标记(Skill, wearer, this).AddToCharacter(ally);
            }
        }

        /// <summary>撤回所有由本装备挂出的【共鸣标记】</summary>
        private void 撤回标记()
        {
            Character? wearer = 穿戴者;
            if (GamingQueue is null || wearer is null) return;
            List<Character> all = [.. GamingQueue.AllCharacters.Union(GamingQueue.Queue)];
            foreach (Character character in all)
            {
                List<共鸣标记> markers = [.. character.Effects.OfType<共鸣标记>().Where(m => m.Skill == Skill)];
                foreach (共鸣标记 marker in markers)
                {
                    marker.RemoveFromCharacter(character);
                }
            }
        }

        /// <summary>由【共鸣标记】转发：队友造成伤害后为穿戴者充能</summary>
        public bool TryGainEnergy(Character ally)
        {
            Character? wearer = 穿戴者;
            if (GamingQueue is null || wearer is null) return false;
            if (wearer.HP <= 0) return false;
            if (_剩余冷却 > 0) return false;
            _剩余冷却 = 冷却时间;
            wearer.EP += 每次回能;
            WriteLine($"[ {wearer} ] 的共鸣核心因 [ {ally} ] 的出手而充能，回复了 {每次回能:0.##} 点能量！（当前 {wearer.EP:0.##}）");
            return true;
        }
    }
}
