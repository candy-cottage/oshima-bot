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
}

public sealed class AdminService(
    IServiceScopeFactory scopeFactory,
    IRandomSource random,
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
            await ItemStore.AddAsync(db, who.Value.TargetId, entry.Name, tier, quantity, ct,
                entry.Id.ToString(CultureInfo.InvariantCulture));

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

    // ---------------- 内部 ----------------

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
