namespace OshimaBot.Commands;

/// <summary>
/// 旧版帮助里 163 条命令的入口适配。
/// <para>迁移矩阵（<c>migration/matrix.tsv</c>）要求 L0 入口等价：旧命令能被解析，
/// 或者给出明确说明。已经同名/同别名可触发的命令不在这里；这里只放两类：</para>
/// <list type="bullet">
/// <item><b>重写</b>：旧名换个新名或加一个固定子命令就能走同一条处理链（<see cref="Rewrites"/>）。</item>
/// <item><b>说明</b>：参数形态变了、或该功能正式不迁移，给出明确提示而不是「未识别的指令」（<see cref="Redirects"/>）。</item>
/// </list>
/// <para>两张表都由 <c>LegacyCommandEntryTests</c> 守着：矩阵里的旧命令必须能被解析或命中说明。</para>
/// </summary>
public static class LegacyAliases
{
    /// <summary>
    /// 旧命令 → 新写法。值是「命令名 + 固定子命令」形式，调用者原来的参数会接在后面：
    /// <c>模拟锻造 @1 x40</c> → <c>锻造 模拟 @1 x40</c>。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Rewrites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // 存档
        ["创建存档"] = "注册",
        // 旧版「还原存档」要先说「确认还原存档」才动数据；新版是 /还原存档 + /还原存档 确认。
        ["确认还原存档"] = "还原存档 确认",
        ["我的存档"] = "状态",
        // 角色
        ["我角色"] = "我的角色",
        ["我的主战"] = "我的角色",
        ["设置主战"] = "设主战",
        ["开启练级"] = "练级",
        ["练级信息"] = "练级",
        ["练级结算"] = "收取练级",
        ["我的状态"] = "状态",
        ["角色升级"] = "升级",
        ["角色突破"] = "突破",
        ["突破信息"] = "突破",
        ["我的小队"] = "小队",
        ["小队状态"] = "小队",
        // 物品
        ["我的库存"] = "背包",
        ["分类库存"] = "背包",
        ["物品库存"] = "背包",
        ["角色库存"] = "背包",
        ["我的物品"] = "背包",
        ["库存搜索2"] = "库存搜索",
        ["分解物品"] = "分解",
        ["锻造配方"] = "锻造",
        ["锻造信息"] = "锻造 信息",
        ["取消锻造"] = "锻造 取消",
        ["模拟锻造"] = "锻造 模拟",
        ["确认开始锻造"] = "锻造",
        // 战斗
        ["查询boss"] = "查询Boss",
        ["讨伐boss"] = "Boss",
        ["小队讨伐boss"] = "小队讨伐Boss",
        ["金币排行榜"] = "排行榜 金币",
        ["钻石排行榜"] = "排行榜",
        // 玩法
        ["挑战金币秘境"] = "秘境 金币",
        ["挑战钻石秘境"] = "秘境 钻石",
        ["挑战经验秘境"] = "秘境 经验",
        ["挑战地区秘境"] = "秘境 地区",
        ["挑战突破秘境"] = "秘境 突破",
        ["挑战技能秘境"] = "秘境 技能",
        ["挑战魔法卡秘境"] = "秘境 魔法卡",
        // 社团
        ["社团任务列表"] = "社团任务",
        ["做社团任务"] = "社团任务 提交",
        ["社团任务结算"] = "社团任务 领取",
        ["社团商店查看"] = "社团商店",
        ["社团商店购买"] = "社团商店 买",
        ["社团商店出售"] = "社团商店 卖",
        ["社团市场出售"] = "社团市场 上架",
        ["社团市场下架"] = "社团市场 下架",
        ["社团市场查看"] = "社团市场",
        ["社团市场购买"] = "社团市场 买",
        ["社团市场清空"] = "社团市场 清空",
        // 商店 / 交易
        ["每日商店"] = "商店",
        ["商店购买"] = "购买",
        ["我的报价"] = "报价列表",
        ["取消报价"] = "撤回报价",
        ["市场出售"] = "上架",
        ["市场下架"] = "下架",
        ["市场查看"] = "市场",
        ["市场购买"] = "购买市场",
    };

    /// <summary>
    /// 旧命令 → 明确说明（不执行）。用于参数形态已经换掉、或正式不迁移的入口。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Redirects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // 参数形态变化：旧版一次传一串序号，新版是逐个操作
        ["设置小队"] = "新版按人操作：/小队 加入 @编号、/小队 移除 @位置（最多 4 人，用 /小队 查看编成）。",
        ["小队添加"] = "改用 /小队 加入 @编号。",
        ["小队移除"] = "改用 /小队 移除 @位置（位置号见 /小队）。",
        ["清空小队"] = "逐个移除：/小队 移除 1、/小队 移除 2……，或直接重新编成。",
        ["取消装备"] = "新版按槽位卸下：/卸下 weapon | armor | trinket | ……（槽位名见 /角色）。",
        ["强制分解"] = "新版不做「绕过保护」：先在 /背包 解锁，再 /分解 @编号。",

        // 正式不迁移（矩阵标 scope-excluded）
        ["加物"] = "旧版作用在角色随身背包上；新版没有角色级物品状态，改用 /背包 管理玩家库存。",
        ["减物"] = "同「加物」：新版没有角色随身背包，请用 /背包 与 /分解。",
        ["生命之泉"] = "新版角色没有持久 HP（每场战斗满状态进场），该功能不迁移。",
        ["酒馆"] = "新版没有持久 EP（EP 只存在于战斗内），该功能不迁移。",

    };

    /// <summary>把旧命令重写成新写法；没有对应项返回 false。</summary>
    public static bool TryRewrite(string text, out string rewritten)
    {
        rewritten = text;
        if (text.Length == 0) return false;

        int split = 0;
        while (split < text.Length && !char.IsWhiteSpace(text[split])) split++;
        string verb = text[..split];
        if (!Rewrites.TryGetValue(verb, out string? target)) return false;

        string rest = split < text.Length ? text[split..].Trim() : "";
        rewritten = rest.Length == 0 ? target : $"{target} {rest}";
        return true;
    }

    /// <summary>旧命令的停用说明；没有对应项返回 null。</summary>
    public static string? Explain(string text)
    {
        int split = text.IndexOf(' ');
        string verb = split < 0 ? text : text[..split];
        return Redirects.TryGetValue(verb, out string? message) ? message : null;
    }
}
