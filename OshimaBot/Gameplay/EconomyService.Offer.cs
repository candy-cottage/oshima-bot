using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>
/// 报价单（旧版 <c>Offer</c> / <c>OfferState</c> 那一套）。
/// <para>与公开挂单（<c>/报价 @1 价格</c>）的区别：报价单是**定向**的——发起时就要指定接收方，
/// 双方各自挑选物品，发送后由接收方接受或拒绝。旧版这样设计是为了换物而不是卖钱。</para>
/// <para>状态：3 草稿 → 0 待接受 → 1 成交 / 2 撤回 / 4 拒绝。只复用 <c>Trades</c> + <c>TradeItems</c>，
/// 不加表：<c>TradeItem.FromUserId</c> 区分「我给的」与「要对方给的」。</para>
/// </summary>
public sealed partial class EconomyService
{
    /// <summary>草稿：只有发起人能看见和编辑，接收方此时还看不到。</summary>
    public const int OfferDraft = 3;

    /// <summary>待接受：发送之后的状态，也是 <c>/报价列表</c> 里出现的那批。</summary>
    public const int OfferSent = 0;

    public const int OfferCompleted = 1;
    public const int OfferWithdrawn = 2;
    public const int OfferRejected = 4;

    private const string OfferLineSeparator = "、";

    public static string OfferStatusName(int status) => status switch
    {
        OfferDraft => "草稿",
        OfferSent => "待接受",
        OfferCompleted => "已成交",
        OfferWithdrawn => "已撤回",
        OfferRejected => "已拒绝",
        _ => "未知"
    };

    public Task<OfferDetail?> GetOfferDetailAsync(long userId, long offerId,
        CancellationToken cancellationToken = default)
        => ReadAsync<OfferDetail?>(async (db, ct) =>
        {
            Trade? trade = await db.Trades.AsNoTracking().FirstOrDefaultAsync(x => x.Id == offerId, ct);
            if (trade is null) return null;
            bool mine = trade.SellerUserId == userId;
            bool target = trade.BuyerUserId == userId;
            // 草稿只有发起人能看；其它状态双方都能看。
            if (trade.Status == OfferDraft && !mine) return null;
            if (!mine && !target) return null;

            List<TradeItem> lines = await db.TradeItems.AsNoTracking()
                .Where(x => x.TradeId == trade.Id).OrderBy(x => x.Id).ToListAsync(ct);
            Dictionary<long, PlayerItem> items = await ItemStore.ByIdsAsync(
                db, [.. lines.Select(x => x.ItemInstanceId)], ct);

            List<OfferLine> Pick(long owner) =>
            [
                .. lines.Where(x => x.FromUserId == owner).Select((x, i) =>
                    new OfferLine(i + 1,
                        items.TryGetValue(x.ItemInstanceId, out PlayerItem? item)
                            ? PlayerService.ReadName(item) : "未知物品",
                        x.Quantity))
            ];

            return new OfferDetail(
                trade.Id, trade.Status, OfferStatusName(trade.Status),
                await DisplayOfAsync(db, trade.SellerUserId, ct),
                trade.BuyerUserId is { } buyer ? await DisplayOfAsync(db, buyer, ct) : "（公开挂单）",
                Pick(trade.SellerUserId),
                trade.BuyerUserId is { } b ? Pick(b) : [],
                mine, target);
        }, cancellationToken);

    public Task<ActionResult> CreateOfferDraftAsync(long userId, string target,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            (long TargetId, string Display)? found = await FindUserAsync(db, target, ct);
            if (found is null)
            {
                return ActionResult.Fail("找不到对方", "没有创建报价单",
                    $"用 UID、Handle 或昵称指定；你输入的是「{target}」", "/报价列表");
            }
            (long targetId, string display) = found.Value;
            if (targetId == userId)
            {
                return ActionResult.Fail("不能和自己报价", "没有创建报价单",
                    "换一个交易对象，或直接用 /报价 公开挂单", "/报价列表");
            }

            // 同一个人身上只允许有一份未完成的定向报价，避免自己把自己绕晕。
            bool busy = await db.Trades.AnyAsync(x =>
                x.SellerUserId == userId && x.BuyerUserId == targetId
                && (x.Status == OfferDraft || x.Status == OfferSent), ct);
            if (busy)
            {
                return ActionResult.Fail($"已经有一份给 {display} 的报价在进行中", "没有创建报价单",
                    "用 /查报价 编号 看进度，或先把它发送/撤回", "/报价列表");
            }

            Trade trade = new()
            {
                SellerUserId = userId,
                BuyerUserId = targetId,
                Status = OfferDraft,
                CreatedAtUtc = clock.UtcNow
            };
            db.Trades.Add(trade);
            await db.SaveChangesAsync(ct);

            return ActionResult.Success("📝 报价单已创建",
                $"报价 #{trade.Id}　接收方 {display}",
                [
                    "- 现在它是草稿，只有你能看到",
                    $"- 加自己的物品：/报价添加物品 {trade.Id} @1 @2",
                    $"- 加想要的物品：/报价添加对方物品 {trade.Id} @对方背包编号",
                    $"- 都加好后：/发送报价 {trade.Id}"
                ], [$"/报价添加物品 {trade.Id} @1", $"/发送报价 {trade.Id}", "/报价列表"]);
        }, cancellationToken);

    public Task<ActionResult> AddOfferItemsAsync(long userId, long offerId, bool opposite,
        IReadOnlyList<int> indexes, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            (Trade? trade, ActionResult? error) = await RequireDraftAsync(db, userId, offerId, ct);
            if (trade is null) return error!;

            long owner = opposite ? trade.BuyerUserId ?? 0 : userId;
            if (owner == 0)
            {
                return ActionResult.Fail("这不是定向报价", "没有添加任何物品",
                    "公开挂单没有「对方物品」这一侧", "/报价列表");
            }
            if (indexes.Count == 0)
            {
                return ActionResult.Fail("没有指定物品编号", "没有添加任何物品",
                    $"用法：/报价添加{(opposite ? "对方" : "")}物品 {offerId} @1 @2", "/背包");
            }

            List<PlayerItem> bag = await ItemStore.ListAsync(db, owner, ct);
            List<TradeItem> existing = await db.TradeItems.Where(x => x.TradeId == trade.Id).ToListAsync(ct);
            HashSet<long> used = [.. existing.Select(x => x.ItemInstanceId)];
            HashSet<long> inOtherOffers = await ItemsInOpenOffersAsync(db, ct);

            List<string> added = [];
            List<string> skipped = [];
            foreach (int index in indexes.Distinct())
            {
                if (index <= 0 || index > bag.Count)
                {
                    skipped.Add($"@{index} 不存在");
                    continue;
                }
                PlayerItem item = bag[index - 1];
                string name = PlayerService.ReadName(item);
                if (used.Contains(item.Id))
                {
                    skipped.Add($"{name} 已在报价里");
                    continue;
                }
                if (inOtherOffers.Contains(item.Id))
                {
                    skipped.Add($"{name} 正在其它报价中");
                    continue;
                }
                if (item.Locked || item.Bound)
                {
                    skipped.Add($"{name} 已锁定/绑定");
                    continue;
                }

                // 我方给出的物品先托管，避免挂单期间被卖掉或穿到身上；「要对方的」动不了，只能到时候校验。
                if (!opposite) item.UserId = GameplayRules.EscrowUserId;
                db.TradeItems.Add(new TradeItem
                {
                    TradeId = trade.Id, FromUserId = owner, ItemInstanceId = item.Id,
                    Quantity = 1, Price = 0
                });
                used.Add(item.Id);
                added.Add(name);
            }

            if (added.Count == 0)
            {
                return ActionResult.Fail("没有物品被加入", "报价单没有变化",
                    string.Join(OfferLineSeparator, skipped), $"/查报价 {offerId}");
            }

            List<string> lines =
            [
                $"- 已加入{(opposite ? "（要对方的）" : "")}：{string.Join(OfferLineSeparator, added)}",
                opposite ? "- 对方物品暂不托管，接受时才校验是否还在对方背包"
                         : "- 你的物品已移入托管，撤回或拒绝会退回"
            ];
            if (skipped.Count > 0) lines.Add($"- 跳过：{string.Join(OfferLineSeparator, skipped)}");
            return ActionResult.Success("➕ 已加入报价单", $"报价 #{offerId}", lines,
                [$"/查报价 {offerId}", $"/发送报价 {offerId}"]);
        }, cancellationToken);

    public Task<ActionResult> RemoveOfferItemsAsync(long userId, long offerId, bool opposite,
        IReadOnlyList<int> indexes, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            (Trade? trade, ActionResult? error) = await RequireDraftAsync(db, userId, offerId, ct);
            if (trade is null) return error!;
            if (indexes.Count == 0)
            {
                return ActionResult.Fail("没有指定物品编号", "报价单没有变化",
                    $"用法：/报价移除{(opposite ? "对方" : "")}物品 {offerId} @1　编号见 /查报价 {offerId}",
                    $"/查报价 {offerId}");
            }

            long owner = opposite ? trade.BuyerUserId ?? 0 : userId;
            List<TradeItem> lines = await db.TradeItems
                .Where(x => x.TradeId == trade.Id && x.FromUserId == owner)
                .OrderBy(x => x.Id).ToListAsync(ct);

            List<string> removed = [];
            foreach (int index in indexes.Distinct())
            {
                if (index <= 0 || index > lines.Count) continue;
                TradeItem line = lines[index - 1];
                PlayerItem? item = await ItemStore.ByIdAsync(db, line.ItemInstanceId, ct);
                // 我方物品是托管状态，退回给发起人；对方的本来就还在对方背包里。
                if (!opposite && item is not null) item.UserId = userId;
                db.TradeItems.Remove(line);
                removed.Add(item is null ? "未知物品" : PlayerService.ReadName(item));
            }

            if (removed.Count == 0)
            {
                return ActionResult.Fail("编号无效", "报价单没有变化",
                    $"用 /查报价 {offerId} 看「{(opposite ? "对方给的" : "我给的")}」那一栏的编号",
                    $"/查报价 {offerId}");
            }
            return ActionResult.Success("➖ 已移出报价单", $"报价 #{offerId}",
                [$"- 已移出：{string.Join(OfferLineSeparator, removed)}"],
                [$"/查报价 {offerId}", $"/发送报价 {offerId}"]);
        }, cancellationToken);

    public Task<ActionResult> SendOfferAsync(long userId, long offerId,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            (Trade? trade, ActionResult? error) = await RequireDraftAsync(db, userId, offerId, ct);
            if (trade is null) return error!;

            List<TradeItem> lines = await db.TradeItems
                .Where(x => x.TradeId == trade.Id).ToListAsync(ct);
            if (!lines.Any(x => x.FromUserId == trade.SellerUserId))
            {
                return ActionResult.Fail("你还没放任何东西", "报价单仍是草稿",
                    $"/报价添加物品 {offerId} @1", $"/查报价 {offerId}");
            }

            trade.Status = OfferSent;
            trade.CreatedAtUtc = clock.UtcNow;

            string target = await DisplayOfAsync(db, trade.BuyerUserId ?? 0, ct);
            return ActionResult.Success("📤 报价已发送", $"报价 #{offerId} → {target}",
                [
                    $"- 我给：{lines.Count(x => x.FromUserId == trade.SellerUserId)} 件",
                    $"- 要对方给：{lines.Count(x => x.FromUserId == trade.BuyerUserId)} 件",
                    $"- 对方用 /接受报价 {offerId} 或 /拒绝报价 {offerId} 处理"
                ], ["/报价列表", $"/查报价 {offerId}"]);
        }, cancellationToken);

    public Task<ActionResult> RejectOfferAsync(long userId, long offerId,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            Trade? trade = await db.Trades.FirstOrDefaultAsync(x => x.Id == offerId, ct);
            if (trade is null)
            {
                return ActionResult.Fail("找不到这份报价", "没有发生任何交易", "用 /报价列表 查看", "/报价列表");
            }
            if (trade.BuyerUserId != userId)
            {
                return ActionResult.Fail("这份报价不是发给你的", "没有发生任何交易",
                    trade.BuyerUserId is null ? "公开挂单没有「拒绝」，只能不买" : "只有接收方能拒绝", "/报价列表");
            }
            if (trade.Status != OfferSent)
            {
                return ActionResult.Fail("当前状态不能拒绝", "没有发生任何交易",
                    $"这份报价是「{OfferStatusName(trade.Status)}」", "/报价列表");
            }

            trade.Status = OfferRejected;
            trade.ClosedAtUtc = clock.UtcNow;
            int returned = await ReleaseOfferedItemsAsync(db, trade, ct);
            return ActionResult.Success("🚫 已拒绝报价", $"报价 #{offerId}",
                [$"- 对方托管中的 {returned} 件物品已退回", "- 双方都没有任何损失"],
                ["/报价列表", "/背包"]);
        }, cancellationToken);

    public Task<ActionResult> AcceptOfferAsync(long userId, int index,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            // 先按 /报价列表 的序号（旧行为）；序号越界时再按报价编号兜底（定向报价用 Trade.Id）。
            List<Trade> trades = await ActiveOffersAsync(db, ct);
            Trade? trade = index > 0 && index <= trades.Count ? trades[index - 1] : null;
            if (trade is null)
            {
                Trade? direct = await db.Trades.FirstOrDefaultAsync(x => x.Id == index, ct);
                if (direct is not null && direct.BuyerUserId == userId)
                {
                    return await AcceptDirectedOfferAsync(db, state, direct, ct);
                }
                return NoListing("报价单");
            }
            if (trade.BuyerUserId == userId)
            {
                return await AcceptDirectedOfferAsync(db, state, trade, ct);
            }
            if (trade.SellerUserId == userId)
            {
                return ActionResult.Fail("不能接受自己的报价单", "没有发生任何交易", "用 /撤回报价 @编号 取消", "/报价列表");
            }
            TradeItem? line = await db.TradeItems.FirstOrDefaultAsync(x => x.TradeId == trade.Id, ct);
            if (line is null) return ActionResult.Fail("报价单缺少物品", "没有发生任何交易", "联系管理员核查", "/报价列表");
            if (state.Credits < line.Price)
            {
                return ActionResult.Fail("金币不足", "没有发生任何交易",
                    $"需要 {line.Price} 金币，当前 {state.Credits}", "/状态");
            }
            PlayerItem? item = await ItemStore.ByIdAsync(db, line.ItemInstanceId, ct);
            if (item is null) return ActionResult.Fail("报价物品已丢失", "没有发生任何交易", "联系管理员核查", "/报价列表");

            PlayerAccount seller = await PlayerService.EnsureAsync(db, trade.SellerUserId, ct);
            string reference = trade.Id.ToString(CultureInfo.InvariantCulture);
            PlayerService.ApplyCurrency(db, state, -line.Price, "credits", "offer_buy", "trade", reference);
            PlayerService.ApplyCurrency(db, seller, line.Price, "credits", "offer_sale", "trade", reference);
            item.UserId = userId;
            trade.Status = OfferCompleted;
            trade.BuyerUserId = userId;
            trade.ClosedAtUtc = clock.UtcNow;
            return ActionResult.Success("🤝 报价成交",
                $"{PlayerService.ReadName(item)} · {line.Price} 金币",
                ["- 双方各写一条 ledger", "- 物品已放入买家背包"], ["/报价列表", "/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> CancelOfferAsync(long userId, int index,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            List<Trade> trades = await ActiveOffersAsync(db, ct);
            Trade? trade = index > 0 && index <= trades.Count ? trades[index - 1] : null;
            if (trade is null)
            {
                // 草稿不在 /报价列表 里，只能按编号撤回。
                Trade? draft = await db.Trades.FirstOrDefaultAsync(x => x.Id == index, ct);
                if (draft is null || draft.SellerUserId != userId)
                {
                    return NoListing("报价单");
                }
                return await WithdrawOfferAsync(db, draft, ct);
            }
            if (trade.SellerUserId != userId)
            {
                return ActionResult.Fail("这不是你的报价单", "没有撤回任何交易",
                    "只能撤回自己发布的报价", "/报价列表");
            }
            return await WithdrawOfferAsync(db, trade, ct);
        }, cancellationToken);

    // ---------------- 内部工具 ----------------

    /// <summary>定向报价的双边换物：我方托管物品给对方，对方物品给我方，不涉及金币。</summary>
    private async Task<ActionResult> AcceptDirectedOfferAsync(
        BotDbContext db, PlayerAccount state, Trade trade, CancellationToken ct)
    {
        if (trade.BuyerUserId != state.UserId)
        {
            return ActionResult.Fail("这份报价不是发给你的", "没有发生任何交易",
                "只有接收方能接受；发起方想改内容只能撤回重发", "/报价列表");
        }
        if (trade.Status != OfferSent)
        {
            return ActionResult.Fail("当前状态不能接受", "没有发生任何交易",
                $"这份报价是「{OfferStatusName(trade.Status)}」", "/报价列表");
        }

        List<TradeItem> lines = await db.TradeItems
            .Where(x => x.TradeId == trade.Id).OrderBy(x => x.Id).ToListAsync(ct);
        List<TradeItem> giving = [.. lines.Where(x => x.FromUserId == trade.SellerUserId)];
        List<TradeItem> wanting = [.. lines.Where(x => x.FromUserId == trade.BuyerUserId)];
        if (giving.Count == 0)
        {
            return ActionResult.Fail("报价单里没有对方的物品", "没有发生任何交易",
                "让对方补上物品再发送", "/报价列表");
        }

        // 对方（接收方）的物品此刻必须还在自己背包里、且未锁定，否则整笔作废。
        List<PlayerItem> bag = await ItemStore.ListAsync(db, state.UserId, ct);
        Dictionary<long, PlayerItem> bagById = bag.ToDictionary(x => x.Id);
        List<string> missing = [];
        foreach (TradeItem line in wanting)
        {
            if (!bagById.TryGetValue(line.ItemInstanceId, out PlayerItem? item) || item.Locked || item.Bound)
            {
                PlayerItem? any = await ItemStore.ByIdAsync(db, line.ItemInstanceId, ct);
                missing.Add(any is null ? "未知物品" : PlayerService.ReadName(any));
            }
        }
        if (missing.Count > 0)
        {
            return ActionResult.Fail("你有物品已经不在背包里了", "没有发生任何交易",
                $"{string.Join(OfferLineSeparator, missing)} 可能已被出售/装备/上架，让对方重新发一份报价",
                "/报价列表");
        }

        List<string> got = [];
        List<string> given = [];
        foreach (TradeItem line in giving)
        {
            PlayerItem? item = await ItemStore.ByIdAsync(db, line.ItemInstanceId, ct);
            if (item is null) continue;   // 托管中丢失：跳过，不阻塞整笔
            item.UserId = state.UserId;
            got.Add(PlayerService.ReadName(item));
        }
        foreach (TradeItem line in wanting)
        {
            PlayerItem item = bagById[line.ItemInstanceId];
            item.UserId = trade.SellerUserId;
            given.Add(PlayerService.ReadName(item));
        }

        trade.Status = OfferCompleted;
        trade.ClosedAtUtc = clock.UtcNow;

        List<string> detail =
        [
            $"- 你得到：{(got.Count == 0 ? "（无）" : string.Join(OfferLineSeparator, got))}",
            $"- 你给出：{(given.Count == 0 ? "（无）" : string.Join(OfferLineSeparator, given))}",
            "- 报价换物不涉及金币，双方都没有手续费"
        ];
        return ActionResult.Success("🤝 报价成交", $"报价 #{trade.Id}",
            detail, ["/背包", "/报价列表", "/状态"]);
    }

    /// <summary>撤回报价（草稿或待接受都行）：我方托管物品退回。</summary>
    private async Task<ActionResult> WithdrawOfferAsync(BotDbContext db, Trade trade, CancellationToken ct)
    {
        if (trade.Status is OfferCompleted or OfferRejected or OfferWithdrawn)
        {
            return ActionResult.Fail("这份报价已经结束", "没有撤回任何交易",
                $"状态是「{OfferStatusName(trade.Status)}」", "/报价列表");
        }
        int returned = await ReleaseOfferedItemsAsync(db, trade, ct);
        trade.Status = OfferWithdrawn;
        trade.ClosedAtUtc = clock.UtcNow;
        return ActionResult.Success("↩️ 报价已撤回", $"报价 #{trade.Id}",
            [$"- {returned} 件物品已退回背包"], ["/报价列表", "/背包"]);
    }

    /// <summary>把发起方托管中的物品全部退回；返回件数。</summary>
    private static async Task<int> ReleaseOfferedItemsAsync(BotDbContext db, Trade trade, CancellationToken ct)
    {
        List<TradeItem> lines = await db.TradeItems
            .Where(x => x.TradeId == trade.Id && x.FromUserId == trade.SellerUserId).ToListAsync(ct);
        int count = 0;
        foreach (TradeItem line in lines)
        {
            PlayerItem? item = await ItemStore.ByIdAsync(db, line.ItemInstanceId, ct);
            if (item is null) continue;
            item.UserId = trade.SellerUserId;
            count++;
        }
        return count;
    }

    /// <summary>草稿校验：存在、是发起人、状态还是草稿。</summary>
    private static async Task<(Trade? Trade, ActionResult? Error)> RequireDraftAsync(
        BotDbContext db, long userId, long offerId, CancellationToken ct)
    {
        Trade? trade = await db.Trades.FirstOrDefaultAsync(x => x.Id == offerId, ct);
        if (trade is null)
        {
            return (null, ActionResult.Fail("找不到这份报价", "报价单没有变化",
                "用 /报价列表 查看进行中的报价，草稿编号在创建时给过", "/报价列表"));
        }
        if (trade.SellerUserId != userId)
        {
            return (null, ActionResult.Fail("这不是你的报价单", "报价单没有变化",
                "只有发起人能编辑内容", "/报价列表"));
        }
        if (trade.Status != OfferDraft)
        {
            return (null, ActionResult.Fail("报价已发送，不能再改内容", "报价单没有变化",
                $"当前状态「{OfferStatusName(trade.Status)}」，要让对方看新内容得撤回重发", "/报价列表"));
        }
        return (trade, null);
    }

    /// <summary>已经在别的未完成报价里的物品（旧版 IsItemInOffers），避免一物多卖。</summary>
    private static async Task<HashSet<long>> ItemsInOpenOffersAsync(BotDbContext db, CancellationToken ct)
    {
        List<long> openTradeIds = await db.Trades
            .Where(x => x.Status == OfferDraft || x.Status == OfferSent)
            .Select(x => x.Id).ToListAsync(ct);
        if (openTradeIds.Count == 0) return [];
        return [.. await db.TradeItems
            .Where(x => openTradeIds.Contains(x.TradeId))
            .Select(x => x.ItemInstanceId).ToListAsync(ct)];
    }

    private static async Task<string> DisplayOfAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        if (userId <= 0) return "未知旅人";
        string? name = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => x.Nickname.Length > 0 ? x.Nickname : x.Handle)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(name) ? $"旅人{userId}" : name;
    }
}
