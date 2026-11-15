using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;
using FunGame.Core.Library.Constant;

namespace OshimaBot.Gameplay;

/// <summary>
/// 管理员的发放工具（旧版 <c>生成</c> / <c>生成指定魔法卡</c> 的对应物）。
/// <para>只做「往玩家背包/钱包里加东西」，不删不改——扣减属于封禁与回滚流程，另开。
/// 每条发放都会在回复里写明目标与结果，配合命令的 <c>audit: true</c> 留痕。</para>
/// </summary>
public interface IAdminService
{
    /// <summary>发放内容包里的物品；单次上限 <see cref="AdminService.MaxGrantPerCall"/> 件。</summary>
    Task<ActionResult> GrantItemAsync(long adminId, string? target, string item,
        int quantity, CancellationToken cancellationToken = default);

    /// <summary>发放金币或钻石。<b>不设上限</b>（用户 2026-11-14 决定），但仍要求金额为正且有限。</summary>
    Task<ActionResult> GrantCurrencyAsync(long adminId, string? target, string currency,
        double amount, CancellationToken cancellationToken = default);

    /// <summary>发放指定魔法卡：品质 + 魔法 + 可选三围；单次上限 <see cref="AdminService.MaxGrantPerCall"/> 张。</summary>
    Task<ActionResult> GrantMagicCardAsync(long adminId, string? target, string magic,
        int qualityTier, int str, int agi, int intelligence, int quantity,
        CancellationToken cancellationToken = default);

    /// <summary>最近的发放记录（管理员）。撤销要靠这里的记录号定位。</summary>
    Task<ActionResult> ListGrantsAsync(long adminId, string? target, int page,
        CancellationToken cancellationToken = default);

    /// <summary>撤销一笔发放：按记录号收回物品/魔法卡/货币，并标记该记录。</summary>
    Task<ActionResult> RevokeGrantAsync(long adminId, long grantId, string? note,
        CancellationToken cancellationToken = default);
}

public sealed class AdminService(
    IServiceScopeFactory scopeFactory,
    IRandomSource random,
    IGameClock clock,
    ILogger<AdminService> logger) : IAdminService
{
    /// <summary>
    /// 单次发放件数上限（物品与魔法卡）。旧版只校验「数量 &gt; 0」，一次发 99 万个也拦不住；
    /// 这里按运营实际用量定 100，超过就分批发。金币/钻石不受这条限制。
    /// </summary>
    public const int MaxGrantPerCall = 100;

    public Task<ActionResult> GrantItemAsync(long adminId, string? target, string item,
        int quantity, CancellationToken cancellationToken = default)
        => MutateAsync(adminId, async (db, ct) =>
        {
            if (quantity <= 0) return BadQuantity(quantity);
            if (quantity > MaxGrantPerCall)
            {
                return ActionResult.Fail($"单次最多发 {MaxGrantPerCall} 件", "没有发放任何物品",
                    $"你要发 {quantity} 件，分批来吧", "/发放物品");
            }

            string name = (item ?? "").Trim();
            if (name.Length == 0)
            {
                return ActionResult.Fail("没写要发什么", "没有发放任何物品",
                    "用法：/发放物品 <物品名|ID> [x数量] [给 <UID|昵称>]", "/发放物品");
            }

            FunGame.Core.Entity.Item? entry = FindItem(name);
            if (entry is null)
            {
                return ActionResult.Fail($"内容包里没有「{name}」", "没有发放任何物品",
                    "名字要跟 /查物品 显示的一致；魔法卡请用 /发放魔法卡", "/查物品");
            }
            if (entry.ItemType == ItemType.MagicCard)
            {
                return ActionResult.Fail("魔法卡要用专门指令", "没有发放任何物品",
                    $"用法：/发放魔法卡 {entry.Name} [品质] [三围 a b c] [x数量]", "/发放魔法卡");
            }

            (long TargetId, string TargetName)? who = await ResolveTargetAsync(db, adminId, target, ct);
            if (who is null) return NoTarget(target);

            int tier = GameplayRules.QualityTierOf(entry.QualityType);
            PlayerAccount account = await PlayerService.EnsureAsync(db, who.Value.TargetId, ct);
            string catalogId = entry.Id.ToString(CultureInfo.InvariantCulture);
            await ItemStore.AddAsync(db, who.Value.TargetId, entry.Name, tier, quantity, ct, catalogId);
            Record(db, adminId, who.Value.TargetId, GrantKinds.Item, entry.Name, quantity, 0, catalogId);

            return ActionResult.Success("🎁 物品已发放",
                $"{entry.Name} ×{quantity} → {who.Value.TargetName}",
                [
                    $"- 品质：{GameplayRules.QualityName(tier)}",
                    $"- 对方背包原有 {await BagSlotsAsync(db, who.Value.TargetId, ct)} 格",
                    "- 不会自动锁定：高品要防误分解，请对方自己 /锁定 @编号",
                    $"- 发放后余额未变（金币 {GameplayRules.Money(account.Credits)}　钻石 {GameplayRules.Money(account.Materials)}）"
                ], ["/查物品", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> GrantCurrencyAsync(long adminId, string? target, string currency,
        double amount, CancellationToken cancellationToken = default)
        => MutateAsync(adminId, async (db, ct) =>
        {
            string kind = (currency ?? "").Trim();
            string code = kind switch
            {
                "金币" or "金" or "credits" or "credit" => "credits",
                "钻石" or "钻" or "diamonds" or "diamond" => "diamonds",
                _ => ""
            };
            if (code.Length == 0)
            {
                return ActionResult.Fail("只支持金币与钻石", "没有发放任何货币",
                    "用法：/发放货币 <金币|钻石> <数量> [给 <UID|昵称>]", "/发放货币");
            }
            // 不设上限（2026-11-14 决定），但仍要挡住 NaN / 无穷 / 负数这类明显错误。
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0)
            {
                return ActionResult.Fail("金额不合法", "没有发放任何货币",
                    $"要发一个大于 0 的数，收到的是 {amount.ToString(CultureInfo.InvariantCulture)}", "/发放货币");
            }

            (long TargetId, string TargetName)? who = await ResolveTargetAsync(db, adminId, target, ct);
            if (who is null) return NoTarget(target);

            PlayerAccount account = await PlayerService.EnsureAsync(db, who.Value.TargetId, ct);
            double before = code == "diamonds" ? account.Materials : account.Credits;
            PlayerService.ApplyCurrency(db, account, amount, code, "admin_grant", "admin",
                adminId.ToString(CultureInfo.InvariantCulture));
            double after = code == "diamonds" ? account.Materials : account.Credits;
            string unit = code == "diamonds" ? "钻石" : "金币";
            Record(db, adminId, who.Value.TargetId, code, "", 0, amount, "");

            return ActionResult.Success($"💰 {unit}已发放",
                $"{GameplayRules.Money(amount)} {unit} → {who.Value.TargetName}",
                [
                    $"- 发放前：{GameplayRules.Money(before)}　发放后：{GameplayRules.Money(after)}",
                    $"- 已记入流水（reason=admin_grant，操作者 {adminId}），可对账",
                    amount > 1_000_000 ? "- 金额较大，确认无误再继续" : "- 金额已到账"
                ], ["/状态", "/发放货币"]);
        }, cancellationToken);

    public Task<ActionResult> GrantMagicCardAsync(long adminId, string? target, string magic,
        int qualityTier, int str, int agi, int intelligence, int quantity,
        CancellationToken cancellationToken = default)
        => MutateAsync(adminId, async (db, ct) =>
        {
            if (quantity <= 0) return BadQuantity(quantity);
            if (quantity > MaxGrantPerCall)
            {
                return ActionResult.Fail($"单次最多发 {MaxGrantPerCall} 张", "没有发放任何魔法卡",
                    $"你要发 {quantity} 张，分批来吧", "/发放魔法卡");
            }
            if (qualityTier is < 1 or > 7)
            {
                return ActionResult.Fail("品质档位要在 1–7 之间", "没有发放任何魔法卡",
                    "1 普通 / 2 优秀 / 3 稀有 / 4 史诗 / 5 传说 / 6 神话 / 7 不朽", "/发放魔法卡");
            }
            if (str < 0 || agi < 0 || intelligence < 0)
            {
                return ActionResult.Fail("三围不能是负数", "没有发放任何魔法卡",
                    "三围写 0 就表示随机分配", "/发放魔法卡");
            }

            FunGame.Core.Entity.Skill? skill = FindMagic(magic);
            if (skill is null)
            {
                return ActionResult.Fail($"内容包里没有叫「{magic}」的魔法", "没有发放任何魔法卡",
                    "名字要跟 /查技能 显示的一致，或直接写技能 ID", "/查技能");
            }

            (long TargetId, string TargetName)? who = await ResolveTargetAsync(db, adminId, target, ct);
            if (who is null) return NoTarget(target);

            await PlayerService.EnsureAsync(db, who.Value.TargetId, ct);
            QualityType quality = GameplayRules.QualityTypeOf(qualityTier);
            int statTotal = str + agi + intelligence;
            List<string> names = [];
            QualityType actual = quality;

            for (int i = 0; i < quantity; i++)
            {
                FunGame.Core.Entity.Item card = FunGameService.GenerateMagicCard(
                    GameplayRules.NewRandom(random), quality, skill.Id, str, agi, intelligence);
                if (i == 0) actual = card.QualityType;
                ItemStore.AddSnapshot(db, who.Value.TargetId, card, skill.Id.ToString(CultureInfo.InvariantCulture));
                names.Add(card.Name);
            }
            // 魔法卡每张名字不同，但同批卡的 CatalogId（技能 Id）相同 —— 撤销按它匹配。
            Record(db, adminId, who.Value.TargetId, GrantKinds.Magic, skill.Name, quantity, 0,
                skill.Id.ToString(CultureInfo.InvariantCulture));

            List<string> lines =
            [
                $"- 品质：{GameplayRules.QualityName(GameplayRules.QualityTierOf(actual))}",
                statTotal == 0
                    ? "- 三围：按该品质的区间随机分配（没指定）"
                    : $"- 三围：力量 {str}　敏捷 {agi}　智力 {intelligence}",
                $"- 卡名：{string.Join("、", names.Take(5))}{(names.Count > 5 ? " …" : "")}",
                "- 卡是独立的，不会并叠；用 /装备 打进卡包后才会进战斗"
            ];
            // 引擎规则：指定了三围时成品品质由总点数决定（≤6 普通 … >36 不朽），与旧版一致。
            if (actual != quality)
            {
                lines.Add($"- 注意：三围总量 {statTotal} 决定实际品质是「{GameplayRules.QualityName(GameplayRules.QualityTierOf(actual))}」，"
                    + $"不是你要的「{GameplayRules.QualityName(qualityTier)}」；要卡住品质就别写三围");
            }

            return ActionResult.Success("🃏 魔法卡已发放",
                $"{skill.Name} ×{quantity} → {who.Value.TargetName}", lines, ["/背包", "/发放魔法卡"]);
        }, cancellationToken);

    public Task<ActionResult> ListGrantsAsync(long adminId, string? target, int page,
        CancellationToken cancellationToken = default)
        => ReadAsync(adminId, async (db, ct) =>
        {
            const int pageSize = 20;
            int current = Math.Max(1, page);

            IQueryable<AdminGrant> query = db.AdminGrants.AsNoTracking();
            string scope = "全部玩家";
            if (!string.IsNullOrWhiteSpace(target))
            {
                (long TargetId, string TargetName)? who = await ResolveTargetAsync(db, adminId, target, ct);
                if (who is null) return NoTarget(target);
                query = query.Where(x => x.TargetUserId == who.Value.TargetId);
                scope = who.Value.TargetName;
            }

            int total = await query.CountAsync(ct);
            if (total == 0)
            {
                return ActionResult.Success("📋 还没有发放记录", scope,
                    ["- 发放物品 / 货币 / 魔法卡之后才会留下记录", "- 撤销要靠这里的记录号"], ["/发放物品", "/撤销发放"]);
            }

            int pages = (total + pageSize - 1) / pageSize;
            if (current > pages) current = pages;
            List<AdminGrant> rows = await query
                .OrderByDescending(x => x.Id)
                .Skip((current - 1) * pageSize).Take(pageSize)
                .ToListAsync(ct);

            Dictionary<long, string> displays = await DisplaysAsync(db, [.. rows.Select(x => x.TargetUserId)], ct);
            List<string> lines = [];
            foreach (AdminGrant row in rows)
            {
                string who = displays.GetValueOrDefault(row.TargetUserId, $"旅人{row.TargetUserId}");
                string what = GrantKinds.IsCurrency(row.Kind)
                    ? $"{GrantKinds.Display(row.Kind)} {GameplayRules.Money(row.Amount)}"
                    : $"{GrantKinds.Display(row.Kind)} {row.Name} ×{row.Quantity}";
                string state = row.RevokedAtUtc is DateTime revokedAt
                    ? $"（已撤销 {revokedAt.ToLocalTime():MM-dd HH:mm}）"
                    : "";
                lines.Add($"- #{row.Id}　{row.CreatedAtUtc.ToLocalTime():MM-dd HH:mm}　给 {who}：{what}{state}");
            }
            lines.Add($"- 第 {current}/{pages} 页，共 {total} 条；撤销用 /撤销发放 <记录号>");

            return ActionResult.Success($"📋 发放记录 · {scope}", $"第 {current}/{pages} 页（共 {total} 条）",
                lines, ["/撤销发放", "/发放记录"]);
        }, cancellationToken);

    public Task<ActionResult> RevokeGrantAsync(long adminId, long grantId, string? note,
        CancellationToken cancellationToken = default)
        => MutateAsync(adminId, async (db, ct) =>
        {
            if (grantId <= 0)
            {
                return ActionResult.Fail("记录号不合法", "没有收回任何东西",
                    "先 /发放记录 看编号，再 /撤销发放 <编号>", "/发放记录");
            }

            AdminGrant? grant = await db.AdminGrants.FirstOrDefaultAsync(x => x.Id == grantId, ct);
            if (grant is null)
            {
                return ActionResult.Fail($"没有 #{grantId} 这条发放记录", "没有收回任何东西",
                    "编号从 /发放记录 里取（按时间倒序）", "/发放记录");
            }
            if (grant.RevokedAtUtc is DateTime already)
            {
                return ActionResult.Fail($"#{grantId} 已经撤销过", "没有重复收回",
                    $"时间：{already.ToLocalTime():MM-dd HH:mm}，操作者 {grant.RevokedBy}；同一笔只能撤一次",
                    "/发放记录");
            }

            string whoName = await DisplayAsync(db, grant.TargetUserId, ct);
            string kindName = GrantKinds.Display(grant.Kind);
            List<string> lines = [$"- 记录：#{grant.Id}　{grant.CreatedAtUtc.ToLocalTime():MM-dd HH:mm} → {whoName}"];
            string outcome;

            if (GrantKinds.IsCurrency(grant.Kind))
            {
                PlayerAccount account = await PlayerService.EnsureAsync(db, grant.TargetUserId, ct);
                bool diamonds = grant.Kind == GrantKinds.Diamonds;
                double balance = diamonds ? account.Materials : account.Credits;
                double taken = Math.Min(grant.Amount, Math.Max(0, balance));
                if (taken <= 0)
                {
                    return ActionResult.Fail($"{whoName} 已经没剩多少{kindName}", "没有收回，记录仍待处理",
                        $"应收回 {GameplayRules.Money(grant.Amount)}，当前余额 {GameplayRules.Money(balance)}；"
                        + "钱已经花掉了，只能从余额里扣或手工处理", "/发放记录");
                }

                PlayerService.ApplyCurrency(db, account, -taken, grant.Kind, "admin_revoke", "admin_grant",
                    grant.Id.ToString(CultureInfo.InvariantCulture));
                double after = diamonds ? account.Materials : account.Credits;
                double shortfall = grant.Amount - taken;
                outcome = $"收回 {GameplayRules.Money(taken)} / {GameplayRules.Money(grant.Amount)} {kindName}";
                lines.Add($"- 余额：{GameplayRules.Money(balance)} → {GameplayRules.Money(after)}");
                if (shortfall > 0)
                {
                    lines.Add($"- 差 {GameplayRules.Money(shortfall)} {kindName} 已经花掉了，只收回能收的部分");
                }
            }
            else
            {
                List<PlayerItem> items = await ItemStore.ListAsync(db, grant.TargetUserId, ct);
                List<PlayerItem> matches = [.. items
                    .Where(x => grant.CatalogId.Length > 0
                        ? string.Equals(x.CatalogId, grant.CatalogId, StringComparison.Ordinal)
                        : string.Equals(x.Name, grant.Name, StringComparison.Ordinal))
                    .OrderByDescending(x => x.Id)];
                int available = matches.Sum(x => x.Quantity);
                int want = Math.Max(1, grant.Quantity);
                if (available <= 0)
                {
                    return ActionResult.Fail($"{whoName} 背包里已经没有「{grant.Name}」", "没有收回，记录仍待处理",
                        "已经用掉 / 装备 / 分解 / 卖掉的东西收不回来；需要的话用 /发放物品 反向调账", "/发放记录");
                }

                int taken = Math.Min(want, available);
                int remaining = taken;
                foreach (PlayerItem item in matches)
                {
                    if (remaining <= 0) break;
                    int take = Math.Min(remaining, item.Quantity);
                    if (item.Quantity <= take) ItemStore.Remove(db, item);
                    else { item.Quantity -= take; ItemStore.Sync(item); }
                    remaining -= take;
                }
                outcome = $"收回「{grant.Name}」×{taken} / {want}";
                lines.Add($"- 回收后背包里还有 {available - taken} 件同名同源物品");
                if (taken < want)
                {
                    lines.Add($"- 差 {want - taken} 件已不在背包（用掉 / 装备 / 分解 / 卖掉），收不回来");
                }
            }

            grant.RevokedAtUtc = clock.UtcNow;
            grant.RevokedBy = adminId;
            grant.RevokeNote = string.IsNullOrWhiteSpace(note) ? outcome : $"{outcome}；理由：{note.Trim()}";

            lines.Add($"- 记录 #{grant.Id} 已标记撤销（操作者 {adminId}）");
            if (!string.IsNullOrWhiteSpace(note)) lines.Add($"- 理由：{note.Trim()}");
            return ActionResult.Success($"↩️ 已撤销发放 #{grant.Id}", $"{outcome} ← {whoName}",
                lines, ["/发放记录", "/状态"]);
        }, cancellationToken);

    // ---------------- 内部 ----------------

    /// <summary>写一条发放记录（与发放本身同一个事务，不会出现「发了但没记」）。</summary>
    private void Record(BotDbContext db, long adminId, long targetUserId, string kind,
        string name, int quantity, double amount, string catalogId)
        => db.AdminGrants.Add(new AdminGrant
        {
            AdminId = adminId,
            TargetUserId = targetUserId,
            Kind = kind,
            Name = name,
            Quantity = quantity,
            Amount = amount,
            CatalogId = catalogId,
            CreatedAtUtc = clock.UtcNow
        });

    /// <summary>只读查询：不开事务，也不需要回滚。</summary>
    private async Task<ActionResult> ReadAsync(long adminId,
        Func<BotDbContext, CancellationToken, Task<ActionResult>> body, CancellationToken ct)
    {
        if (adminId <= 0) return PlayerService.NoIdentity;
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return await body(db, ct);
    }

    /// <summary>批量取显示名，列表里避免逐行查库。</summary>
    private static async Task<Dictionary<long, string>> DisplaysAsync(
        BotDbContext db, IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return [];
        List<User> users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToListAsync(ct);
        return users.ToDictionary(x => x.Id, Display);
    }

    private static ActionResult BadQuantity(int quantity)
        => ActionResult.Fail("数量不合法", "没有发放任何东西",
            $"要发正整数，收到的是 {quantity}", "/帮助");

    private static ActionResult NoTarget(string? target)
        => ActionResult.Fail("找不到这个玩家", "没有发放任何东西",
            $"用 UID / Handle / 昵称指定；你输入的是「{target}」", "/发放物品");

    /// <summary>品质档位：认「1–7」与中文品质名（普通/优秀/稀有/史诗/传说/神话/不朽）。</summary>
    public static bool TryParseQualityTier(string? token, out int tier)
    {
        tier = 0;
        string value = (token ?? "").Trim();
        if (value.Length == 0) return false;
        if (int.TryParse(value, out int number))
        {
            if (number is < 1 or > 7) return false;
            tier = number;
            return true;
        }

        for (int candidate = 1; candidate <= 7; candidate++)
        {
            if (!string.Equals(GameplayRules.QualityName(candidate), value, StringComparison.Ordinal)) continue;
            tier = candidate;
            return true;
        }
        return false;
    }

    private static FunGame.Core.Entity.Item? FindItem(string key)
    {
        FunGame.Core.Entity.Item? byName = FunGameService.AllItems
            .FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.Ordinal));
        if (byName is not null) return byName;
        return long.TryParse(key, out long id)
            ? FunGameService.AllItems.FirstOrDefault(x => x.Id == id)
            : null;
    }

    private static FunGame.Core.Entity.Skill? FindMagic(string key)
    {
        List<FunGame.Core.Entity.Skill> magics = [.. FunGameService.AllSkills.Where(x => x.IsMagic)];
        FunGame.Core.Entity.Skill? byName = magics
            .FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.Ordinal));
        if (byName is not null) return byName;
        return long.TryParse(key, out long id) ? magics.FirstOrDefault(x => x.Id == id) : null;
    }

    /// <summary>空目标 = 发给自己；否则按 UID / Handle / 昵称找。</summary>
    private static async Task<(long TargetId, string TargetName)?> ResolveTargetAsync(
        BotDbContext db, long adminId, string? target, CancellationToken ct)
    {
        string key = (target ?? "").Trim().TrimStart('@').Trim();
        if (key.Length == 0) return (adminId, await DisplayAsync(db, adminId, ct));

        if (long.TryParse(key, out long id) && id > 0)
        {
            if (await db.Users.AsNoTracking().AnyAsync(x => x.Id == id, ct)
                || await db.CorePlayers.AsNoTracking().AnyAsync(x => x.UserId == id, ct))
            {
                return (id, await DisplayAsync(db, id, ct));
            }
            return null;
        }

        User? byName = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Handle == key || x.Nickname == key, ct);
        return byName is null ? null : (byName.Id, Display(byName));
    }

    private static async Task<string> DisplayAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        User? user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        return user is null ? $"旅人{userId}" : Display(user);
    }

    private static string Display(User user)
        => string.IsNullOrWhiteSpace(user.Nickname) ? user.Handle : user.Nickname;

    private static Task<int> BagSlotsAsync(BotDbContext db, long userId, CancellationToken ct)
        => db.CoreItems.AsNoTracking().CountAsync(x => x.UserId == userId, ct);

    private async Task<ActionResult> MutateAsync(long adminId,
        Func<BotDbContext, CancellationToken, Task<ActionResult>> body, CancellationToken ct)
    {
        if (adminId <= 0) return PlayerService.NoIdentity;
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(ct);

            ActionResult result = await body(db, ct);
            if (!result.Ok)
            {
                await transaction.RollbackAsync(ct);
                return result;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "发放失败：Admin={AdminId}", adminId);
            return ActionResult.Fail("发放出现异常", "本次操作已回滚", "稍后重试或看日志", "/状态");
        }
    }
}
