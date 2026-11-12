using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 定向报价单（旧版 <c>Offer</c>）：创建草稿 → 双方加物品 → 发送 → 对方接受 / 拒绝。
/// <para>与公开挂单的区别是不涉及金币：发起方托管「我给的」，接收方那侧只在成交时校验。</para>
/// </summary>
public sealed class OfferFlowTests
{
    private const long Me = 42;
    private const long Rival = 77;

    [Fact]
    public async Task Draft_AddBothSidesThenSendAndAccept()
    {
        using OfferDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreatePlayerAsync(Me, "甲方");
        await db.CreatePlayerAsync(Rival, "乙方");
        await db.AddItemAsync(Me, "我的剑", 2);
        await db.AddItemAsync(Rival, "你的盾", 1);

        ActionResult created = await economy.CreateOfferDraftAsync(Me, "乙方");
        Assert.True(created.Ok, created.Title);
        long offerId = await db.SingleOfferIdAsync();

        // 草稿里加自己的一件、要对方的一件
        Assert.True((await economy.AddOfferItemsAsync(Me, offerId, opposite: false, [1])).Ok);
        Assert.True((await economy.AddOfferItemsAsync(Me, offerId, opposite: true, [1])).Ok);
        Assert.Empty(await db.ItemsAsync(Me));                 // 我方物品已托管

        OfferDetail detail = (await economy.GetOfferDetailAsync(Me, offerId))!;
        Assert.Equal(EconomyService.OfferDraft, detail.Status);
        Assert.Single(detail.Giving);
        Assert.Single(detail.Wanted);
        Assert.Equal("我的剑", detail.Giving[0].Name);
        Assert.Equal("你的盾", detail.Wanted[0].Name);

        // 草稿阶段对方看不到
        Assert.Null(await economy.GetOfferDetailAsync(Rival, offerId));

        Assert.True((await economy.SendOfferAsync(Me, offerId)).Ok);
        Assert.Equal(EconomyService.OfferSent, (await economy.GetOfferDetailAsync(Rival, offerId))!.Status);

        // 接收方接受：双边互换，不涉及金币
        double richBefore = await db.CreditsAsync(Rival);
        ActionResult accepted = await economy.AcceptOfferAsync(Rival, (int)offerId);
        Assert.True(accepted.Ok, accepted.Title);
        Assert.Equal(richBefore, await db.CreditsAsync(Rival), 3);
        Assert.Equal("你的盾", (await db.ItemsAsync(Me)).Single().Name);
        Assert.Equal("我的剑", (await db.ItemsAsync(Rival)).Single().Name);
        Assert.Equal(EconomyService.OfferCompleted, (await economy.GetOfferDetailAsync(Me, offerId))!.Status);
    }

    [Fact]
    public async Task Reject_ReturnsEscrowedItems()
    {
        using OfferDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreatePlayerAsync(Me, "甲方");
        await db.CreatePlayerAsync(Rival, "乙方");
        await db.AddItemAsync(Me, "我的剑", 2);

        await economy.CreateOfferDraftAsync(Me, "乙方");
        long offerId = await db.SingleOfferIdAsync();
        Assert.True((await economy.AddOfferItemsAsync(Me, offerId, false, [1])).Ok);
        Assert.True((await economy.SendOfferAsync(Me, offerId)).Ok);

        ActionResult rejected = await economy.RejectOfferAsync(Rival, offerId);
        Assert.True(rejected.Ok, rejected.Title);
        Assert.Equal("我的剑", (await db.ItemsAsync(Me)).Single().Name);   // 退回背包
        Assert.Equal(EconomyService.OfferRejected, (await economy.GetOfferDetailAsync(Me, offerId))!.Status);

        // 已经拒绝的报价不能反过来接受
        Assert.Contains("不能接受", (await economy.AcceptOfferAsync(Rival, (int)offerId)).Title);
    }

    [Fact]
    public async Task RemoveAndWithdraw_RestoreItems()
    {
        using OfferDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreatePlayerAsync(Me, "甲方");
        await db.CreatePlayerAsync(Rival, "乙方");
        await db.AddItemAsync(Me, "甲剑", 2);
        await db.AddItemAsync(Me, "甲弓", 2);

        await economy.CreateOfferDraftAsync(Me, "乙方");
        long offerId = await db.SingleOfferIdAsync();
        Assert.True((await economy.AddOfferItemsAsync(Me, offerId, false, [1, 2])).Ok);
        Assert.Empty(await db.ItemsAsync(Me));

        // 移除第一件（编号来自 /查报价 的「发起方给」栏）
        Assert.True((await economy.RemoveOfferItemsAsync(Me, offerId, false, [1])).Ok);
        Assert.Single(await db.ItemsAsync(Me));

        // 撤回草稿：剩下的也退回
        ActionResult withdrawn = await economy.CancelOfferAsync(Me, (int)offerId);
        Assert.True(withdrawn.Ok, withdrawn.Title);
        Assert.Equal(2, (await db.ItemsAsync(Me)).Count);
        Assert.Equal(EconomyService.OfferWithdrawn, (await economy.GetOfferDetailAsync(Me, offerId))!.Status);
    }

    [Fact]
    public async Task Guard_RulesForDraftAndTarget()
    {
        using OfferDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreatePlayerAsync(Me, "甲方");
        await db.CreatePlayerAsync(Rival, "乙方");
        await db.AddItemAsync(Me, "甲剑", 2);

        Assert.Contains("不能和自己报价", (await economy.CreateOfferDraftAsync(Me, "甲方")).Title);
        Assert.Contains("找不到对方", (await economy.CreateOfferDraftAsync(Me, "查无此人")).Title);

        await economy.CreateOfferDraftAsync(Me, "乙方");
        long offerId = await db.SingleOfferIdAsync();

        // 同一对象不能并存两份未完成报价
        Assert.Contains("已经有", (await economy.CreateOfferDraftAsync(Me, "乙方")).Title);

        // 没有物品不能发送
        Assert.Contains("还没放", (await economy.SendOfferAsync(Me, offerId)).Title);

        // 别人不能往我的草稿里加东西
        Assert.Contains("不是你的报价单",
            (await economy.AddOfferItemsAsync(Rival, offerId, false, [1])).Title);

        // 发出后不能再改内容
        Assert.True((await economy.AddOfferItemsAsync(Me, offerId, false, [1])).Ok);
        Assert.True((await economy.SendOfferAsync(Me, offerId)).Ok);
        Assert.Contains("不能再改内容",
            (await economy.AddOfferItemsAsync(Me, offerId, false, [1])).Title);
    }

    [Fact]
    public async Task Accept_FailsWhenWantedItemLeftTheBag()
    {
        using OfferDatabase db = new();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreatePlayerAsync(Me, "甲方");
        await db.CreatePlayerAsync(Rival, "乙方");
        await db.AddItemAsync(Me, "我的剑", 2);
        await db.AddItemAsync(Rival, "你的盾", 1);

        await economy.CreateOfferDraftAsync(Me, "乙方");
        long offerId = await db.SingleOfferIdAsync();
        Assert.True((await economy.AddOfferItemsAsync(Me, offerId, false, [1])).Ok);
        Assert.True((await economy.AddOfferItemsAsync(Me, offerId, true, [1])).Ok);
        Assert.True((await economy.SendOfferAsync(Me, offerId)).Ok);

        // 接收方把「要给对方的那件」拿去挂市场了
        await db.MoveToEscrowAsync(Rival, "你的盾");
        ActionResult accepted = await economy.AcceptOfferAsync(Rival, (int)offerId);
        Assert.False(accepted.Ok);
        Assert.Contains("不在背包里", accepted.Title);
    }

    private sealed class OfferDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public OfferDatabase()
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
            services.AddSingleton<ApiKeyAuthenticator>();
            services.AddGameplayRuntime();
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IEconomyService, EconomyService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task CreatePlayerAsync(long userId, string nickname)
        {
            using (IServiceScope scope = Provider.CreateScope())
            {
                BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
                context.Users.Add(new OshimaBot.Data.Entities.User
                {
                    Id = userId, Handle = $"h{userId}", Nickname = nickname
                });
                await context.SaveChangesAsync();
            }
            await Get<IPlayerService>().GetSnapshotAsync(userId);
        }

        public async Task AddItemAsync(long userId, string name, int quantity)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await ItemStore.AddAsync(context, userId, name, 1, quantity, CancellationToken.None, "test");
            await context.SaveChangesAsync();
        }

        public async Task<List<PlayerItem>> ItemsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return await ItemStore.ListAsync(context, userId, CancellationToken.None);
        }

        public async Task<double> CreditsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return (await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId)).Credits;
        }

        public async Task<long> SingleOfferIdAsync()
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return await context.Trades.OrderByDescending(x => x.Id).Select(x => x.Id).FirstAsync();
        }

        /// <summary>模拟「把要交易的物品挪作他用」：直接改归属到托管账号。</summary>
        public async Task MoveToEscrowAsync(long userId, string name)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            List<PlayerItem> items = await ItemStore.ListAsync(context, userId, CancellationToken.None);
            PlayerItem item = items.First(x => x.Name == name);
            item.UserId = GameplayRules.EscrowUserId;
            ItemStore.Sync(item);
            await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
