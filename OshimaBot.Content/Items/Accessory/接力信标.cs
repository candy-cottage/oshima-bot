using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.PassiveEffects;
using OshimaBot.Content.Skills;

namespace OshimaBot.Content.Items
{
    /// <summary>
    /// 装备 · 饰品【接力信标】—— 持续压制团队硬直的「节奏器」
    /// <para/>设计意图：硬直决定出手频率，但模组里几乎没有「跨角色削减硬直」的辅助。
    /// 本装备把队友普攻 / 技能后的硬直整体下调，让团队出手衔接更紧凑（接力）；
    /// 并把「被空投重新部署」这个节奏本身变成一次全队回能。
    /// <para/>实现：给每名队友挂【信标标记】，标记在硬直钩子里返回比率修正
    /// （不做任何属性增减，天然幂等）。
    /// <para/>数值：由技能等级决定（Orange 4 级 = −8% / 6，Red 7 级 = −11% / 12，Gold 10 级 = −14% / 18）；
    /// 等级 &lt; 4 时取兜底 −5% 与 1（正常用不到）。
    /// <para/>标尺（手册 §6.4 被动·增益 ≤36%）：Gold 档硬直 −14% ✓
    /// <para/>⚠ 幂等约定：装备会被周期空投<b>随时替换</b>，因此
    /// <see cref="接力信标特效.OnEffectLost"/> 必须把挂在他人状态栏上的【信标标记】全部撤回，
    /// 否则光环会残留在队友身上。
    /// </summary>
    public abstract class 接力信标 : Item
    {
        protected 接力信标(Character? character, int 技能等级) : base(ItemType.Accessory)
        {
            Skills.Passives.Add(new 接力信标技能(character, this) { Level = 技能等级 });
        }

        public override string Description => Skills.Passives.Count > 0 ? Skills.Passives.First().Description : "";

        public override string BackgroundStory => "一截被磨得发亮的信号杆，顶端绑着褪色的布条。它不发声，只是把每一个人的步点悄悄对齐。";
    }

    public class 接力信标1 : 接力信标
    {
        public override long Id => (long)AccessoryID.接力信标1;
        public override string Name => "接力信标 -8%";
        public override QualityType QualityType => QualityType.Orange;

        public 接力信标1(Character? character = null) : base(character, 4) { }
    }

    public class 接力信标2 : 接力信标
    {
        public override long Id => (long)AccessoryID.接力信标2;
        public override string Name => "接力信标 -11%";
        public override QualityType QualityType => QualityType.Red;

        public 接力信标2(Character? character = null) : base(character, 7) { }
    }

    public class 接力信标3 : 接力信标
    {
        public override long Id => (long)AccessoryID.接力信标3;
        public override string Name => "接力信标 -14%";
        public override QualityType QualityType => QualityType.Gold;

        public 接力信标3(Character? character = null) : base(character, 10) { }
    }

    public class 接力信标技能 : Skill
    {
        public override long Id => (long)ItemPassiveID.接力信标;
        public override string Name => "接力信标";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";

        public 接力信标技能(Character? character = null, Item? item = null) : base(SkillType.Passive, character)
        {
            Level = 4;   // 最低档兜底；三档装备分别写入 4 / 7 / 10
            Item = item;
            Effects.Add(new 接力信标特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    /// <summary>
    /// 【接力信标】的控制器：常驻在穿戴者身上，负责维护队友身上的【信标标记】，
    /// 并在「部署」时（装备 / 被空投换上）为全队结算一次回能。
    /// </summary>
    public class 接力信标特效 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"装备期间，队友的普攻与技能硬直减少 {硬直减免 * 100:0.##}%；" +
            $"每次被重新部署（装备）时，全体队友回复 {部署回能:0.##} 点能量。";

        public override EffectType EffectType => EffectType.Item;

        private bool _已部署 = false;

        /// <summary>
        /// 硬直减免：等级 &lt; 4 时取兜底 5%（正常用不到）；
        /// 否则 Orange 4 级 = 8%，Red 7 级 = 11%，Gold 10 级 = 14%
        /// </summary>
        private double 硬直减免 => Level < 4 ? 0.05 : (Level + 4) * 0.01;

        /// <summary>
        /// 部署回能（对每名队友）：等级 &lt; 4 时取兜底 1（正常用不到）；
        /// 否则 Orange 4 级 = 6，Red 7 级 = 12，Gold 10 级 = 18
        /// </summary>
        private double 部署回能 => Level < 4 ? 1 : 2 * (Level - 1);

        public 接力信标特效(Skill skill) : base(skill)
        {
        }

        private Character? 穿戴者 => Skill.Character;

        public override void OnEffectGained(HookContext ctx)
        {
            // 装备 = 部署：挂光环 + 全队回能（队伍未就绪时交给 OnGameStart / OnTimeElapsed 重试）
            发送标记();
            部署();
        }

        public override void OnGameStart(HookContext ctx)
        {
            发送标记();
            部署();
        }

        public override void OnTimeElapsed(TimeLapseContext ctx)
        {
            if (ctx.Trigger is not Character character) return;
            if (穿戴者 is null || 穿戴者 != character) return;
            // 兜底：新入场 / 复活的队友补挂标记；部署若因队伍未就绪而错过，这里补结算
            发送标记();
            部署();
        }

        public override void OnEffectLost(HookContext ctx)
        {
            // 卸装 / 换装：撤回光环标记，保证幂等；同时允许重新装备时再次"部署"
            撤回标记();
            _已部署 = false;
        }

        /// <summary>为每名存活队友补挂【信标标记】（按本控制器的技能实例去重）</summary>
        private void 发送标记()
        {
            Character? wearer = 穿戴者;
            if (GamingQueue is null || wearer is null) return;
            foreach (Character ally in GamingQueue.GetTeammates(wearer))
            {
                if (ally.HP <= 0) continue;
                if (ally.Effects.Any(e => e is 信标标记 m && m.Skill == Skill)) continue;
                new 信标标记(Skill, wearer, 硬直减免).AddToCharacter(ally);
            }
        }

        /// <summary>撤回所有由本装备挂出的【信标标记】</summary>
        private void 撤回标记()
        {
            Character? wearer = 穿戴者;
            if (GamingQueue is null || wearer is null) return;
            List<Character> all = [.. GamingQueue.AllCharacters.Union(GamingQueue.Queue)];
            foreach (Character character in all)
            {
                List<信标标记> markers = [.. character.Effects.OfType<信标标记>().Where(m => m.Skill == Skill)];
                foreach (信标标记 marker in markers)
                {
                    marker.RemoveFromCharacter(character);
                }
            }
        }

        /// <summary>部署结算：为全队回复能量，每个装备实例只结算一次</summary>
        private void 部署()
        {
            if (_已部署) return;
            Character? wearer = 穿戴者;
            if (GamingQueue is null || wearer is null) return;
            List<Character> allies = [.. GamingQueue.GetTeammates(wearer).Where(c => c.HP > 0)];
            if (allies.Count == 0) return;   // 队伍未就绪：留给后续钩子重试
            _已部署 = true;
            foreach (Character ally in allies)
            {
                ally.EP += 部署回能;
            }
            WriteLine($"[ {wearer} ] 部署了接力信标，全体队友回复了 {部署回能:0.##} 点能量！");
        }
    }
}
