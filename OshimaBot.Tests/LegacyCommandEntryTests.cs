using Microsoft.Extensions.Logging.Abstractions;
using OshimaBot.Commands;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// L0 入口等价：旧版帮助里的 163 条命令，必须落到迁移矩阵声明的当前入口
/// （<c>migration/matrix.tsv</c> 的「当前入口」列），或者命中
/// <see cref="LegacyAliases.Redirects"/> 给出明确说明。
/// <para>数据来自矩阵；要加兼容请改 <c>LegacyAliases</c> 或矩阵，再重跑
/// <c>python migration/gen_legacy_entry_test.py</c>。</para>
/// </summary>
public class LegacyCommandEntryTests
{
    /// <summary>旧命令 → 矩阵声明的当前入口（空字符串表示矩阵里是 —，只应给说明）。</summary>
    private static readonly (string Legacy, string Entry)[] LegacyCommands =
    [
        ("创建存档", "/注册"),
        ("还原存档", ""),
        ("我的存档", "/状态"),
        ("我角色", "/我的角色"),
        ("我的角色", "/我的角色"),
        ("设置主战", "/设主战"),
        ("我的主战", "/我的角色"),
        ("开启练级", "/练级"),
        ("练级结算", "/收取练级"),
        ("练级信息", "/练级"),
        ("我的状态", "/状态"),
        ("装备", "/装备"),
        ("取消装备", "/卸下"),
        ("加物", ""),
        ("减物", ""),
        ("角色升级", "/升级"),
        ("角色突破", "/突破"),
        ("突破信息", "/突破"),
        ("普攻升级", "/普攻升级"),
        ("查看普攻升级", "/查看普攻升级"),
        ("技能升级", "/技能升级"),
        ("查看技能升级", "/查看技能升级"),
        ("角色重随", "/角色重随 · /确认重随 · /取消重随"),
        ("角色改名", "/角色改名 · /我的改名"),
        ("我的小队", "/小队"),
        ("设置小队", "/小队"),
        ("小队添加", "/小队 加入"),
        ("小队移除", "/小队 移除"),
        ("小队状态", "/小队"),
        ("清空小队", "/小队"),
        ("生命之泉", ""),
        ("酒馆", ""),
        ("我的库存", "/背包"),
        ("分类库存", "/背包"),
        ("物品库存", "/背包"),
        ("角色库存", "/背包"),
        ("我的物品", "/背包"),
        ("库存搜索2", "/库存搜索"),
        ("库存搜索", "/库存搜索"),
        ("抽卡", "/抽卡"),
        ("十连抽卡", "/十连抽卡"),
        ("钻石抽卡", "/钻石抽卡"),
        ("钻石十连抽卡", "/钻石十连抽卡"),
        ("兑换金币", "/兑换金币"),
        ("使用", "/使用"),
        ("使用魔法卡", "/使用魔法卡"),
        ("合成魔法卡", "/合成魔法卡"),
        ("分解物品", "/分解"),
        ("分解", "/分解"),
        ("强制分解", "/分解"),
        ("品质分解", "/品质分解"),
        ("锻造配方", "/锻造 @1 x40 [@2 x20…]"),
        ("锻造信息", "/锻造 信息"),
        ("取消锻造", "/锻造 取消"),
        ("模拟锻造", "/锻造 模拟 @1 x40"),
        ("确认开始锻造", "/锻造 @1 x40"),
        ("决斗", "/决斗"),
        ("完整决斗", "/完整决斗"),
        ("小队决斗", "/小队决斗"),
        ("查询boss", "/查询Boss"),
        ("讨伐boss", "/Boss"),
        ("小队讨伐boss", "/小队讨伐Boss"),
        ("排行榜", "/排行榜"),
        ("金币排行榜", "/排行榜 金币"),
        ("钻石排行榜", "/排行榜"),
        ("赛马排行榜", "/赛马排行榜"),
        ("共斗排行榜", "/共斗排行榜"),
        ("锻造排行榜", "/锻造排行榜"),
        ("任务列表", "/每日任务 · /任务列表"),
        ("做任务", "/做任务"),
        ("任务信息", "/任务信息"),
        ("任务结算", "/领奖 · /任务结算"),
        ("世界地图", "/世界地图"),
        ("主城", "/主城"),
        ("查地区", "/查地区 [编号|名称]"),
        ("探索", "/探索"),
        ("小队探索", "/小队探索 <地区>"),
        ("探索结算", "/探索结算"),
        ("挑战金币秘境", "/秘境"),
        ("挑战钻石秘境", "/秘境"),
        ("挑战经验秘境", "/秘境"),
        ("挑战地区秘境", "/秘境"),
        ("挑战突破秘境", "/秘境"),
        ("挑战技能秘境", "/秘境"),
        ("挑战魔法卡秘境", "/秘境"),
        ("创建房间", "/创建房间"),
        ("加入房间", "/加入房间"),
        ("退出房间", "/退出房间"),
        ("开始游戏", "/开始游戏"),
        ("房间列表", "/房间列表"),
        ("房间信息", "/房间信息"),
        ("创建赛马", "/创建赛马"),
        ("加入赛马", "/加入赛马"),
        ("创建共斗", "/创建共斗"),
        ("创建混战", "/创建混战"),
        ("创建团战", "/创建团战"),
        ("社团列表", "/社团列表"),
        ("查看社团", "/查看社团"),
        ("我的社团", "/我的社团"),
        ("私信", "/私信 · /收件箱 · /读信"),
        ("加入社团", "/加入社团"),
        ("邀请加入", "/邀请加入"),
        ("退出社团", "/退出社团"),
        ("创建社团", "/创建社团"),
        ("查看社团成员", "/查看社团成员"),
        ("查看社团管理", "/查看社团管理"),
        ("查看申请人列表", "/查看申请人列表"),
        ("查看受邀人列表", "/查看受邀人列表"),
        ("解散社团", "/解散社团"),
        ("社团批准", "/社团批准"),
        ("社团拒绝", "/社团拒绝"),
        ("社团踢出", "/社团踢出"),
        ("社团转让", "/社团转让"),
        ("社团设置", "/社团设置"),
        ("社团捐献", "/社团捐献"),
        ("社团任务列表", "/社团任务"),
        ("做社团任务", "/社团任务 提交 @编号 [x数量]"),
        ("社团任务结算", "/社团任务 领取 编号"),
        ("社团商店", "/社团商店"),
        ("社团商店查看", "/社团商店"),
        ("社团商店购买", "/社团商店 买 编号 [x数量]"),
        ("社团商店出售", "/社团商店 卖 @编号 [x数量]"),
        ("社团市场", "/社团市场"),
        ("社团市场出售", "/社团市场 上架 @编号 价格"),
        ("社团市场下架", "/社团市场 下架 编号"),
        ("社团市场查看", "/社团市场"),
        ("社团市场购买", "/社团市场 买 编号"),
        ("社团市场清空", "/社团市场 清空"),
        ("签到", "/签到"),
        ("公告", "/公告 · /设置公告"),
        ("活动", "/活动"),
        ("查活动", "/查活动"),
        ("做活动任务", "/做活动任务"),
        ("领取奖励", "/领取奖励"),
        ("每日商店", "/商店"),
        ("商店1", "/商店1"),
        ("商店2", "/商店2"),
        ("商店3", "/商店3"),
        ("商店4", "/商店4"),
        ("锻造商店", "/商店5"),
        ("商店5", "/商店5"),
        ("赛马商店", "/商店6"),
        ("商店6", "/商店6"),
        ("共斗商店", "/商店7"),
        ("商店7", "/商店7"),
        ("商店查看", "/商店详情"),
        ("商店购买", "/购买"),
        ("商店出售", "/商店出售"),
        ("创建报价", "/创建报价"),
        ("我的报价", "/报价列表"),
        ("发送报价", "/发送报价"),
        ("取消报价", "/撤回报价"),
        ("接受报价", "/接受报价"),
        ("拒绝报价", "/拒绝报价"),
        ("报价添加物品", "/报价添加物品"),
        ("报价添加对方物品", "/报价添加对方物品"),
        ("报价移除物品", "/报价移除物品"),
        ("报价移除对方物品", "/报价移除对方物品"),
        ("市场", "/市场"),
        ("市场出售", "/上架"),
        ("市场下架", "/下架"),
        ("市场查看", "/市场"),
        ("市场购买", "/购买市场"),
    ];

    private static CommandRegistry CreateRegistry()
    {
        CommandDependencies dependencies = new(
            null!, null!, new NullBindCodes(), new NullSessions(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!,
            NullLogger.Instance);
        return CommandCatalog.Build(dependencies);
    }

    [Fact]
    public void LegacyCommands_ResolveOrExplain()
    {
        Assert.Equal(163, LegacyCommands.Length);
        CommandRegistry registry = CreateRegistry();

        List<string> unresolved = [];
        foreach ((string legacy, string _) in LegacyCommands)
        {
            if (registry.Resolve(legacy) is not null) continue;
            if (LegacyAliases.Explain(legacy) is not null) continue;
            unresolved.Add(legacy);
        }

        Assert.True(unresolved.Count == 0,
            $"以下旧命令既不能解析也没有说明（共 {unresolved.Count} 条）：\n  " + string.Join("\n  ", unresolved));
    }

    /// <summary>
    /// 能解析的旧命令必须落到矩阵声明的入口之一，避免被前缀匹配带偏；
    /// 已用 <see cref="LegacyAliases.Redirects"/> 给出说明的行（参数形态变了）也算通过。
    /// </summary>
    [Fact]
    public void LegacyCommands_LandOnTheDeclaredEntry()
    {
        CommandRegistry registry = CreateRegistry();
        List<string> wrong = [];

        foreach ((string legacy, string entry) in LegacyCommands)
        {
            CommandMatch? actual = registry.Resolve(legacy);
            string? explanation = LegacyAliases.Explain(legacy);

            if (entry.Length == 0)
            {
                if (actual is not null) wrong.Add($"{legacy} → 矩阵说没有入口，实际解析到 {actual.Command.Key}");
                else if (explanation is null) wrong.Add($"{legacy} → 矩阵说没有入口，也没有说明");
                continue;
            }

            // 矩阵的「当前入口」可能列多个（用 · 分隔），命中任一即算对齐
            string[] candidates = entry.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            List<string> keys = [];
            foreach (string candidate in candidates)
            {
                string head = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].TrimStart('/');
                CommandMatch? resolved = registry.Resolve(head);
                if (resolved is not null) keys.Add(resolved.Command.Key);
            }

            if (keys.Count == 0)
            {
                wrong.Add($"{legacy} → 矩阵声明的入口 /{entry} 一个都不存在");
                continue;
            }
            if (actual is not null && keys.Contains(actual.Command.Key, StringComparer.OrdinalIgnoreCase)) continue;
            if (explanation is not null) continue;   // 参数形态已变：给说明不算错

            wrong.Add(actual is null
                ? $"{legacy} → 解析失败（应落到 {string.Join(" / ", keys)}）"
                : $"{legacy} → 落到 {actual.Command.Key}，矩阵声明的是 {string.Join(" / ", keys)}");
        }

        Assert.True(wrong.Count == 0,
            $"以下旧命令落点与矩阵不一致（共 {wrong.Count} 条）：\n  " + string.Join("\n  ", wrong));
    }

    [Fact]
    public void LegacyRewrites_PointAtARealCommand()
    {
        CommandRegistry registry = CreateRegistry();
        List<string> broken = [];

        foreach ((string legacy, string target) in LegacyAliases.Rewrites)
        {
            if (registry.Resolve(target) is null) broken.Add($"{legacy} → {target}");
        }

        Assert.True(broken.Count == 0, "重写目标不存在：\n  " + string.Join("\n  ", broken));
    }

    [Fact]
    public void LegacyCommands_KeepTheirArguments()
    {
        CommandRegistry registry = CreateRegistry();

        // 重写只替换动词，参数原样接在后面：模拟锻造 @1 x40 → 锻造 模拟 @1 x40
        CommandMatch? match = registry.Resolve("模拟锻造 @1 x40");
        Assert.NotNull(match);
        Assert.Equal("forge", match!.Command.Key);
        Assert.Equal(["模拟", "@1", "x40"], match.Args);

        // 纯改名同样保留参数：市场出售 3 8000 → 上架 3 8000
        CommandMatch? renamed = registry.Resolve("市场出售 3 8000");
        Assert.NotNull(renamed);
        Assert.Equal(["3", "8000"], renamed!.Args);
    }

    [Fact]
    public void ScopeExcludedCommands_ExplainInsteadOfFailing()
    {
        foreach (string command in new[] { "生命之泉", "酒馆", "加物", "减物" })
        {
            Assert.Null(CreateRegistry().Resolve(command));
            Assert.False(string.IsNullOrWhiteSpace(LegacyAliases.Explain(command)), $"{command} 缺少排除说明");
        }
    }

    private sealed class NullBindCodes : IBindCodeService
    {
        public int TtlSeconds => 60;
        public Task<string> IssueAsync(long userId, string purpose = "qq_bind", CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<BindRedeemResult> RedeemAsync(string code, ExternalIdentity identity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NullSessions : IUserSessionService
    {
        public Task<UserSessionState?> GetAsync(long userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SetPendingAsync(long userId, string action, string payloadJson, TimeSpan ttl,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task ClearPendingAsync(long userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task SetPageCursorAsync(long userId, string cursor, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
