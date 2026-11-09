using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using FunGame.Core.Model.Framework;
using OshimaBot.Content.Effects.PassiveEffects;
using OshimaBot.Content.Skills;

namespace OshimaBot.Content.Items
{
    /// <summary>
    /// 装备 · 饰品【行军医箱】—— 可携带的团队急救模块
    /// <para/>设计意图：模组中角色的被动 / 爆发技几乎都为自身服务，缺少「跨角色监听」的辅助。
    /// 本装备把「监听队友受击 → 消耗自身能量急救」做成与穿戴者身份<b>解耦</b>的模块：
    /// 任意角色穿上它都会临时成为队伍的治疗点，因此它可以随空投在不同角色之间流转。
    /// <para/>触发：穿戴者为每名队友挂【巡诊标记】，标记以宿主身份收到
    /// <see cref="Effect.AfterDamageCalculation"/> 后转发给穿戴者；穿戴者消耗能量为其回血。
    /// <para/>标尺（手册 §6.4 被动·回复 ≤2–3%/秒）：Gold 档 10% 最大生命值 / 10 秒 ⇒ 约 1%/秒 ✓
    /// <para/>⚠ 幂等约定：装备会被周期空投<b>随时替换</b>，因此
    /// <see cref="行军医箱特效.OnEffectLost"/> 必须把挂在他人状态栏上的【巡诊标记】全部撤回，
    /// 否则会残留成「幽灵治疗」。
    /// <para/>⚠ 主动权约定：是否救援由<b>装备持有者</b>通过询问（<see cref="Effect.Inquiry"/>）决定。
    /// 询问的 <c>DefaultChoice</c> 用条件表达式计算——只有当持有者处于预释放爆发技状态、
    /// 且这次扣能量会让预释放付不起能量时，才默认「否」（门槛取「角色所有爆发技的最高门槛」作保守上界，
    /// 见 <see cref="行军医箱特效.够付预释放门槛"/>）；其余情况默认「是」。
    /// 「否」会作为 AI 托管 / 无人应答时的行为，也会成为玩家弹窗的默认高亮项。玩家拒绝<b>不消耗冷却</b>。
    /// </summary>
    public abstract class 行军医箱 : Item
    {
        protected 行军医箱(Character? character, int 技能等级) : base(ItemType.Accessory)
        {
            Skills.Passives.Add(new 行军医箱技能(character, this) { Level = 技能等级 });
        }

        public override string Description => Skills.Passives.Count > 0 ? Skills.Passives.First().Description : "";

        public override string BackgroundStory => "随军多年的旧式医疗箱，扣锁处还缠着一圈早已褪色的绷带。打开时会有温热的光晕沿着箱内的试剂管缓缓流转——只要队伍里还有人站着，它就不算用完。";
    }

    public class 行军医箱1 : 行军医箱
    {
        public override long Id => (long)AccessoryID.行军医箱1;
        public override string Name => "行军医箱 +4%";
        public override QualityType QualityType => QualityType.Orange;

        public 行军医箱1(Character? character = null) : base(character, 4) { }
    }

    public class 行军医箱2 : 行军医箱
    {
        public override long Id => (long)AccessoryID.行军医箱2;
        public override string Name => "行军医箱 +7%";
        public override QualityType QualityType => QualityType.Red;

        public 行军医箱2(Character? character = null) : base(character, 7) { }
    }

    public class 行军医箱3 : 行军医箱
    {
        public override long Id => (long)AccessoryID.行军医箱3;
        public override string Name => "行军医箱 +10%";
        public override QualityType QualityType => QualityType.Gold;

        public 行军医箱3(Character? character = null) : base(character, 10) { }
    }

    public class 行军医箱技能 : Skill
    {
        public override long Id => (long)ItemPassiveID.行军医箱;
        public override string Name => "行军医箱";
        public override string Description => Effects.Count > 0 ? Effects.First().Description : "";

        public 行军医箱技能(Character? character = null, Item? item = null) : base(SkillType.Passive, character)
        {
            Level = 4;   // 最低档兜底；三档装备分别写入 4 / 7 / 10
            Item = item;
            Effects.Add(new 行军医箱特效(this));
        }

        public override IEnumerable<Effect> AddPassiveEffectToCharacter()
        {
            return Effects;
        }
    }

    /// <summary>
    /// 【行军医箱】的控制器：常驻在穿戴者身上，负责维护队友身上的【巡诊标记】并执行急救。
    /// <para/>冷却 / 能量状态集中在这里，因此多名队友同时受击时只有一次响应（救援成功后共享冷却）。
    /// <para/>是否救援由穿戴者本人通过询问决定；拒绝不消耗冷却。
    /// </summary>
    public class 行军医箱特效 : Effect
    {
        public override long Id => Skill.Id;
        public override string Name => Skill.Name;
        public override string Description =>
            $"当生命值低于 {触发阈值 * 100:0}% 的队友受到伤害时，可由你决定是否消耗自身 {EP消耗:0.##} 点能量，" +
            $"为其回复 {回复系数 * 100:0.##}% 最大生命值（成功救援后 {冷却时间:0.##} {GameplayEquilibriumConstant.InGameTime}内不再响应）。";

        public override EffectType EffectType => EffectType.Item;

        private double _剩余冷却 = 0;

        /// <summary>
        /// 回复系数：等级 &lt; 4 时取兜底 1%（低于最低档，正常用不到）；否则等于技能等级
        /// （Orange 4 级 = 4%，Red 7 级 = 7%，Gold 10 级 = 10%）
        /// </summary>
        private double 回复系数 => Level < 4 ? 0.01 : Level * 0.01;

        /// <summary>目标生命值低于该比例才响应</summary>
        private const double 触发阈值 = 0.5;

        /// <summary>响应冷却（穿戴者共享，避免多名队友同时触发）</summary>
        private const double 冷却时间 = 10;

        /// <summary>每次响应消耗的能量，作为「急救要付出代价」的取舍点</summary>
        private const double EP消耗 = 20;

        public 行军医箱特效(Skill skill) : base(skill)
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

        /// <summary>为每名存活队友补挂【巡诊标记】（按本控制器的技能实例去重）</summary>
        private void 发送标记()
        {
            Character? wearer = 穿戴者;
            if (GamingQueue is null || wearer is null) return;
            foreach (Character ally in GamingQueue.GetTeammates(wearer))
            {
                if (ally.HP <= 0) continue;
                if (ally.Effects.Any(e => e is 巡诊标记 m && m.Skill == Skill)) continue;
                new 巡诊标记(Skill, wearer, this).AddToCharacter(ally);
            }
        }

        /// <summary>撤回所有由本装备挂出的【巡诊标记】</summary>
        private void 撤回标记()
        {
            Character? wearer = 穿戴者;
            if (GamingQueue is null || wearer is null) return;
            List<Character> all = [.. GamingQueue.AllCharacters.Union(GamingQueue.Queue)];
            foreach (Character character in all)
            {
                List<巡诊标记> markers = [.. character.Effects.OfType<巡诊标记>().Where(m => m.Skill == Skill)];
                foreach (巡诊标记 marker in markers)
                {
                    marker.RemoveFromCharacter(character);
                }
            }
        }

        /// <summary>
        /// 由【巡诊标记】转发：队友受击且跌破阈值时，向穿戴者发起询问，由其决定是否消耗能量急救。
        /// <para/>询问的 <c>DefaultChoice</c> 用条件表达式计算——穿戴者处于预释放爆发技状态时为「否」，
        /// 否则为「是」；它同时决定 AI 托管 / 无人应答时的实际行为，也是玩家弹窗的默认高亮项。
        /// <para/>玩家拒绝<b>不消耗冷却</b>。
        /// </summary>
        public bool TryEmergencyHeal(Character host)
        {
            Character? wearer = 穿戴者;
            if (GamingQueue is null || wearer is null) return false;
            if (wearer.HP <= 0 || host.HP <= 0) return false;
            if (_剩余冷却 > 0) return false;
            if (host.HP / Math.Max(1, host.MaxHP) >= 触发阈值) return false;
            if (wearer.EP < EP消耗) return false;
            double heal = host.MaxHP * 回复系数;
            if (heal <= 0) return false;

            // 预释放期间：只有当这次扣能量真的会让预释放的爆发技付不起能量时，才默认保守（玩家仍可强行救援）
            bool 危及预释放 = wearer.CharacterState == CharacterState.PreCastSuperSkill && !够付预释放门槛(wearer, EP消耗);

            InquiryResponse response = Inquiry(wearer, new InquiryOptions(InquiryType.BinaryChoice, nameof(行军医箱))
            {
                Description = $"队友 [ {host} ] 生命值告急，是否消耗 {EP消耗:0.##} 点能量为其回复 {heal:0.##} 点生命值？"
                            + (危及预释放 ? "（警告：你正处于预释放爆发技状态，扣减能量将使该爆发技因能量不足而无法释放）" : ""),
                DefaultChoice = 危及预释放 ? "否" : "是",
                CanCancel = false
            });

            // 拒绝不消耗冷却，玩家可以连续拒绝而不被惩罚
            if (response.Choices.Count == 0 || response.Choices[0] != "是") return false;

            _剩余冷却 = 冷却时间;
            wearer.EP -= EP消耗;
            WriteLine($"[ {wearer} ] 展开了行军医箱，消耗 {EP消耗:0.##} 点能量，为 [ {host} ] 回复 {heal:0.##} 点生命值！");
            HealToTarget(wearer, host, heal);
            return true;
        }

        /// <summary>
        /// 扣除 <paramref name="消耗"/> 点能量后，是否仍付得起该角色任一爆发技的预释放门槛。
        /// <para/>Core 未公开预释放字典（<c>_castingSuperSkills</c>），因此这里对「角色所有爆发技」取最高门槛作为<b>保守上界</b>：
        /// 只要上界付得起，无论预释放的是哪一个都不会失败。
        /// <para/>· 普通爆发技：门槛 = <see cref="Skill.RealEPCost"/>（即固定的 <see cref="Skill.EPCost"/>）
        /// <para/>· <see cref="Skill.CostAllEP"/> 型：真实门槛是 <see cref="Skill.MinCostEP"/>。
        /// 它的 <c>RealEPCost</c> 是 <c>max(MinCostEP, 当前EP)</c>，跟着当前 EP 走，直接取会显著过估
        /// （例如 EP 150 时上界被算成 150，于是永远拒绝救援）。
        /// </summary>
        private static bool 够付预释放门槛(Character wearer, double 消耗)
        {
            double 门槛 = wearer.Skills
                .Where(s => s.SkillType == SkillType.SuperSkill)
                .Select(s => s.CostAllEP ? s.MinCostEP : s.RealEPCost)
                .DefaultIfEmpty(0)
                .Max();
            return wearer.EP - 消耗 >= 门槛;
        }
    }
}
