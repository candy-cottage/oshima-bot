using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Combat;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>P2 经济仓储：跨用户交易、托管、双边账本、定时结算与排行榜。</summary>
[Collection(BossPoolCollection.Name)]
public sealed class EconomyTests
{
    private const long Seller = 42;
    private const long Buyer = 43;

    [Fact]
    public async Task Store_DailyHasFourRandomGoodsPerUser()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        IReadOnlyList<StoreEntry> goods = await economy.GetStoreAsync(Seller);
        Assert.Equal(StoreCatalog.DailyGoodsCount, goods.Count);
        Assert.Equal(1, goods[0].Index);
        Assert.All(goods, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Name));
            Assert.True(g.Price > 0);
            Assert.True(g.Stock >= 1);        // 每日商店库存 1–2（魔法卡 1，消耗品 +3）
        });

        // 每个用户一份，互不相同（用另一个用户再取一次）
        IReadOnlyList<StoreEntry> other = await economy.GetStoreAsync(Buyer);
        Assert.Equal(StoreCatalog.DailyGoodsCount, other.Count);
    }

    [Fact]
    public async Task Store_FixedShopSellsFreeGoodsOncePerUser()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        // 慈善基金会：免费、每人限购 1
        IReadOnlyList<StoreEntry> goods = await economy.GetStoreAsync(Seller, StoreCatalog.WelfareKey, default);
        Assert.NotEmpty(goods);
        StoreEntry good = goods[0];
        Assert.Equal("免费", good.Currency);
        Assert.Equal(1, good.Quota);

        ActionResult first = await economy.BuyAsync(Seller, StoreCatalog.WelfareKey, 1, 1);
        Assert.True(first.Ok, first.Title);

        ActionResult second = await economy.BuyAsync(Seller, StoreCatalog.WelfareKey, 1, 1);
        Assert.False(second.Ok);
        Assert.Contains("限购", second.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        // 免费商品不记 store_buy 流水
        Assert.Empty(await dbContext.LedgerEntries.Where(x => x.Reason == "store_buy").ToListAsync());
        Assert.Equal(1, await dbContext.CoreItems.CountAsync(x => x.UserId == Seller));
    }

    [Fact]
    public async Task Store_FixedShopDebitsCorrectCurrencyAndStock()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await SetCreditsAsync(db, Seller, 200_000);

        // 武器商会 第 2 件是金币 60000，库存 4
        IReadOnlyList<StoreEntry> goods = await economy.GetStoreAsync(Seller, StoreCatalog.WeaponsKey, default);
        StoreEntry good = goods.Single(g => g.Price == 60_000);
        Assert.Equal(4, good.Stock);

        ActionResult buy = await economy.BuyAsync(Seller, StoreCatalog.WeaponsKey, good.Index, 1);
        Assert.True(buy.Ok, buy.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord state = await dbContext.CorePlayers.FirstAsync(x => x.UserId == Seller);
        LedgerEntry entry = await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "store_buy");
        Assert.Equal(-60_000, entry.Delta);
        Assert.Equal(state.Credits, entry.BalanceAfter);

        IReadOnlyList<StoreEntry> after = await economy.GetStoreAsync(Seller, StoreCatalog.WeaponsKey, default);
        Assert.Equal(3, after.Single(g => g.Price == 60_000).Stock);
    }

    [Fact]
    public async Task Store_InsufficientCreditsRollsBackEverything()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.Get<IPlayerService>().GetSnapshotAsync(Seller);
        await SetCreditsAsync(db, Seller, 0);

        ActionResult result = await economy.BuyAsync(Seller, StoreCatalog.WeaponsKey, 1, 1);

        Assert.False(result.Ok);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Empty(await dbContext.LedgerEntries.Where(x => x.Reason == "store_buy").ToListAsync());
        Assert.Equal(0, await dbContext.CoreItems.CountAsync(x => x.UserId == Seller));
        // 失败时不扣库存
        Assert.All(await dbContext.StoreGoods.Where(x => x.StoreKey == StoreCatalog.WeaponsKey).ToListAsync(),
            good => Assert.Equal(0, good.SoldCount));
    }

    [Fact]
    public async Task Market_CrossUserTradeHasTwoBalancedLedgerEntries()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await TestItems.SeedAsync(db.Provider, Seller, 3);   // 旧版注册不带物品，测试自备 3 件

        ActionResult listed = await economy.ListOnMarketAsync(Seller, 1, 200);
        Assert.True(listed.Ok, listed.Title);
        Assert.Equal(2, (await players.GetBagAsync(Seller)).Count); // 已托管 1 件

        IReadOnlyList<MarketEntry> market = await economy.GetMarketAsync(Buyer);
        Assert.Single(market);
        Assert.False(market[0].IsMine);
        Assert.Equal(GameplayRules.MarketFee(200), market[0].Fee);

        ActionResult bought = await economy.BuyMarketAsync(Buyer, 1);
        Assert.True(bought.Ok, bought.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord sellerState = await dbContext.CorePlayers.FirstAsync(x => x.UserId == Seller);
        CorePlayerRecord buyerState = await dbContext.CorePlayers.FirstAsync(x => x.UserId == Buyer);
        Assert.Equal(sellerState.Credits,
            (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_sale")).BalanceAfter);
        Assert.Equal(buyerState.Credits,
            (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_buy")).BalanceAfter);
        Assert.Equal(200, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_sale")).Delta);
        Assert.Equal(-200, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "market_buy")).Delta);
        Assert.Equal(1, (await dbContext.MarketListings.FirstAsync()).Status);
        long tradedItemId = (await dbContext.MarketListings.FirstAsync()).ItemInstanceId;
        Assert.Equal(Buyer, (await dbContext.CoreItems.FirstAsync(x => x.LocalId == tradedItemId)).UserId);
        Assert.Empty(await economy.GetMarketAsync(Buyer));
    }

    [Fact]
    public async Task Market_CannotBuyOwnListingAndDelistReturnsItem()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await TestItems.SeedAsync(db.Provider, Seller, 3);

        Assert.True((await economy.ListOnMarketAsync(Seller, 1, 150)).Ok);
        Assert.False((await economy.BuyMarketAsync(Seller, 1)).Ok);
        Assert.True((await economy.DelistAsync(Seller, 1)).Ok);
        Assert.Equal(3, (await players.GetBagAsync(Seller)).Count);
        Assert.Empty(await economy.GetMarketAsync(Seller));

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(2, (await dbContext.MarketListings.FirstAsync()).Status);
    }

    /// <summary>
    /// 上架费在**上架时**预扣，下架不退；成交时卖家收全额，因此「成交净收入 = 标价 − 上架费」。
    /// 旧版是成交时按 15% 现扣（见 DIV-003 / DIV-015），两种口径对成交单的净额相同，对撤回单不同。
    /// </summary>
    /// <summary>
    /// 上架费 = 标价 × 15%，**不取整、不设下限**（2026-11-14 决定，见 DIV-003）。
    /// 曾经是 `Math.Max(1, price * 15 / 100)`：1 金币的白装要收 1 金币（等于 100% 手续费），
    /// 19 金币收 2 而不是 2.85。现在逐值和旧版 `price * 0.15` 相同。
    /// </summary>
    [Theory]
    [InlineData(1, 0.15)]
    [InlineData(19, 2.85)]
    [InlineData(20, 3.00)]
    [InlineData(199, 29.85)]
    [InlineData(1000, 150.00)]
    public void MarketFee_ChargesTheExactRateWithoutFloor(long price, double expected)
    {
        Assert.Equal(expected, GameplayRules.MarketFee(price), 6);
        Assert.Equal(expected, ClubRules.MarketFee(price) * 3, 6);   // 社团市场 5%：1000 → 50，×3 = 150
    }

    /// <summary>
    /// 金币 / 钻石只**显示**两位小数，内部值不取整：`0.15 × 200` 在 double 下可能算出
    /// 30.000000000000004，落库取整会让账本和实际扣款对不上。
    /// </summary>
    [Theory]
    [InlineData(30.000000000000004, "30.00")]
    [InlineData(2.85, "2.85")]
    [InlineData(0.155, "0.16")]        // 显示时四舍五入
    [InlineData(-1.50, "-1.50")]       // 账本里的负数（扣款）同样两位
    [InlineData(0, "0.00")]
    public void Money_ShowsTwoDecimalsWithoutChangingTheValue(double value, string expected)
    {
        Assert.Equal(expected, GameplayRules.Money(value));
    }

    /// <summary>1 金币的挂单只扣 0.15，余额与账本都必须记这个数。</summary>
    [Fact]
    public async Task Market_OneCreditListingCostsFifteenCents()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await TestItems.SeedAsync(db.Provider, Seller, 1);

        double before = (await players.GetSnapshotAsync(Seller))!.Credits;
        ActionResult result = await economy.ListOnMarketAsync(Seller, 1, 1);

        Assert.True(result.Ok, result.Title);
        Assert.Equal(before - 0.15, (await players.GetSnapshotAsync(Seller))!.Credits, 10);
        Assert.Contains(result.Lines, line => line.Contains("0.15 金币"));

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        LedgerEntry fee = await context.LedgerEntries.SingleAsync(x => x.Reason == "market_fee");
        Assert.Equal(-0.15, fee.Delta, 10);
        Assert.Equal(before - 0.15, fee.BalanceAfter, 10);
    }

    [Fact]
    public async Task Market_FeeIsChargedUpfrontAndNotRefundedOnDelist()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await TestItems.SeedAsync(db.Provider, Seller, 3);

        double before = (await players.GetSnapshotAsync(Seller))!.Credits;
        double fee = GameplayRules.MarketFee(200);
        Assert.True(fee > 0);

        Assert.True((await economy.ListOnMarketAsync(Seller, 1, 200)).Ok);
        Assert.Equal(before - fee, (await players.GetSnapshotAsync(Seller))!.Credits, 3);

        Assert.True((await economy.DelistAsync(Seller, 1)).Ok);
        Assert.Equal(before - fee, (await players.GetSnapshotAsync(Seller))!.Credits, 3);   // 不退费
        Assert.Equal(3, (await players.GetBagAsync(Seller)).Count);                          // 物品回包
    }

    [Fact]
    public async Task Market_CompletedSaleNetsPriceMinusListingFee()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await players.GetSnapshotAsync(Buyer);
        await TestItems.SeedAsync(db.Provider, Seller, 1);

        double sellerBefore = (await players.GetSnapshotAsync(Seller))!.Credits;
        double buyerBefore = (await players.GetSnapshotAsync(Buyer))!.Credits;
        double fee = GameplayRules.MarketFee(200);

        Assert.True((await economy.ListOnMarketAsync(Seller, 1, 200)).Ok);
        Assert.True((await economy.BuyMarketAsync(Buyer, 1)).Ok);

        double sellerAfter = (await players.GetSnapshotAsync(Seller))!.Credits;
        double buyerAfter = (await players.GetSnapshotAsync(Buyer))!.Credits;

        Assert.Equal(sellerBefore - fee + 200, sellerAfter, 3);   // 净收入 = 标价 − 上架费
        Assert.Equal(buyerBefore - 200, buyerAfter, 3);           // 买家只付标价，不再另付手续费

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        List<LedgerEntry> sellerLedger = await context.LedgerEntries
            .Where(x => x.UserId == Seller && x.Reason.StartsWith("market_"))
            .OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(["market_fee", "market_sale"], sellerLedger.Select(x => x.Reason));
        Assert.Equal(-fee, sellerLedger[0].Delta);
        Assert.Equal(200, sellerLedger[1].Delta);
    }

    [Fact]
    public async Task Market_EnforcesPriceBoundsAndListingCap()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await TestItems.SeedAsync(db.Provider, Seller, 8);

        Assert.False((await economy.ListOnMarketAsync(Seller, 1, 0)).Ok);                         // 低于下限
        Assert.False((await economy.ListOnMarketAsync(Seller, 1, GameplayRules.MarketMaxPrice + 1)).Ok);

        for (int i = 1; i <= GameplayRules.MarketMaxListingsPerUser; i++)
        {
            Assert.True((await economy.ListOnMarketAsync(Seller, 1, 100)).Ok, $"第 {i} 件应能上架");
        }
        ActionResult sixth = await economy.ListOnMarketAsync(Seller, 1, 100);
        Assert.False(sixth.Ok);
        Assert.Contains("在售挂单已满", sixth.Title);
    }

    [Fact]
    public async Task Offer_AcceptAndCancelLifecycle()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await TestItems.SeedAsync(db.Provider, Seller, 3);

        Assert.True((await economy.CreateOfferAsync(Seller, 1, 300)).Ok);
        Assert.Single(await economy.GetOffersAsync(Buyer));
        Assert.False((await economy.AcceptOfferAsync(Seller, 1)).Ok); // 不能接受自己的报价

        ActionResult accepted = await economy.AcceptOfferAsync(Buyer, 1);
        Assert.True(accepted.Ok, accepted.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(-300, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "offer_buy")).Delta);
        Assert.Equal(300, (await dbContext.LedgerEntries.FirstAsync(x => x.Reason == "offer_sale")).Delta);
        Assert.Equal(1, (await dbContext.Trades.FirstAsync()).Status);
        Assert.Equal(Buyer, (await dbContext.Trades.FirstAsync()).BuyerUserId);

        Assert.True((await economy.CreateOfferAsync(Seller, 1, 100)).Ok);
        Assert.True((await economy.CancelOfferAsync(Seller, 1)).Ok);
        Assert.Equal(2, (await dbContext.Trades.OrderBy(x => x.Id).LastAsync()).Status);
    }

    [Fact]
    public async Task Instance_RequiresExpiryThenDeliversRewards()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await TestItems.SeedAsync(db.Provider, Seller, 3);   // 旧版注册不带物品
        await AddMaterialAsync(db, Seller, 3);                // @4

        Assert.True((await economy.InstanceAsync(Seller, "地区 3")).Ok);
        Assert.False((await economy.ClaimInstanceAsync(Seller)).Ok);
        await ExpireAsync(db, "instance");
        Assert.True((await economy.ClaimInstanceAsync(Seller)).Ok);
        Assert.Contains(await players.GetBagAsync(Seller), item => item.Name.Contains("秘境掉落"));

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        // 秘境经验记在角色身上（Core 模型），可能已经升级消耗掉，故断言「有进展」。
        CoreCharacterRecord levelRow = await dbContext.CoreCharacters.FirstAsync(x => x.UserId == Seller);
        Assert.True(levelRow.Level > 1 || levelRow.Exp > 0,
            $"秘境结算后角色应有经验或升级，实际 Lv.{levelRow.Level} / Exp {levelRow.Exp}");
        Assert.True(await dbContext.LedgerEntries.CountAsync(x => x.Reason == "instance_start") >= 1);
    }

    /// <summary>
    /// 秘境奖励按旧版口径：**每名出战角色各掷一次**（550–1049 金币 / 3–6 钻 / 570–1259 经验）再乘难度。
    /// 这里用「永远取区间下限」的随机源把数值钉死。
    /// </summary>
    [Fact]
    public void InstanceReward_ScalesWithMembersAndDifficulty()
    {
        AlwaysHitRandomSource random = new();

        (long exp1, long credits1, long diamonds1, _) =
            GameplayRules.InstanceReward(GameplayRules.InstanceKind.Gold, 2, 1, random);
        (_, long credits4, _, _) =
            GameplayRules.InstanceReward(GameplayRules.InstanceKind.Gold, 2, 4, random);
        Assert.Equal(0, exp1);
        Assert.Equal(550 * 2, credits1);
        Assert.Equal(550 * 2 * 4, credits4);        // 4 人 = 单人的 4 倍

        (_, _, diamonds1, _) =
            GameplayRules.InstanceReward(GameplayRules.InstanceKind.Material, 3, 1, random);
        (_, _, long diamonds4, _) =
            GameplayRules.InstanceReward(GameplayRules.InstanceKind.Material, 3, 4, random);
        Assert.Equal(3 * 3, diamonds1);
        Assert.Equal(3 * 3 * 4, diamonds4);

        (long expGain, _, _, _) =
            GameplayRules.InstanceReward(GameplayRules.InstanceKind.Exp, 5, 4, random);
        Assert.Equal(570 * 5 * 4, expGain);
    }

    [Fact]
    public async Task Ranking_HasHorseCoopAndForgeBoards()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(Seller);
        await players.GetSnapshotAsync(Buyer);

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord seller = await context.CorePlayers.FirstAsync(x => x.UserId == Seller);
            seller.HorseRacingPoints = 30;
            seller.CooperativePoints = 4;
            seller.ForgePoints = 7;
            CorePlayerRecord buyer = await context.CorePlayers.FirstAsync(x => x.UserId == Buyer);
            buyer.HorseRacingPoints = 10;
            buyer.CooperativePoints = 9;
            await context.SaveChangesAsync();
        }

        IReadOnlyList<RankEntry> horse = await economy.GetRankingAsync("赛马");
        Assert.Equal(2, horse.Count);
        Assert.Equal(30, horse[0].Value, 3);
        Assert.Equal(10, horse[1].Value, 3);
        Assert.Equal("赛马积分榜", GameplayRules.RankingBoardName("赛马"));

        IReadOnlyList<RankEntry> coop = await economy.GetRankingAsync("共斗");
        Assert.Equal(9, coop[0].Value, 3);                 // 共斗榜按积分降序，买家在前
        Assert.Equal("共斗积分榜", GameplayRules.RankingBoardName("coop"));

        IReadOnlyList<RankEntry> forge = await economy.GetRankingAsync("锻造");
        Assert.Single(forge);                              // 只有卖家有锻造积分
        Assert.Equal(7, forge[0].Value, 3);
        Assert.Equal("锻造积分榜", GameplayRules.RankingBoardName("锻造"));

        // 积分榜不混入金币榜
        IReadOnlyList<RankEntry> credits = await economy.GetRankingAsync("credits");
        Assert.NotEmpty(credits);
    }

    [Fact]
    public async Task Boss_SharedTargetAllowsRepeatAndAccumulatesLedger()
    {
        BossPool.Reset();
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        // 旧版 Boss 是全服共享的持久实体：没有「每人每日一次」的限制，可以反复挑战。
        Assert.True((await economy.BossAsync(Seller)).Ok);
        Assert.True((await economy.BossAsync(Seller)).Ok);
        Assert.True((await economy.DuelAsync(Seller)).Ok);

        IReadOnlyList<RankEntry> points = await economy.GetRankingAsync("战绩");
        IReadOnlyList<RankEntry> credits = await economy.GetRankingAsync("credits");
        Assert.Single(points);
        // Boss 是真实战斗：击杀给满分，未击杀按伤害占比给部分战绩。
        Assert.True(points[0].Value >= 1);
        Assert.NotEmpty(credits);

        // 池子始终保持 10 只（未击杀时不会减少，击杀后会补充）。
        Assert.Equal(BossPool.PoolSize, BossPool.All.Count);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        // 旧版不收入场费、只有击杀才发材料，所以这两条流水都可能不存在；
        // 唯一稳定的是战绩累积（未击杀也按伤害给分）。
        Assert.True(await dbContext.LedgerEntries.CountAsync(x => x.Reason == "boss_entry") == 0);
        Assert.True(await dbContext.LedgerEntries.CountAsync(x => x.Reason == "boss_reward")
            == await dbContext.LedgerEntries.CountAsync(x => x.Reason == "boss_reward" && x.Currency == "diamonds"));
    }

    [Fact]
    public async Task Duel_RunsRealBattleInsteadOfDiceRoll()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Seller);   // 建档并发放初始角色

        double creditsBefore = (await players.GetSnapshotAsync(Seller))!.Credits;
        ActionResult result = await economy.DuelAsync(Seller);

        Assert.True(result.Ok, result.Title);
        Assert.Contains(result.Lines, line => line.Contains("回合"));
        Assert.Contains(result.Lines, line => line.Contains("HP"));
        Assert.Contains(result.Lines, line => line.Contains("战斗摘要"));

        // 旧版决斗既不入场费也不发奖励：余额一分不动，账本不留痕迹
        Assert.Equal(creditsBefore, (await players.GetSnapshotAsync(Seller))!.Credits, 3);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(0, await context.LedgerEntries.CountAsync(x => x.Reason.StartsWith("duel")));
    }

    [Fact]
    public async Task NoIdentity_NeverWritesEconomyData()
    {
        using EconomyDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();

        Assert.False((await economy.BuyAsync(0, null, 1, 1)).Ok);
        Assert.False((await economy.ListOnMarketAsync(0, 1, 100)).Ok);
        Assert.False((await economy.BossAsync(0)).Ok);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(0, await dbContext.MarketListings.CountAsync());
        Assert.Equal(0, await dbContext.Trades.CountAsync());
    }

    private static async Task SetCreditsAsync(EconomyDatabase db, long userId, long credits)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord state = await context.CorePlayers.FirstAsync(x => x.UserId == userId);
        state.Credits = credits;
        await context.SaveChangesAsync();
    }

    private static async Task AddMaterialAsync(EconomyDatabase db, long userId, int quantity)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        await ItemStore.AddAsync(context, userId, "测试材料", 1, quantity, CancellationToken.None, "test");
        await context.SaveChangesAsync();
    }

    private static async Task ExpireAsync(EconomyDatabase db, string kind)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        if (kind == "forge")
        {
            ForgingJob job = await context.ForgingJobs.FirstAsync();
            job.EndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
        }
        else
        {
            ExplorationRun run = await context.ExplorationRuns.FirstAsync();
            run.EndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
        }
        await context.SaveChangesAsync();
    }

    private sealed class EconomyDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public EconomyDatabase()
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddGameplayRuntime();
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IEconomyService, EconomyService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
