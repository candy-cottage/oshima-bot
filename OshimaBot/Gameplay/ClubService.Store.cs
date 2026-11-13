using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>
/// 社团商店：货币是社团基金（<see cref="Club.Funds"/>）。
/// <para>购买把基金换成成员个人物资；收购是管理用基金按分解价一半收成员的东西，
/// 两个方向都用 ledger 记账（<c>club_store_buy</c> / <c>club_store_sell</c>）。</para>
/// </summary>
public sealed partial class ClubService
{
    public Task<ClubStoreView> StoreAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<ClubStoreView>(async (db, ct) =>
        {
            ClubMember? member = await db.ClubMembers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (member is null) return new ClubStoreView(0, []);

            Club? club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == member.ClubId, ct);
            double funds = club?.Funds ?? 0;
            return new ClubStoreView(funds, [.. ClubRules.Goods.Select((good, i) =>
                new ClubStoreEntry(i + 1, good.Name, good.Price, good.Description, funds))]);
        }, cancellationToken);

    public Task<ActionResult> StoreBuyAsync(long userId, int index, int quantity,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            // 花的是公款，只有社长/管理能动，否则任何成员都能把基金掏空。
            (Club club, ClubMember _, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;

            if (index <= 0 || index > ClubRules.Goods.Count)
            {
                return ActionResult.Fail($"商品 @{index} 不存在", "没有扣基金，也没有发放物品",
                    "用 /社团商店 查看货架", "/社团商店");
            }
            ClubStoreGoodSpec good = ClubRules.Goods[index - 1];
            int amount = Math.Clamp(quantity, 1, 99);
            long cost = good.Price * amount;
            if (club.Funds < cost)
            {
                return ActionResult.Fail("社团基金不足", "没有扣基金，也没有发放物品",
                    $"需要 {GameplayRules.Money(cost)} 基金，当前 {GameplayRules.Money(club.Funds)}；让成员 /社团捐献 补一点", "/社团捐献");
            }

            club.Funds -= cost;
            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            string reference = club.Id.ToString(CultureInfo.InvariantCulture);

            if (ClubRules.IsCurrencyGood(good))
            {
                string currency = good.CatalogId["currency:".Length..];
                PlayerService.ApplyCurrency(db, account, amount, currency, "club_store_buy", "club", reference);
            }
            else
            {
                await PlayerService.AddItemAsync(db, userId, good.Name, 1, amount, ct,
                    string.IsNullOrWhiteSpace(good.CatalogId) ? "club" : good.CatalogId);
            }
            account.UpdatedAtUtc = clock.UtcNow;

            return ActionResult.Success("🛍️ 兑换成功",
                $"{good.Name} ×{amount} · 花费 {GameplayRules.Money(cost)} 社团基金",
                [
                    $"- 社团基金：{GameplayRules.Money(club.Funds)}",
                    $"- 你的钱包：金币 {GameplayRules.Money(account.Credits)}　钻石 {GameplayRules.Money(account.Materials)}",
                    "- 基金是公会公款，花费请和成员说一声"
                ], ["/社团商店", "/背包", "/我的社团"]);
        }, cancellationToken);

    public Task<ActionResult> StoreSellAsync(long userId, int itemIndex, int quantity,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember _, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;

            PlayerItem? item = await PlayerService.ItemByIndexAsync(db, userId, itemIndex, ct);
            if (item is null)
            {
                return ActionResult.Fail($"背包里没有 @{itemIndex}", "没有收购任何物品",
                    "用 /背包 查看编号", "/背包");
            }
            if (item.Locked || item.Bound)
            {
                return ActionResult.Fail("该物品已锁定或绑定", "没有收购任何物品",
                    "先 /解锁 @编号", "/背包");
            }

            int amount = Math.Clamp(quantity, 1, item.Quantity);
            long payout = ClubRules.BuybackCredits(item.Quality, amount);
            if (club.Funds < payout)
            {
                return ActionResult.Fail("社团基金不足", "没有收购任何物品",
                    $"这件要付 {GameplayRules.Money(payout)} 金币，基金只有 {GameplayRules.Money(club.Funds)}", "/社团捐献");
            }

            string name = PlayerService.ReadName(item);
            int quality = item.Quality;
            PlayerService.Consume(db, item, amount);

            club.Funds -= payout;
            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            PlayerService.ApplyCurrency(db, account, payout, "credits", "club_store_sell", "club",
                club.Id.ToString(CultureInfo.InvariantCulture));
            account.UpdatedAtUtc = clock.UtcNow;

            return ActionResult.Success("📥 社团已收购",
                $"{name} ×{amount} · 你获得 {GameplayRules.Money(payout)} 金币",
                [
                    $"- 收购价按{GameplayRules.QualityName(quality)}分解价的一半折算",
                    $"- 社团基金：{GameplayRules.Money(club.Funds)}",
                    $"- 当前金币：{GameplayRules.Money(account.Credits)}"
                ], ["/社团商店", "/背包", "/社团任务"]);
        }, cancellationToken);
}
