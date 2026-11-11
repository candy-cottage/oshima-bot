using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Combat;
using OshimaBot.Game.Services;
using CoreCharacter = FunGame.Core.Entity.Character;
using CoreTeam = FunGame.Core.Entity.Team;

namespace OshimaBot.Gameplay;

public interface IEconomyService
{
    /// <param name="storeKey">商店 key；为空时用玩家上次查看的商店（默认每日商店）。</param>
    Task<IReadOnlyList<StoreEntry>> GetStoreAsync(long userId, string? storeKey = null, CancellationToken cancellationToken = default);
    Task<ActionResult> BuyAsync(long userId, string? storeKey, int index, int quantity, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketEntry>> GetMarketAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> ListOnMarketAsync(long userId, int itemIndex, long price, CancellationToken cancellationToken = default);
    Task<ActionResult> DelistAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> BuyMarketAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OfferEntry>> GetOffersAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> CreateOfferAsync(long userId, int itemIndex, long price, CancellationToken cancellationToken = default);
    Task<ActionResult> AcceptOfferAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> CancelOfferAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> ForgeAsync(long userId, int materialIndex, int minutes, CancellationToken cancellationToken = default);
    Task<ActionResult> ClaimForgeAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> InstanceAsync(long userId, string difficulty, CancellationToken cancellationToken = default);
    Task<ActionResult> ClaimInstanceAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> DuelAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> BossAsync(long userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RankEntry>> GetRankingAsync(string board, CancellationToken cancellationToken = default);
}

/// <summary>
/// P2 经济与战斗仓储。所有货币变动（含跨用户买卖）都在同一个 SQLite 事务内完成，
/// 买卖双方各自写一条 ledger；市场上架/报价的物品托管到 EscrowUserId，不出现在任何玩家背包。
/// </summary>
public sealed class EconomyService(
    IServiceScopeFactory scopeFactory,
    ILogger<EconomyService> logger,
    IGameClock clock,
    IRandomSource random) : IEconomyService
{
    private const string StoreKey = "system";
    private const string PointsKey = "p2_points";

    private async Task<T> ReadAsync<T>(
        Func<BotDbContext, CancellationToken, Task<T>> body, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return await body(db, cancellationToken);
    }

    private async Task<ActionResult> MutateAsync(
        long userId,
        Func<BotDbContext, PlayerAccount, CancellationToken, Task<ActionResult>> body,
        CancellationToken cancellationToken)
    {
        if (userId <= 0) return PlayerService.NoIdentity;
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);

            PlayerAccount state = await PlayerService.EnsureAsync(db, userId, cancellationToken);
            ActionResult result = await body(db, state, cancellationToken);
            if (!result.Ok)
            {
                await transaction.RollbackAsync(cancellationToken);
                return result;
            }
            state.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActionResult.Fail("存档正被另一条消息修改", "本次操作已回滚", "稍等一秒后重试", "/状态");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "P2 经济操作失败：UserId={UserId}", userId);
            return ActionResult.Fail("操作出现异常", "本次操作已回滚", "稍后重试", "/状态");
        }
    }

    // ---------------- 商店 ----------------

    /// <summary>
    /// 取商店货架（旧版 <c>CheckDailyStore</c> / <c>Players.GetStore</c>）：
    /// 常规与积分商店是全局固定货架；每日商店每个用户一份，4 件随机、4:00 刷新。
    /// </summary>
    private async Task<List<StoreGood>> EnsureStoreAsync(
        BotDbContext db, string storeKey, long userId, CancellationToken ct)
    {
        if (string.Equals(storeKey, StoreCatalog.DailyKey, StringComparison.OrdinalIgnoreCase))
        {
            return await EnsureDailyStoreAsync(db, userId, ct);
        }

        StoreSpec? spec = StoreCatalog.Find(storeKey);
        if (spec is null) return [];

        if (!await db.Stores.AnyAsync(x => x.Key == spec.Key, ct))
        {
            db.Stores.Add(new Store
            {
                Key = spec.Key, Name = spec.Name, Kind = spec.Kind.ToString(), ConfigJson = "{}"
            });
            await db.SaveChangesAsync(ct);
        }

        List<StoreGood> goods = await db.StoreGoods
            .Where(x => x.StoreKey == spec.Key && x.OwnerUserId == 0)
            .OrderBy(x => x.Slot).ThenBy(x => x.Id).ToListAsync(ct);
        if (goods.Count > 0) return goods;

        int slot = 0;
        foreach (StoreGoodSpec item in spec.Goods)
        {
            db.StoreGoods.Add(new StoreGood
            {
                StoreKey = spec.Key, Slot = ++slot, OwnerUserId = 0,
                Name = item.Name, ItemCatalogId = item.CatalogId,
                PriceCurrency = StoreCatalog.LedgerCurrency(item.Currency),
                Price = item.Price, Stock = item.Stock, Quota = item.Quota,
                SoldCount = 0, UsersBuyCountJson = "{}"
            });
        }
        await db.SaveChangesAsync(ct);
        return await db.StoreGoods
            .Where(x => x.StoreKey == spec.Key && x.OwnerUserId == 0)
            .OrderBy(x => x.Slot).ThenBy(x => x.Id).ToListAsync(ct);
    }

    private async Task<List<StoreGood>> EnsureDailyStoreAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        string storeDay = GameplayRules.StoreDay(clock.UtcNow);
        string storeKey = $"{StoreCatalog.DailyKey}:{userId}";

        if (!await db.Stores.AnyAsync(x => x.Key == storeKey, ct))
        {
            db.Stores.Add(new Store
            {
                Key = storeKey, Name = "每日商店", Kind = nameof(StoreKind.Daily),
                RefreshAtUtc = clock.UtcNow, ConfigJson = "{}"
            });
            await db.SaveChangesAsync(ct);
        }

        Store store = await db.Stores.FirstAsync(x => x.Key == storeKey, ct);
        List<StoreGood> goods = await db.StoreGoods
            .Where(x => x.StoreKey == storeKey).OrderBy(x => x.Slot).ToListAsync(ct);

        // 用「商店日」判断是否该刷新：跨过本地 4:00 就重开一摊。
        bool stale = goods.Count == 0
            || goods.Any(x => !string.Equals(x.RefreshAtUtc?.ToString("yyyy-MM-dd"), storeDay, StringComparison.Ordinal));
        if (!stale) return goods;

        db.StoreGoods.RemoveRange(goods);
        await db.SaveChangesAsync(ct);

        goods = GenerateDailyGoods(storeKey, storeDay);
        db.StoreGoods.AddRange(goods);
        store.RefreshAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return goods;
    }

    /// <summary>
    /// 旧版每日商店生成：4 件商品，1/3 概率是魔法卡，其余从抽卡池随机；
    /// 价格按品质区间随机，库存 1–2（魔法卡 ×0.7 且库存 1，消耗品按品阶换 formula 并 +3）。
    /// </summary>
    private List<StoreGood> GenerateDailyGoods(string storeKey, string storeDay)
    {
        List<FunGame.Core.Entity.Item> drawPool = FunGameService.AllItems
            .Where(GameplayRules.IsDrawable).ToList();
        List<FunGame.Core.Entity.Item> magicCards = FunGameService.AllItems
            .Where(i => i.ItemType == FunGame.Core.Library.Constant.ItemType.MagicCard).ToList();

        List<StoreGood> goods = [];
        DateTime refreshAt = DateTime.ParseExact(storeDay, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        for (int slot = 1; slot <= StoreCatalog.DailyGoodsCount; slot++)
        {
            bool wantMagicCard = random.Next(3) < 1 && magicCards.Count > 0;
            List<FunGame.Core.Entity.Item> pool = wantMagicCard ? magicCards : drawPool;
            if (pool.Count == 0) continue;

            FunGame.Core.Entity.Item pick = pool[random.Next(pool.Count)];
            (int min, int max) = StoreCatalog.PriceRange(pick.QualityType);
            long price = random.Next(min, Math.Max(min + 1, max));
            int stock = random.Next(1, 3);                       // 1–2

            if (pick.ItemType == FunGame.Core.Library.Constant.ItemType.MagicCard)
            {
                price = (long)(price * 0.7);
                stock = 1;
            }
            else if (pick.ItemType == FunGame.Core.Library.Constant.ItemType.Consumable)
            {
                int previous = (int)pick.QualityType - 1;
                int current = (int)pick.QualityType;
                int low = 300 * (1 + previous * previous - previous);
                int high = 300 * (1 + current * current - current);
                price = random.Next(low, Math.Max(low + 1, high));
                stock += 3;
            }
            if (price <= 0)
            {
                int tier = (int)pick.QualityType;
                price = (long)((random.NextDouble() + 0.1)
                    * random.Next(1_000, 5_000)
                    * random.Next(tier + 2, 6 + tier));
            }

            goods.Add(new StoreGood
            {
                StoreKey = storeKey, Slot = slot, OwnerUserId = 0,
                Name = pick.Name,
                ItemCatalogId = pick.Id.ToString(CultureInfo.InvariantCulture),
                PriceCurrency = "credits", Price = price, Stock = stock, Quota = 0,
                SoldCount = 0, UsersBuyCountJson = "{}",
                RefreshAtUtc = refreshAt
            });
        }
        return goods;
    }

    public Task<IReadOnlyList<StoreEntry>> GetStoreAsync(long userId, string? storeKey = null,
        CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<StoreEntry>>(async (db, ct) =>
        {
            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            string key = ResolveStoreKey(storeKey, account);
            List<StoreGood> goods = await EnsureStoreAsync(db, key, userId, ct);
            if (goods.Count == 0) return [];

            // 记住「上次看的店」，/购买 不带参数时就用它（旧版 UserLastVisitStore）。
            if (!string.Equals(account.Wallet.LastStoreKey, key, StringComparison.Ordinal))
            {
                account.Wallet.LastStoreKey = key;
                await db.SaveChangesAsync(ct);
            }

            return [.. goods.Select((good, i) => new StoreEntry(
                i + 1, good.Id, good.Name,
                StoreCatalog.CurrencyName(ParseCurrency(good.PriceCurrency)),
                good.Price,
                good.Stock < 0 ? -1 : Math.Max(0, good.Stock - good.SoldCount),
                good.Quota, ReadBoughtCount(good, userId)))];
        }, cancellationToken);

    public Task<ActionResult> BuyAsync(long userId, string? storeKey, int index, int quantity,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            string key = ResolveStoreKey(storeKey, state);
            List<StoreGood> goods = await EnsureStoreAsync(db, key, userId, ct);
            if (index <= 0 || index > goods.Count) return NoListing("商品");

            StoreGood good = goods[index - 1];
            int amount = Math.Clamp(quantity, 1, 99);
            int remaining = good.Stock < 0 ? -1 : good.Stock - good.SoldCount;
            if (remaining >= 0 && remaining < amount)
            {
                return ActionResult.Fail("库存不足", "没有扣款，也没有发放物品",
                    $"{good.Name} 只剩 {remaining} 件，想买 {amount} 件", "/商店");
            }
            int bought = ReadBoughtCount(good, userId);
            if (good.Quota > 0 && bought + amount > good.Quota)
            {
                return ActionResult.Fail("超出限购", "没有扣款，也没有发放物品",
                    $"{good.Name} 每人限购 {good.Quota} 件，你已买 {bought} 件", "/商店");
            }

            StoreCurrency currency = ParseCurrency(good.PriceCurrency);
            long cost = good.Price * amount;
            if (currency != StoreCurrency.Free)
            {
                double balance = StoreCatalog.GetBalance(currency, state);
                if (balance < cost)
                {
                    return ActionResult.Fail($"{StoreCatalog.CurrencyName(currency)}不足", "没有扣款，也没有发放物品",
                        $"需要 {cost}，当前 {balance:0.##}", "/签到");
                }
                PlayerService.ApplyCurrency(db, state, -cost, StoreCatalog.LedgerCurrency(currency),
                    "store_buy", "store_good", good.Id.ToString(CultureInfo.InvariantCulture));
            }

            good.SoldCount += amount;
            WriteBoughtCount(good, userId, bought + amount);

            // 「钻石」是旧版赛马积分商店里的货币型商品，购买后直接进 Materials。
            if (good.ItemCatalogId.StartsWith("currency:", StringComparison.Ordinal))
            {
                string target = good.ItemCatalogId["currency:".Length..];
                PlayerService.ApplyCurrency(db, state, amount, target, "store_exchange", "store_good",
                    good.Id.ToString(CultureInfo.InvariantCulture));
            }
            else if (!string.IsNullOrWhiteSpace(good.ItemCatalogId))
            {
                await PlayerService.AddItemAsync(db, userId, good.Name, 1, amount, ct, good.ItemCatalogId);
            }
            else
            {
                // 内容包缺同名物品：按名称造占位物品（旧商品目录里有不少这类）。
                await PlayerService.AddItemAsync(db, userId, good.Name, 1, amount, ct, "store");
            }

            int left = good.Stock < 0 ? -1 : Math.Max(0, good.Stock - good.SoldCount);
            return ActionResult.Success("🛒 购买成功",
                $"{good.Name} ×{amount}"
                    + (currency == StoreCurrency.Free ? " · 免费" : $" · 花费 {cost} {StoreCatalog.CurrencyName(currency)}"),
                [
                    $"- 余额：金币 {state.Credits:0.##}　钻石 {state.Materials:0.##}　锻 {state.ForgePoints:0.##}"
                        + $"　赛马 {state.HorseRacingPoints:0.##}　共斗 {state.CooperativePoints:0.##}",
                    $"- 库存：{(left < 0 ? "无限" : left.ToString(CultureInfo.InvariantCulture))}"
                        + (good.Quota > 0 ? $"　已购 {bought + amount}/{good.Quota}" : ""),
                    "- 已写入账本 ledger"
                ], ["/商店", "/背包", "/状态"]);
        }, cancellationToken);

    private string ResolveStoreKey(string? storeKey, PlayerAccount state)
    {
        if (!string.IsNullOrWhiteSpace(storeKey)) return storeKey;
        return string.IsNullOrWhiteSpace(state.Wallet.LastStoreKey)
            ? StoreCatalog.DailyKey : state.Wallet.LastStoreKey;
    }

    private static StoreCurrency ParseCurrency(string currency) => currency switch
    {
        "credits" => StoreCurrency.Credits,
        "diamonds" => StoreCurrency.Diamonds,
        "forge_points" => StoreCurrency.ForgePoints,
        "horse_points" => StoreCurrency.HorseRacingPoints,
        "coop_points" => StoreCurrency.CooperativePoints,
        _ => StoreCurrency.Free
    };

    private static int ReadBoughtCount(StoreGood good, long userId)
    {
        Dictionary<string, int> map = ParseBuyCounts(good);
        return map.GetValueOrDefault(userId.ToString(CultureInfo.InvariantCulture));
    }

    private static void WriteBoughtCount(StoreGood good, long userId, int count)
    {
        Dictionary<string, int> map = ParseBuyCounts(good);
        map[userId.ToString(CultureInfo.InvariantCulture)] = count;
        good.UsersBuyCountJson = JsonSerializer.Serialize(map);
    }

    private static Dictionary<string, int> ParseBuyCounts(StoreGood good)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(good.UsersBuyCountJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ---------------- 市场 ----------------

    private static async Task<List<MarketListing>> ActiveListingsAsync(BotDbContext db, CancellationToken ct)
        => await db.MarketListings.Where(x => x.Status == 0).OrderBy(x => x.Id).ToListAsync(ct);

    private static async Task<IReadOnlyList<MarketEntry>> MarketViewAsync(
        BotDbContext db, long userId, CancellationToken ct)
    {
        List<MarketListing> listings = await ActiveListingsAsync(db, ct);
        if (listings.Count == 0) return [];
        List<long> ids = listings.Select(x => x.ItemInstanceId).ToList();
        Dictionary<long, PlayerItem> items = await ItemStore.ByIdsAsync(db, ids, ct);
        List<long> sellers = listings.Select(x => x.SellerUserId).Distinct().ToList();
        Dictionary<long, string> handles = await db.Users.AsNoTracking()
            .Where(x => sellers.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Handle, ct);
        return listings.Select((listing, i) =>
        {
            items.TryGetValue(listing.ItemInstanceId, out PlayerItem? item);
            return new MarketEntry(i + 1, listing.Id,
                item is null ? "未知物品" : PlayerService.ReadName(item), item?.Quality ?? 1,
                listing.Price, listing.Fee,
                handles.GetValueOrDefault(listing.SellerUserId, $"旅人{listing.SellerUserId}"),
                listing.SellerUserId == userId);
        }).ToArray();
    }

    public Task<IReadOnlyList<MarketEntry>> GetMarketAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<MarketEntry>>((db, ct) => MarketViewAsync(db, userId, ct), cancellationToken);

    public Task<ActionResult> ListOnMarketAsync(long userId, int itemIndex, long price, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            if (price is < GameplayRules.MarketMinPrice or > GameplayRules.MarketMaxPrice)
            {
                return ActionResult.Fail("价格超出允许范围", "没有上架任何物品",
                    $"价格需在 {GameplayRules.MarketMinPrice} ~ {GameplayRules.MarketMaxPrice} 之间", "/市场");
            }
            int active = await db.MarketListings.CountAsync(
                x => x.SellerUserId == userId && x.Status == 0, ct);
            if (active >= GameplayRules.MarketMaxListingsPerUser)
            {
                return ActionResult.Fail("在售挂单已满", "没有上架任何物品",
                    $"最多同时挂 {GameplayRules.MarketMaxListingsPerUser} 件，先 /下架 @编号", "/市场");
            }
            PlayerItem? item = await PlayerService.ItemByIndexAsync(db, userId, itemIndex, ct);
            if (item is null) return NoListing("背包物品");
            if (item.Locked || item.Bound)
            {
                return ActionResult.Fail("该物品已锁定或绑定", "没有上架任何物品",
                    "先 /解锁 @编号，已装备物品需先 /卸下", "/背包");
            }
            long fee = GameplayRules.MarketFee(price);
            if (state.Credits < fee)
            {
                return ActionResult.Fail("金币不足以支付上架费", "没有上架任何物品",
                    $"需要 {fee} 金币，当前 {state.Credits}", "/状态");
            }
            PlayerService.ApplyCurrency(db, state, -fee, "credits", "market_fee", "listing", price.ToString(CultureInfo.InvariantCulture));
            item.UserId = GameplayRules.EscrowUserId;
            db.MarketListings.Add(new MarketListing
            {
                SellerUserId = userId, ItemInstanceId = item.Id,
                Price = price, Fee = fee, Status = 0, CreatedAtUtc = DateTime.UtcNow
            });
            return ActionResult.Success("🏷️ 上架成功",
                $"{PlayerService.ReadName(item)} · 标价 {price} 金币",
                [$"- 上架费 {fee} 金币（{GameplayRules.MarketFeeRatePercent}%）已记入账本",
                    "- 物品已移入托管，成交前不会出现在背包",
                    "- 用 /市场 查看编号，/下架 @编号 可撤回"], ["/市场", "/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> DelistAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            List<MarketListing> listings = await ActiveListingsAsync(db, ct);
            if (index <= 0 || index > listings.Count) return NoListing("挂单");
            MarketListing listing = listings[index - 1];
            if (listing.SellerUserId != userId)
            {
                return ActionResult.Fail("这不是你的挂单", "没有下架任何物品", "只能下架自己上架的商品", "/市场");
            }
            PlayerItem? item = await ItemStore.ByIdAsync(db, listing.ItemInstanceId, ct);
            listing.Status = 2;
            listing.ClosedAtUtc = DateTime.UtcNow;
            if (item is not null) item.UserId = userId;
            return ActionResult.Success("📦 已下架",
                $"{(item is null ? "未知物品" : PlayerService.ReadName(item))} 已退回背包",
                ["- 上架费不予退还"], ["/市场", "/背包"]);
        }, cancellationToken);

    public Task<ActionResult> BuyMarketAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            List<MarketListing> listings = await ActiveListingsAsync(db, ct);
            if (index <= 0 || index > listings.Count) return NoListing("挂单");
            MarketListing listing = listings[index - 1];
            if (listing.SellerUserId == userId)
            {
                return ActionResult.Fail("不能购买自己的挂单", "没有发生任何交易", "用 /下架 @编号 撤回", "/市场");
            }
            if (state.Credits < listing.Price)
            {
                return ActionResult.Fail("金币不足", "没有发生任何交易",
                    $"需要 {listing.Price} 金币，当前 {state.Credits}", "/状态");
            }
            PlayerItem? item = await ItemStore.ByIdAsync(db, listing.ItemInstanceId, ct);
            if (item is null) return ActionResult.Fail("挂单物品已丢失", "没有发生任何交易", "联系管理员核查", "/市场");

            PlayerAccount seller = await PlayerService.EnsureAsync(db, listing.SellerUserId, ct);
            string reference = listing.Id.ToString(CultureInfo.InvariantCulture);
            PlayerService.ApplyCurrency(db, state, -listing.Price, "credits", "market_buy", "listing", reference);
            PlayerService.ApplyCurrency(db, seller, listing.Price, "credits", "market_sale", "listing", reference);
            item.UserId = userId;
            listing.Status = 1;
            listing.ClosedAtUtc = DateTime.UtcNow;
            return ActionResult.Success("🤝 交易达成",
                $"{PlayerService.ReadName(item)} · {listing.Price} 金币",
                [$"- 买家支出 {listing.Price}，卖家收入 {listing.Price}",
                    "- 双方各写一条 ledger，金额可对账",
                    "- 物品已放入买家背包"], ["/市场", "/背包", "/状态"]);
        }, cancellationToken);

    // ---------------- 报价交易 ----------------

    private static async Task<List<Trade>> ActiveOffersAsync(BotDbContext db, CancellationToken ct)
        => await db.Trades.Where(x => x.Status == 0).OrderBy(x => x.Id).ToListAsync(ct);

    public Task<IReadOnlyList<OfferEntry>> GetOffersAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<OfferEntry>>(async (db, ct) =>
        {
            List<Trade> trades = await ActiveOffersAsync(db, ct);
            if (trades.Count == 0) return [];
            List<long> tradeIds = trades.Select(x => x.Id).ToList();
            List<TradeItem> tradeItems = await db.TradeItems.AsNoTracking()
                .Where(x => tradeIds.Contains(x.TradeId)).ToListAsync(ct);
            List<long> itemIds = tradeItems.Select(x => x.ItemInstanceId).ToList();
            Dictionary<long, PlayerItem> items = await ItemStore.ByIdsAsync(db, itemIds, ct);
            List<long> sellers = trades.Select(x => x.SellerUserId).Distinct().ToList();
            Dictionary<long, string> handles = await db.Users.AsNoTracking()
                .Where(x => sellers.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Handle, ct);
            return trades.Select((trade, i) =>
            {
                TradeItem? line = tradeItems.FirstOrDefault(x => x.TradeId == trade.Id);
                items.TryGetValue(line?.ItemInstanceId ?? 0, out PlayerItem? item);
                return new OfferEntry(i + 1, trade.Id,
                    item is null ? "未知物品" : PlayerService.ReadName(item), line?.Price ?? 0,
                    handles.GetValueOrDefault(trade.SellerUserId, $"旅人{trade.SellerUserId}"),
                    trade.SellerUserId == userId);
            }).ToArray();
        }, cancellationToken);

    public Task<ActionResult> CreateOfferAsync(long userId, int itemIndex, long price, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            if (price is < GameplayRules.MarketMinPrice or > GameplayRules.MarketMaxPrice)
            {
                return ActionResult.Fail("报价超出允许范围", "没有创建报价单",
                    $"价格需在 {GameplayRules.MarketMinPrice} ~ {GameplayRules.MarketMaxPrice} 之间", "/报价列表");
            }
            PlayerItem? item = await PlayerService.ItemByIndexAsync(db, userId, itemIndex, ct);
            if (item is null) return NoListing("背包物品");
            if (item.Locked || item.Bound)
            {
                return ActionResult.Fail("该物品已锁定或绑定", "没有创建报价单",
                    "先 /解锁 @编号，已装备物品需先 /卸下", "/背包");
            }
            item.UserId = GameplayRules.EscrowUserId;
            Trade trade = new() { SellerUserId = userId, Status = 0, CreatedAtUtc = DateTime.UtcNow };
            db.Trades.Add(trade);
            await db.SaveChangesAsync(ct); // 先落库取得 Trade.Id
            db.TradeItems.Add(new TradeItem
            {
                TradeId = trade.Id, FromUserId = userId, ItemInstanceId = item.Id,
                Quantity = 1, Price = price
            });
            return ActionResult.Success("📝 报价单已发布",
                $"{PlayerService.ReadName(item)} · 要价 {price} 金币",
                ["- 物品已移入托管，等待其它旅人 /接受报价 @编号",
                    "- 报价交易不收上架费，撤回用 /撤回报价 @编号"], ["/报价列表", "/背包"]);
        }, cancellationToken);

    public Task<ActionResult> AcceptOfferAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            List<Trade> trades = await ActiveOffersAsync(db, ct);
            if (index <= 0 || index > trades.Count) return NoListing("报价单");
            Trade trade = trades[index - 1];
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
            trade.Status = 1;
            trade.BuyerUserId = userId;
            trade.ClosedAtUtc = DateTime.UtcNow;
            return ActionResult.Success("🤝 报价成交",
                $"{PlayerService.ReadName(item)} · {line.Price} 金币",
                ["- 双方各写一条 ledger", "- 物品已放入买家背包"], ["/报价列表", "/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> CancelOfferAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            List<Trade> trades = await ActiveOffersAsync(db, ct);
            if (index <= 0 || index > trades.Count) return NoListing("报价单");
            Trade trade = trades[index - 1];
            if (trade.SellerUserId != userId)
            {
                return ActionResult.Fail("这不是你的报价单", "没有撤回任何交易", "只能撤回自己发布的报价", "/报价列表");
            }
            TradeItem? line = await db.TradeItems.FirstOrDefaultAsync(x => x.TradeId == trade.Id, ct);
            PlayerItem? item = line is null ? null : await ItemStore.ByIdAsync(db, line.ItemInstanceId, ct);
            trade.Status = 2;
            trade.ClosedAtUtc = DateTime.UtcNow;
            if (item is not null) item.UserId = userId;
            return ActionResult.Success("↩️ 报价已撤回",
                $"{(item is null ? "未知物品" : PlayerService.ReadName(item))} 已退回背包",
                [], ["/报价列表", "/背包"]);
        }, cancellationToken);

    // ---------------- 锻造 / 秘境 ----------------

    public Task<ActionResult> ForgeAsync(long userId, int materialIndex, int minutes, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            if (await db.ForgingJobs.AnyAsync(x => x.UserId == userId && x.SettledAtUtc == null, ct))
            {
                return ActionResult.Fail("已有锻造进行中", "没有开始新的锻造",
                    "等待结束后用 /收取锻造 结算", "/收取锻造");
            }
            PlayerItem? material = await PlayerService.ItemByIndexAsync(db, userId, materialIndex, ct);
            if (material is null) return NoListing("材料");
            if (material.Locked || material.Bound)
            {
                return ActionResult.Fail("材料已锁定或绑定", "没有消耗材料", "先 /解锁 @编号", "/背包");
            }
            if (material.Quantity < GameplayRules.ForgeMaterialQuantity)
            {
                return ActionResult.Fail("材料数量不足", "没有消耗材料",
                    $"需要 {GameplayRules.ForgeMaterialQuantity} 个同类材料", "/背包");
            }
            if (state.Credits < GameplayRules.ForgeCostCredits)
            {
                return ActionResult.Fail("金币不足", "没有消耗材料或金币",
                    $"需要 {GameplayRules.ForgeCostCredits} 金币，当前 {state.Credits}", "/状态");
            }
            string name = PlayerService.ReadName(material);
            int duration = Math.Clamp(minutes, 1, GameplayRules.ForgeMaxMinutes);
            PlayerService.ApplyCurrency(db, state, -GameplayRules.ForgeCostCredits, "credits", "forge_start", "forge", userId.ToString(CultureInfo.InvariantCulture));
            PlayerService.Consume(db, material, GameplayRules.ForgeMaterialQuantity);
            DateTime now = clock.UtcNow;
            db.ForgingJobs.Add(new ForgingJob
            {
                UserId = userId, RecipeKey = "standard",
                StateJson = JsonSerializer.Serialize(new { material = name }),
                StartedAtUtc = now, EndsAtUtc = now.AddMinutes(duration)
            });
            return ActionResult.Success("🔨 开始锻造",
                $"{name} ×{GameplayRules.ForgeMaterialQuantity} · {duration} 分钟后结算",
                [$"- 消耗金币 {GameplayRules.ForgeCostCredits}，剩余 {state.Credits}",
                    $"- 成品品质：{GameplayRules.QualityName(GameplayRules.ForgeOutputQuality)}"],
                ["/收取锻造", "/背包"]);
        }, cancellationToken);

    public Task<ActionResult> ClaimForgeAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            ForgingJob? job = await db.ForgingJobs
                .Where(x => x.UserId == userId && x.SettledAtUtc == null)
                .OrderBy(x => x.EndsAtUtc).FirstOrDefaultAsync(ct);
            if (job is null) return ActionResult.Fail("没有进行中的锻造", "没有产出任何物品", "先用 /锻造 @编号 [分钟] 开始", "/锻造 @1 1");
            if (job.EndsAtUtc > clock.UtcNow) return NotReady("锻造", job.EndsAtUtc);
            await PlayerService.AddItemAsync(db, userId, "锻造·精制装备",
                GameplayRules.ForgeOutputQuality, 1, ct, "forge");
            job.SettledAtUtc = DateTime.UtcNow;
            return ActionResult.Success("⚒️ 锻造完成", "精制装备已放入背包",
                [$"- 品质：{GameplayRules.QualityName(GameplayRules.ForgeOutputQuality)}"],
                ["/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> InstanceAsync(long userId, string difficulty, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            if (await db.ExplorationRuns.AnyAsync(x => x.UserId == userId && x.SettledAtUtc == null, ct))
            {
                return ActionResult.Fail("已有秘境探索进行中", "没有开始新的探索",
                    "等待结束后用 /收取秘境 结算", "/收取秘境");
            }
            if (state.Credits < GameplayRules.InstanceCostCredits)
            {
                return ActionResult.Fail("金币不足", "没有开始探索",
                    $"需要 {GameplayRules.InstanceCostCredits} 金币，当前 {state.Credits}", "/状态");
            }

            // 规格解析：`金币 3` / `精英` / `3` / 空 → 类型 + 难度 1-5
            string spec = string.IsNullOrWhiteSpace(difficulty) ? "" : difficulty.Trim();
            string? kindToken = null;
            int level = 1;
            foreach (string token in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, out int parsed))
                {
                    level = GameplayRules.ClampDifficulty(parsed);
                    continue;
                }
                if (GameplayRules.InstanceKinds.Any(x => token.Contains(x.Name, StringComparison.Ordinal)))
                {
                    kindToken = token;
                    continue;
                }
                level = GameplayRules.ParseInstanceDifficulty(token);
            }
            GameplayRules.InstanceKind kind = GameplayRules.ParseInstanceKind(kindToken);
            string kindName = GameplayRules.InstanceKindName(kind);
            string region = $"{kindName}:{level}";

            DateTime now = clock.UtcNow;
            PlayerService.ApplyCurrency(db, state, -GameplayRules.InstanceCostCredits, "credits", "instance_start", "instance", region);
            db.ExplorationRuns.Add(new ExplorationRun
            {
                UserId = userId, RegionKey = region,
                StateJson = JsonSerializer.Serialize(new { kind = kind.ToString(), difficulty = level }),
                StartedAtUtc = now, EndsAtUtc = now.AddMinutes(GameplayRules.InstanceDefaultMinutes)
            });
            (long exp, long credits, long diamonds, int items) = GameplayRules.InstanceReward(kind, level);
            return ActionResult.Success("🗺️ 进入秘境",
                $"{kindName}秘境 · 难度 {level} · {GameplayRules.InstanceDefaultMinutes} 分钟后结算",
                [$"- 消耗金币 {GameplayRules.InstanceCostCredits}",
                    $"- 敌人：{GameplayRules.InstanceEnemyCount(level)} 名 Lv.{GameplayRules.InstanceEnemyLevel(level)}",
                    $"- 预计收获：{exp} 经验 / {credits} 金币 / {diamonds} 钻石 / {items} 件掉落",
                    "- 胜利全额，失败半额；用 /收取秘境 结算"],
                ["/收取秘境", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> ClaimInstanceAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            ExplorationRun? run = await db.ExplorationRuns
                .Where(x => x.UserId == userId && x.SettledAtUtc == null)
                .OrderBy(x => x.EndsAtUtc).FirstOrDefaultAsync(ct);
            if (run is null) return ActionResult.Fail("没有进行中的探索", "没有结算任何奖励", "先用 /秘境 开始", "/秘境");
            if (run.EndsAtUtc > clock.UtcNow) return NotReady("探索", run.EndsAtUtc);

            // 结算时打一场真实战斗，胜负决定奖励倍率（胜：全额 + 掉落；负：半额 + 降档掉落）。
            PlayerCharacter? mine = await PlayerService.MainCharacterAsync(db, userId, ct);
            if (mine is null)
            {
                return ActionResult.Fail("没有可出战的角色", "没有结算任何奖励",
                    "先 /注册 建立存档，或用 /抽卡 获取角色", "/状态");
            }

            // 出战队伍：优先使用小队编成（最多 3 人），未编成则主战单刷。
            List<PlayerCharacter> roster = [];
            Squad? squad = await db.Squads.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive, ct);
            if (squad is not null)
            {
                List<long> memberIds = await db.SquadMembers.AsNoTracking()
                    .Where(x => x.SquadId == squad.Id).OrderBy(x => x.Slot)
                    .Select(x => x.CharacterId).ToListAsync(ct);
                if (memberIds.Count > 0)
                {
                    string? mainGuid = state.Wallet.MainCharacterGuid;
                    List<PlayerCharacter> loaded = (await CharacterStore.ListAsync(db, userId, mainGuid, ct))
                        .Where(x => memberIds.Contains(x.Id)).ToList();
                    roster = [.. loaded.OrderBy(x => memberIds.IndexOf(x.Id))];
                }
            }
            if (roster.Count == 0) roster = [mine];

            List<CoreCharacter> party = [];
            foreach (PlayerCharacter member in roster)
            {
                CoreCharacter? built = CombatCharacterFactory.FromCatalog(
                    member.CatalogId, member.Level, member.Exp, member.BreakLevel);
                if (built is not null) party.Add(built);
            }
            if (party.Count == 0)
            {
                return ActionResult.Fail("角色数据缺失", "没有结算任何奖励",
                    "内容包可能已更新，请反馈给管理员", "/状态");
            }

            // RegionKey 形如「金币:3」，解析出秘境类型与难度。
            string[] parts = run.RegionKey.Split(':');
            GameplayRules.InstanceKind kind = GameplayRules.ParseInstanceKind(parts[0]);
            int level = parts.Length > 1 && int.TryParse(parts[1], out int parsed)
                ? GameplayRules.ClampDifficulty(parsed) : 1;
            string kindName = GameplayRules.InstanceKindName(kind);

            // 敌人数量与等级跟随难度（旧版：数量 = 难度 + 1，等级 = 难度 × 8）。
            int enemyCount = GameplayRules.InstanceEnemyCount(level);
            int enemyLevel = GameplayRules.InstanceEnemyLevel(level);
            List<CoreCharacter> enemies = [];
            for (int i = 0; i < enemyCount; i++)
            {
                CoreCharacter template = FunGameService.Characters[(i + 1) % FunGameService.Characters.Count];
                enemies.Add(CombatCharacterFactory.Build(template, enemyLevel));
            }

            // 2 人以上走分队战，单人走混战。
            BattleOptions battleOptions = new() { MaxRound = 120, UseRoundRewards = false };
            BattleResult battle = party.Count >= 2
                ? FunGameActionQueue.RunTeam([new CoreTeam("小队", party), new CoreTeam("秘境守卫", enemies)], battleOptions)
                : FunGameActionQueue.RunMix([party[0], .. enemies], battleOptions);
            bool win = battle.Winner is not null && party.Any(x => ReferenceEquals(x, battle.Winner));
            double rate = win ? 1.0 : 0.5;

            (long fullExp, long fullCredits, long fullDiamonds, int fullItems) =
                GameplayRules.InstanceReward(kind, level);
            long exp = (long)(fullExp * rate);
            long credits = (long)(fullCredits * rate);
            long diamonds = (long)(fullDiamonds * rate);
            int itemCount = win ? fullItems : Math.Max(0, fullItems / 2);

            PlayerCharacter? levelTarget = await PlayerService.MainCharacterAsync(db, userId, ct);
            int levelGain = 0;
            if (levelTarget is not null && exp > 0)
            {
                int beforeLevel = levelTarget.Level;
                levelTarget.Exp += exp;
                levelTarget.Core.OnLevelUp();
                levelGain = levelTarget.Level - beforeLevel;
                CharacterStore.Sync(levelTarget);
            }
            if (credits > 0)
            {
                PlayerService.ApplyCurrency(db, state, credits, "credits", "instance_reward", "instance", run.RegionKey);
            }
            if (diamonds > 0)
            {
                PlayerService.ApplyCurrency(db, state, diamonds, "diamonds", "instance_reward", "instance", run.RegionKey);
            }
            List<string> drops = [];
            for (int i = 0; i < itemCount; i++)
            {
                int quality = Math.Clamp(GameplayRules.RollGachaQuality(random) - (win ? 0 : 2), 1, 5);
                await PlayerService.AddItemAsync(db, userId, $"{kindName}秘境掉落", quality, 1, ct, "instance");
                drops.Add(GameplayRules.QualityName(quality));
            }
            run.SettledAtUtc = DateTime.UtcNow;

            List<string> lines =
            [
                $"- 结果：{(win ? "清剿成功" : "被迫撤退")} · {battle.Rounds} 回合 · 难度 {level} · 出战 {party.Count} 人",
                $"- 经验 +{exp}　金币 +{credits}　钻石 +{diamonds}",
                $"- 掉落：{(drops.Count == 0 ? "无" : string.Join("、", drops))} · {kindName}秘境",
                levelTarget is null
                    ? $"- 金币：{state.Credits}　钻石：{state.Materials}"
                    : $"- {levelTarget.Name} 经验 +{exp}{(levelGain > 0 ? $" · 升 {levelGain} 级 → Lv.{levelTarget.Level}" : $"（Lv.{levelTarget.Level}）")}　金币：{state.Credits}　钻石：{state.Materials}"
            ];
            lines.AddRange(battle.Tail(2)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => "  " + (x.Length > 120 ? x[..120] + "…" : x)));

            return ActionResult.Success(win ? "🏆 秘境结算" : "🚪 秘境撤退",
                win ? $"经验 +{exp} · 金币 +{credits}" : $"经验 +{exp} · 金币 +{credits}（半额）",
                lines,
                ["/背包", "/状态", "/秘境"]);
        }, cancellationToken);

    // ---------------- 决斗 / Boss / 排行 ----------------

    public Task<ActionResult> DuelAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            if (state.Credits < GameplayRules.DuelCostCredits)
            {
                return ActionResult.Fail("金币不足", "没有开始决斗",
                    $"需要 {GameplayRules.DuelCostCredits} 金币，当前 {state.Credits}", "/状态");
            }

            // 出战角色：优先主战，否则取第一个角色（与旧版「默认角色 1」一致）。
            PlayerCharacter? mine = await PlayerService.MainCharacterAsync(db, userId, ct);
            if (mine is null)
            {
                return ActionResult.Fail("没有可出战的角色", "没有扣除报名费",
                    "先 /注册 建立存档，或用 /抽卡 获取角色", "/状态");
            }

            CoreCharacter? me = CombatCharacterFactory.FromCatalog(
                mine.CatalogId, mine.Level, mine.Exp, mine.BreakLevel);
            CoreCharacter? opponentTemplate = CombatCharacterFactory.PickOpponent(mine.CatalogId);
            if (me is null || opponentTemplate is null)
            {
                return ActionResult.Fail("角色数据缺失", "没有扣除报名费",
                    "内容包可能已更新，请反馈给管理员", "/状态");
            }

            // 真实战斗：复用 Core 的 GamingQueue 生命周期，不再用概率结算。
            CoreCharacter enemy = CombatCharacterFactory.Build(opponentTemplate, mine.Level, 0, mine.BreakLevel);
            BattleResult battle = FunGameActionQueue.RunMix([me, enemy],
                new BattleOptions { MaxRound = 100, UseRoundRewards = false });
            bool win = battle.Winner is not null && ReferenceEquals(battle.Winner, me);

            PlayerService.ApplyCurrency(db, state, -GameplayRules.DuelCostCredits,
                "credits", "duel_entry", "duel", userId.ToString(CultureInfo.InvariantCulture));
            if (win)
            {
                PlayerService.ApplyCurrency(db, state, GameplayRules.DuelRewardCredits,
                    "credits", "duel_reward", "duel", userId.ToString(CultureInfo.InvariantCulture));
            }
            int gain = win ? GameplayRules.DuelWinPoints : GameplayRules.DuelLosePoints;
            long points = await AddPointsAsync(db, userId, gain, ct);

            List<string> lines =
            [
                $"- 我方：{me.Name} Lv.{me.Level}　HP {me.HP:F0}/{me.MaxHP:F0}",
                $"- 对手：{enemy.Name} Lv.{enemy.Level}　HP {enemy.HP:F0}/{enemy.MaxHP:F0}",
                $"- {battle.Rounds} 回合 · 耗时 {battle.TotalTime:F1}",
                $"- 当前战绩积分：{points}　金币：{state.Credits}",
                "- 战斗摘要："
            ];
            lines.AddRange(battle.Tail(3)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => "  " + (x.Length > 120 ? x[..120] + "…" : x)));

            return ActionResult.Success(win ? "⚔️ 决斗胜利" : "🛡️ 决斗失利",
                win
                    ? $"击败 {enemy.Name} · 净获 {GameplayRules.DuelRewardCredits - GameplayRules.DuelCostCredits} 金币 · 战绩 +{gain}"
                    : $"不敌 {enemy.Name} · 损失报名费 {GameplayRules.DuelCostCredits} · 战绩 +{gain}",
                lines,
                ["/决斗", "/排行榜 战绩", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> BossAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            // 全服共享一只残血目标：谁都能接着打，血量跨挑战累计。
            BossEntry? bossEntry = BossPool.Current();
            if (bossEntry is null)
            {
                return ActionResult.Fail("讨伐目标正在刷新", "没有扣除金币",
                    "稍后再试，或先去 /决斗 练手", "/决斗");
            }
            PlayerCharacter? mine = await PlayerService.MainCharacterAsync(db, userId, ct);
            if (mine is null)
            {
                return ActionResult.Fail("没有可出战的角色", "没有扣除金币",
                    "先 /注册 建立存档，或用 /抽卡 获取角色", "/状态");
            }

            CoreCharacter? me = CombatCharacterFactory.FromCatalog(
                mine.CatalogId, mine.Level, mine.Exp, mine.BreakLevel);
            if (me is null)
            {
                return ActionResult.Fail("角色数据缺失", "没有扣除金币",
                    "内容包可能已更新，请反馈给管理员", "/状态");
            }

            // Boss 副本继承当前剩余生命，因此这次伤害会累计到全服共享的目标上。
            string reference = $"boss-{bossEntry.Index}";
            double maxHp = bossEntry.Character.MaxHP;
            CoreCharacter boss = BossPool.CreateFighter(bossEntry);
            BattleResult battle = FunGameActionQueue.RunMix([me, boss],
                new BattleOptions { MaxRound = 150, UseRoundRewards = false });

            bool killed = boss.HP <= 0;
            double damage = maxHp <= 0 ? 0 : Math.Clamp(1 - Math.Max(0, boss.HP) / maxHp, 0, 1);
            if (killed) damage = 1;
            BossPool.ApplyResult(bossEntry, boss.HP, boss.MP, boss.EP);

            // 旧版 FightBoss 不收入场费；击杀只做 Inventory.Materials += boss.Level（材料在本作即钻石）。
            int credits = 0;
            int diamonds = killed ? bossEntry.Level : 0;
            if (credits > 0)
            {
                PlayerService.ApplyCurrency(db, state, credits, "credits", "boss_reward", "boss", reference);
            }
            if (diamonds > 0)
            {
                PlayerService.ApplyCurrency(db, state, diamonds, "diamonds", "boss_reward", "boss", reference);
            }
            int gain = killed
                ? GameplayRules.BossWinPoints
                : Math.Max(1, (int)(GameplayRules.BossWinPoints * damage * 0.4));
            long points = await AddPointsAsync(db, userId, gain, ct);

            List<string> lines =
            [
                $"- 目标：{bossEntry.Display}　剩余 HP {bossEntry.HPPercent:P0}（全服共享）",
                $"- 我方：{me.Name} Lv.{me.Level}　HP {me.HP:F0}/{me.MaxHP:F0}",
                $"- 本次伤害占比：{damage:P0} · {battle.Rounds} 回合",
                diamonds > 0
                    ? $"- 结算：钻石 +{diamonds}（= 目标等级）· 战绩 +{gain}"
                    : $"- 结算：本次未击杀，只有战绩 +{gain}（击杀才发钻石）",
                $"- 当前战绩：{points}　金币：{state.Credits}"
            ];
            lines.AddRange(battle.Tail(2)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => "  " + (x.Length > 120 ? x[..120] + "…" : x)));
            if (damage <= 0 && !killed)
            {
                lines.Add("- 提示：本次未对目标造成伤害，先 /练级 提升角色或 /背包 更换装备再来");
            }

            return ActionResult.Success(killed ? "🐉 Boss 讨伐成功" : "🛡️ Boss 讨伐失败",
                killed
                    ? $"击败 {bossEntry.Name} · 金币 +{credits} · 钻石 +{diamonds} · 战绩 +{gain}"
                    : $"{bossEntry.Name} 剩 {Math.Max(0, boss.HP):F0} HP · 按伤害结算 {credits} 金币",
                lines,
                ["/排行榜 战绩", "/决斗", "/状态"]);
        }, cancellationToken);

    public Task<IReadOnlyList<RankEntry>> GetRankingAsync(string board, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<RankEntry>>(async (db, ct) =>
        {
            string normalized = board.ToLowerInvariant() switch
            {
                "level" or "等级" => "level",
                "points" or "战绩" => "points",
                _ => "credits"
            };
            if (normalized == "points")
            {
                List<ActivityProgress> rows = await db.ActivityProgress.AsNoTracking()
                    .Where(x => x.ActivityKey == PointsKey).ToListAsync(ct);
                List<long> ids = rows.Select(x => x.UserId).ToList();
                Dictionary<long, string> handles = await db.Users.AsNoTracking()
                    .Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Handle, ct);
                return rows.Select(x => new { x.UserId, Value = ReadPoints(x.ProgressJson) })
                    .Where(x => x.Value > 0)
                    .OrderByDescending(x => x.Value).Take(GameplayRules.RankingTop)
                    .Select((x, i) => new RankEntry(i + 1,
                        handles.GetValueOrDefault(x.UserId, $"旅人{x.UserId}"), x.Value)).ToArray();
            }

            if (normalized == "level")
            {
                // 等级属于角色（Core 模型），取每个用户等级最高的角色上榜。
                List<CoreCharacterRecord> rows = await db.CoreCharacters.AsNoTracking()
                    .OrderByDescending(x => x.Level).ThenByDescending(x => x.Exp)
                    .Take(GameplayRules.RankingTop * 3).ToListAsync(ct);
                List<CoreCharacterRecord> top = [.. rows
                    .GroupBy(x => x.UserId)
                    .Select(g => g.OrderByDescending(x => x.Level).ThenByDescending(x => x.Exp).First())
                    .OrderByDescending(x => x.Level).ThenByDescending(x => x.Exp)
                    .Take(GameplayRules.RankingTop)];
                List<long> levelIds = top.Select(x => x.UserId).ToList();
                Dictionary<long, string> levelHandles = await db.Users.AsNoTracking()
                    .Where(x => levelIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Handle, ct);
                return top.Select((row, i) => new RankEntry(i + 1,
                    levelHandles.GetValueOrDefault(row.UserId, $"旅人{row.UserId}"),
                    row.Level)).ToArray();
            }

            // 金币榜直接读钱包（CorePlayers.Credits 为 double），不再依赖进度表。
            List<CorePlayerRecord> wallets = await db.CorePlayers.AsNoTracking()
                .OrderByDescending(x => x.Credits).Take(GameplayRules.RankingTop).ToListAsync(ct);
            List<long> walletIds = wallets.Select(x => x.UserId).ToList();
            Dictionary<long, string> walletHandles = await db.Users.AsNoTracking()
                .Where(x => walletIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Handle, ct);
            return wallets.Select((wallet, i) => new RankEntry(i + 1,
                walletHandles.GetValueOrDefault(wallet.UserId, $"旅人{wallet.UserId}"),
                wallet.Credits)).ToArray();
        }, cancellationToken);

    // ---------------- 内部工具 ----------------

    private static async Task<long> AddPointsAsync(BotDbContext db, long userId, int delta, CancellationToken ct)
    {
        ActivityProgress? row = await db.ActivityProgress.FirstOrDefaultAsync(
            x => x.UserId == userId && x.ActivityKey == PointsKey, ct);
        long points = (row is null ? 0 : ReadPoints(row.ProgressJson)) + delta;
        string json = JsonSerializer.Serialize(new { points });
        if (row is null)
        {
            db.ActivityProgress.Add(new ActivityProgress
            {
                UserId = userId, ActivityKey = PointsKey,
                ProgressJson = json, ClaimedJson = "[]", UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            row.ProgressJson = json;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        return points;
    }

    private static long ReadPoints(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("points", out JsonElement points)
                ? points.GetInt64() : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static ActionResult NoListing(string what)
        => ActionResult.Fail($"{what} @编号 不存在", "没有改动任何数据",
            "先查看列表确认编号（编号按列表顺序稳定排列）", "/状态");

    private ActionResult NotReady(string what, DateTime endsAtUtc)
        => ActionResult.Fail($"{what}尚未结束", "没有结算任何奖励",
            $"还需约 {(int)Math.Ceiling((endsAtUtc - clock.UtcNow).TotalSeconds)} 秒，稍后再收取", "/状态");
}
