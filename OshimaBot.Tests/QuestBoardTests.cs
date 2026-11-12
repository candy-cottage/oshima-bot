using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 任务板（旧版 任务列表 / 做任务 / 任务信息 / 任务结算）。
/// <para>三类任务各自的口径：持续任务等时长、即时任务当场发奖、渐进任务提交物品；
/// 非渐进任务同时只能进行一个（同旧版）。</para>
/// </summary>
public sealed class QuestBoardTests
{
    private const long Player = 42;

    [Fact]
    public async Task List_ShowsAllQuestsAsAvailable()
    {
        using QuestDatabase db = new();
        IQuestBoardService quests = db.Get<IQuestBoardService>();
        await db.CreatePlayerAsync(Player);

        IReadOnlyList<QuestBoardEntry> entries = await quests.ListAsync(Player);
        Assert.Equal(QuestBoardRules.Quests.Count, entries.Count);
        Assert.All(entries, entry => Assert.Equal(QuestBoardRules.NotStarted, entry.State));
        Assert.Empty(await quests.WorkingAsync(Player));
    }

    [Fact]
    public async Task Continuous_WaitsThenSettles()
    {
        using QuestDatabase db = new();
        IQuestBoardService quests = db.Get<IQuestBoardService>();
        await db.CreatePlayerAsync(Player);
        double before = await db.CreditsAsync(Player);

        ActionResult accepted = await quests.AcceptAsync(Player, 1, 0);
        Assert.True(accepted.Ok, accepted.Title);

        // 进行中：任务信息能看到，结算会被拒
        QuestBoardEntry running = Assert.Single(await quests.WorkingAsync(Player));
        Assert.Equal("巡逻铎京", running.Name);
        Assert.True(running.RemainingSeconds > 0);
        Assert.Contains("还没完成", (await quests.SettleAsync(Player)).Title);

        // 非渐进任务同时只能一个
        Assert.Contains("正在进行任务", (await quests.AcceptAsync(Player, 2, 0)).Title);

        db.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True((await quests.WorkingAsync(Player)).Single().Completed);
        ActionResult settled = await quests.SettleAsync(Player);
        Assert.True(settled.Ok, settled.Title);
        Assert.Equal(before + 800, await db.CreditsAsync(Player), 3);
        Assert.Equal(1, await db.MaterialsAsync(Player), 3);

        // 已结算的任务不能再接
        Assert.Contains("已经结算", (await quests.AcceptAsync(Player, 1, 0)).Title);
        Assert.Empty(await quests.WorkingAsync(Player));
    }

    [Fact]
    public async Task Immediate_ExecutesAtOnce()
    {
        using QuestDatabase db = new();
        IQuestBoardService quests = db.Get<IQuestBoardService>();
        await db.CreatePlayerAsync(Player);
        double before = await db.CreditsAsync(Player);

        // 清理下水道：先付 200，直接拿 500 + 1 钻石
        ActionResult done = await quests.AcceptAsync(Player, 3, 0);
        Assert.True(done.Ok, done.Title);
        Assert.Equal(before - 200 + 500, await db.CreditsAsync(Player), 3);
        Assert.Equal(1, await db.MaterialsAsync(Player), 3);

        // 即时任务不占「进行中」的位置
        Assert.Empty(await quests.WorkingAsync(Player));
        Assert.True((await quests.AcceptAsync(Player, 1, 0)).Ok);
    }

    [Fact]
    public async Task Progressive_NeedsThreeItems()
    {
        using QuestDatabase db = new();
        IQuestBoardService quests = db.Get<IQuestBoardService>();
        await db.CreatePlayerAsync(Player);
        await db.AddItemAsync(Player, "测试矿石", 3);

        // 不给物品
        Assert.Contains("没有指定", (await quests.AcceptAsync(Player, 4, 0)).Title);

        Assert.Contains("1/3", (await quests.AcceptAsync(Player, 4, 1)).Status);
        Assert.Contains("2/3", (await quests.AcceptAsync(Player, 4, 1)).Status);
        Assert.Equal(1, (await db.ItemsAsync(Player)).Single().Quantity);

        ActionResult done = await quests.AcceptAsync(Player, 4, 1);
        Assert.Contains("任务已达成", done.Title);

        double before = await db.CreditsAsync(Player);
        Assert.True((await quests.SettleAsync(Player)).Ok);
        Assert.Equal(before + 1500, await db.CreditsAsync(Player), 3);
        Assert.Empty(await db.ItemsAsync(Player));
    }

    [Fact]
    public async Task Accept_RejectsUnknownIndexAndLockedItem()
    {
        using QuestDatabase db = new();
        IQuestBoardService quests = db.Get<IQuestBoardService>();
        await db.CreatePlayerAsync(Player);

        Assert.Contains("不存在", (await quests.AcceptAsync(Player, 99, 0)).Title);

        await db.AddItemAsync(Player, "测试矿石", 1);
        await db.SetLockAsync(Player, "测试矿石");
        Assert.Contains("锁定", (await quests.AcceptAsync(Player, 4, 1)).Title);
    }

    private sealed class QuestDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public FakeClock Clock { get; }

        public QuestDatabase()
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            Clock = new FakeClock(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<ApiKeyAuthenticator>();
            services.AddGameplayRuntime(Clock);
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IQuestBoardService, QuestBoardService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task CreatePlayerAsync(long userId)
            => await Get<IPlayerService>().GetSnapshotAsync(userId);

        public async Task<double> CreditsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId);
            return wallet.Credits;
        }

        public async Task<double> MaterialsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId);
            return wallet.Materials;
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

        public async Task SetLockAsync(long userId, string name)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            List<PlayerItem> items = await ItemStore.ListAsync(context, userId, CancellationToken.None);
            PlayerItem item = items.First(x => x.Name == name);
            item.Locked = true;
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
