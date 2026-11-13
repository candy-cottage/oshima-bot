using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>社团市场挂单状态。</summary>
public static class ClubListingStatus
{
    public const int Active = 0;
    public const int Sold = 1;
    public const int Delisted = 2;

    /// <summary>社团解散时统一退回。</summary>
    public const int ClubDisbanded = 3;

    /// <summary>卖家退团 / 被踢时自动退回。</summary>
    public const int SellerLeft = 4;
}

/// <summary>
/// 社团市场：只对本社团成员可见的二手交易，货币是金币，手续费（5%）进社团基金。
/// <para>和全局市场的区别是「按社团隔离 + 手续费回流」；物品在成交前同样托管给
/// <see cref="GameplayRules.EscrowUserId"/>。</para>
/// </summary>
public sealed partial class ClubService
{
    public Task<IReadOnlyList<ClubMarketEntry>> MarketAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<ClubMarketEntry>>(async (db, ct) =>
        {
            ClubMember? member = await db.ClubMembers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (member is null) return [];

            List<ClubMarketListing> listings = await db.ClubMarketListings.AsNoTracking()
                .Where(x => x.ClubId == member.ClubId && x.Status == ClubListingStatus.Active)
                .OrderBy(x => x.Id).ToListAsync(ct);
            if (listings.Count == 0) return [];

            Dictionary<long, PlayerItem> items = await ItemStore.ByIdsAsync(
                db, [.. listings.Select(x => x.ItemInstanceId)], ct);
            List<long> sellers = [.. listings.Select(x => x.SellerUserId).Distinct()];
            Dictionary<long, string> names = await DisplaysAsync(db, sellers, ct);

            return [.. listings.Select((listing, i) =>
            {
                items.TryGetValue(listing.ItemInstanceId, out PlayerItem? item);
                return new ClubMarketEntry(
                    i + 1, listing.Id,
                    item is null ? "未知物品" : PlayerService.ReadName(item),
                    item?.Quality ?? 1, listing.Price,
                    names.GetValueOrDefault(listing.SellerUserId, $"旅人{listing.SellerUserId}"),
                    listing.SellerUserId == userId);
            })];
        }, cancellationToken);

    public Task<ActionResult> MarketListAsync(long userId, int itemIndex, long price,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember _, ActionResult? denied) = await RequireMemberAsync(db, userId, ct);
            if (denied is not null) return denied;

            if (price is < ClubRules.MinPrice or > ClubRules.MaxPrice)
            {
                return ActionResult.Fail("价格超出允许范围", "没有上架任何物品",
                    $"价格需在 {ClubRules.MinPrice} ~ {ClubRules.MaxPrice} 之间", "/社团市场");
            }

            int mine = await db.ClubMarketListings.CountAsync(
                x => x.ClubId == club.Id && x.SellerUserId == userId && x.Status == ClubListingStatus.Active, ct);
            if (mine >= ClubRules.MaxListingsPerUser)
            {
                return ActionResult.Fail("你的挂单已满", "没有上架任何物品",
                    $"最多同时挂 {ClubRules.MaxListingsPerUser} 件，先 /社团市场 下架 @编号", "/社团市场");
            }
            int total = await db.ClubMarketListings.CountAsync(
                x => x.ClubId == club.Id && x.Status == ClubListingStatus.Active, ct);
            if (total >= ClubRules.MaxListingsPerClub)
            {
                return ActionResult.Fail("社团市场已满", "没有上架任何物品",
                    $"本社团在售已达 {ClubRules.MaxListingsPerClub} 件，等别人买走或 /社团市场 清空", "/社团市场");
            }

            PlayerItem? item = await PlayerService.ItemByIndexAsync(db, userId, itemIndex, ct);
            if (item is null)
            {
                return ActionResult.Fail($"背包里没有 @{itemIndex}", "没有上架任何物品",
                    "用 /背包 查看编号", "/背包");
            }
            if (item.Locked || item.Bound)
            {
                return ActionResult.Fail("该物品已锁定或绑定", "没有上架任何物品",
                    "先 /解锁 @编号，已装备物品需先 /卸下", "/背包");
            }

            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            double fee = ClubRules.MarketFee(price);
            if (account.Credits < fee)
            {
                return ActionResult.Fail("金币不足以支付上架费", "没有上架任何物品",
                    $"需要 {GameplayRules.Money(fee)} 金币，当前 {GameplayRules.Money(account.Credits)}", "/状态");
            }

            PlayerService.ApplyCurrency(db, account, -fee, "credits", "club_market_fee", "club",
                club.Id.ToString(CultureInfo.InvariantCulture));
            account.UpdatedAtUtc = clock.UtcNow;
            club.Funds += fee;                       // 与全局市场不同：手续费留在社团里

            string name = PlayerService.ReadName(item);
            item.UserId = GameplayRules.EscrowUserId;
            db.ClubMarketListings.Add(new ClubMarketListing
            {
                ClubId = club.Id, SellerUserId = userId, ItemInstanceId = item.Id,
                Price = price, Fee = fee, Status = ClubListingStatus.Active, CreatedAtUtc = clock.UtcNow
            });

            return ActionResult.Success("🏷️ 已上架到社团市场",
                $"{name} · 标价 {GameplayRules.Money(price)} 金币",
                [
                    $"- 上架费 {GameplayRules.Money(fee)} 金币（{ClubRules.MarketFeeRatePercent}%）进入社团基金",
                    $"- 社团基金：{GameplayRules.Money(club.Funds)}",
                    "- 物品已托管，成交前不在背包里；用 /社团市场 查看编号"
                ], ["/社团市场", "/背包", "/我的社团"]);
        }, cancellationToken);

    public Task<ActionResult> MarketDelistAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember _, ActionResult? denied) = await RequireMemberAsync(db, userId, ct);
            if (denied is not null) return denied;

            List<ClubMarketListing> listings = await ActiveListingsAsync(db, club.Id, ct);
            if (index <= 0 || index > listings.Count)
            {
                return ActionResult.Fail($"挂单 @{index} 不存在", "没有下架任何物品",
                    "用 /社团市场 查看编号", "/社团市场");
            }
            ClubMarketListing listing = listings[index - 1];
            if (listing.SellerUserId != userId)
            {
                return ActionResult.Fail("这不是你的挂单", "没有下架任何物品",
                    "只能下架自己上架的商品", "/社团市场");
            }

            PlayerItem? item = await ItemStore.ByIdAsync(db, listing.ItemInstanceId, ct);
            listing.Status = ClubListingStatus.Delisted;
            listing.ClosedAtUtc = clock.UtcNow;
            if (item is not null) item.UserId = userId;

            return ActionResult.Success("📦 已下架",
                $"{(item is null ? "未知物品" : PlayerService.ReadName(item))} 已退回背包",
                ["- 上架费不予退还"], ["/社团市场", "/背包"]);
        }, cancellationToken);

    public Task<ActionResult> MarketBuyAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember _, ActionResult? denied) = await RequireMemberAsync(db, userId, ct);
            if (denied is not null) return denied;

            List<ClubMarketListing> listings = await ActiveListingsAsync(db, club.Id, ct);
            if (index <= 0 || index > listings.Count)
            {
                return ActionResult.Fail($"挂单 @{index} 不存在", "没有发生任何交易",
                    "用 /社团市场 查看编号", "/社团市场");
            }
            ClubMarketListing listing = listings[index - 1];
            if (listing.SellerUserId == userId)
            {
                return ActionResult.Fail("不能买自己的挂单", "没有发生任何交易",
                    "用 /社团市场 下架 @编号 撤回", "/社团市场");
            }

            PlayerAccount buyer = await PlayerService.EnsureAsync(db, userId, ct);
            if (buyer.Credits < listing.Price)
            {
                return ActionResult.Fail("金币不足", "没有发生任何交易",
                    $"需要 {GameplayRules.Money(listing.Price)} 金币，当前 {GameplayRules.Money(buyer.Credits)}", "/状态");
            }
            PlayerItem? item = await ItemStore.ByIdAsync(db, listing.ItemInstanceId, ct);
            if (item is null)
            {
                return ActionResult.Fail("挂单物品已丢失", "没有发生任何交易", "联系管理员核查", "/社团市场");
            }

            PlayerAccount seller = await PlayerService.EnsureAsync(db, listing.SellerUserId, ct);
            string reference = listing.Id.ToString(CultureInfo.InvariantCulture);
            PlayerService.ApplyCurrency(db, buyer, -listing.Price, "credits", "club_market_buy", "club_listing", reference);
            PlayerService.ApplyCurrency(db, seller, listing.Price, "credits", "club_market_sale", "club_listing", reference);
            buyer.UpdatedAtUtc = clock.UtcNow;
            seller.UpdatedAtUtc = clock.UtcNow;

            item.UserId = userId;
            listing.Status = ClubListingStatus.Sold;
            listing.ClosedAtUtc = clock.UtcNow;

            return ActionResult.Success("🤝 交易达成",
                $"{PlayerService.ReadName(item)} · {GameplayRules.Money(listing.Price)} 金币",
                [
                    $"- 你支出 {GameplayRules.Money(listing.Price)} 金币，对方收入 {GameplayRules.Money(listing.Price)}",
                    "- 双方各写一条 ledger，金额可对账",
                    "- 物品已放入你的背包"
                ], ["/社团市场", "/背包", "/状态"]);
        }, cancellationToken);

    /// <summary>社长清空市场：所有在售挂单退回卖家背包（旧入口只对社长开放）。</summary>
    public Task<ActionResult> MarketClearAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember mine, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;
            if (mine.Role != ClubRole.Master)
            {
                return ActionResult.Fail("只有社长可以清空市场", "没有清空任何挂单", "让社长来操作", "/社团市场");
            }

            List<ClubMarketListing> listings = await ActiveListingsAsync(db, club.Id, ct);
            if (listings.Count == 0)
            {
                return ActionResult.Fail("社团市场已经是空的", "没有清空任何挂单", "等成员上架", "/社团市场");
            }
            await ReleaseListingRowsAsync(db, listings, ClubListingStatus.ClubDisbanded, ct);

            return ActionResult.Success("🧹 社团市场已清空",
                $"{listings.Count} 件挂单退回各自卖家",
                ["- 上架费不退还", "- 物品已回到卖家背包"], ["/社团市场", "/我的社团"]);
        }, cancellationToken);

    // ---------------- 内部 ----------------

    private static async Task<List<ClubMarketListing>> ActiveListingsAsync(
        BotDbContext db, long clubId, CancellationToken ct)
        => await db.ClubMarketListings
            .Where(x => x.ClubId == clubId && x.Status == ClubListingStatus.Active)
            .OrderBy(x => x.Id).ToListAsync(ct);

    /// <summary>把某个卖家在本社团的在售挂单全部退回（退团 / 被踢时调用）。</summary>
    private async Task ReleaseListingsAsync(
        BotDbContext db, long clubId, long userId, int status, CancellationToken ct)
    {
        List<ClubMarketListing> listings = await db.ClubMarketListings
            .Where(x => x.ClubId == clubId && x.SellerUserId == userId && x.Status == ClubListingStatus.Active)
            .ToListAsync(ct);
        await ReleaseListingRowsAsync(db, listings, status, ct);
    }

    /// <summary>按状态关闭一批挂单并把物品还给卖家；不调用 SaveChanges。</summary>
    private async Task ReleaseListingRowsAsync(
        BotDbContext db, IReadOnlyCollection<ClubMarketListing> listings, int status, CancellationToken ct)
    {
        if (listings.Count == 0) return;
        Dictionary<long, PlayerItem> items = await ItemStore.ByIdsAsync(
            db, [.. listings.Select(x => x.ItemInstanceId)], ct);
        foreach (ClubMarketListing listing in listings)
        {
            listing.Status = status;
            listing.ClosedAtUtc = clock.UtcNow;
            if (items.TryGetValue(listing.ItemInstanceId, out PlayerItem? item)) item.UserId = listing.SellerUserId;
        }
    }
}
