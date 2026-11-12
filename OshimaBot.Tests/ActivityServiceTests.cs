using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 活动（旧版 GetEvents / PerformEvent / ClaimEventPrize）与运营公告。
/// <para>v3 内容包没有活动数据，窗口规则写在 <see cref="ActivityCatalog"/> 里；
/// 进度复用既有的 <c>ActivityProgress</c> 表（<c>activity:{key}</c>）。</para>
/// </summary>
public sealed class ActivityServiceTests
{
    private const long Player = 42;

    [Fact]
    public async Task List_ShowsPhasePerWindow()
    {
        // 2026-11-12 是周四 20:00（本地），周末活动在周六 04:00 开——32 小时后，算「未开放」
        using ActivityDatabase db = new();
        IActivityService activities = db.Get<IActivityService>();
        await db.CreatePlayerAsync(Player);

        IReadOnlyList<ActivityEntry> rows = await activities.ListAsync(Player);
        Assert.Equal(ActivityCatalog.Activities.Count, rows.Count);
        Assert.Equal("进行中", rows[0].Phase);          // 旅人试炼：长期
        Assert.Equal("未开放", rows[1].Phase);          // 周末疾行：还没到
        Assert.Equal("未开放", rows[2].Phase);          // 双旦活动：12-20 才开
        Assert.Equal("长期开放", rows[0].Window);
    }

    [Fact]
    public async Task List_WeekendActivityRunsOnSaturday()
    {
        // 2026-11-14 12:00 本地（周六）→ 周末活动进行中；周一之后又回到未开放
        using ActivityDatabase db = new(new DateTime(2026, 11, 14, 4, 0, 0, DateTimeKind.Utc));
        IActivityService activities = db.Get<IActivityService>();
        await db.CreatePlayerAsync(Player);

        IReadOnlyList<ActivityEntry> rows = await activities.ListAsync(Player);
        Assert.Equal("进行中", rows[1].Phase);
    }

    [Fact]
    public async Task Perform_ChargesCreditsAndClaimGivesItems()
    {
        using ActivityDatabase db = new();
        IActivityService activities = db.Get<IActivityService>();
        await db.CreatePlayerAsync(Player);

        // 旅人试炼任务 2「以币换印」：每次 15000 金币，做 2 次
        double before = await db.CreditsAsync(Player);
        Assert.True(before < 15_000);
        Assert.Contains("金币不足", (await activities.PerformAsync(Player, 1, 2, 0)).Title);

        await db.SetCreditsAsync(Player, 40_000);
        Assert.True((await activities.PerformAsync(Player, 1, 2, 0)).Ok);
        Assert.Equal(25_000, await db.CreditsAsync(Player), 3);

        // 做满之前不能领
        Assert.False((await activities.ClaimAsync(Player, 1, 2)).Ok);
        // 做满之后也不能再多做
        Assert.True((await activities.PerformAsync(Player, 1, 2, 0)).Ok);
        ActionResult capped = await activities.PerformAsync(Player, 1, 2, 0);
        Assert.False(capped.Ok);
        Assert.Contains("做满", capped.Title);

        ActionResult claimed = await activities.ClaimAsync(Player, 1, 2);
        Assert.True(claimed.Ok, claimed.Title);
        Assert.Contains(await db.ItemNamesAsync(Player), name => name == "升华之印");

        // 一个任务只能领一次
        ActionResult again = await activities.ClaimAsync(Player, 1, 2);
        Assert.False(again.Ok);
        Assert.Contains("已经领过", again.Title);

        ActivityDetail detail = (await activities.InfoAsync(Player, 1))!;
        Assert.True(detail.Quests[1].Claimed);
        Assert.False(detail.AnyReady);
    }

    [Fact]
    public async Task Perform_ItemsCostNeedsBagIndex()
    {
        using ActivityDatabase db = new();
        IActivityService activities = db.Get<IActivityService>();
        await db.CreatePlayerAsync(Player);
        await db.AddItemAsync(Player, "测试矿石", 1, 3);

        // 不给 @编号 直接拒绝
        Assert.Contains("没有指定", (await activities.PerformAsync(Player, 1, 1, 0)).Title);

        ActionResult done = await activities.PerformAsync(Player, 1, 1, 1);
        Assert.True(done.Ok, done.Title);
        Assert.Empty(await db.ItemNamesAsync(Player));      // 3 件全被收走
        Assert.Contains("领取", string.Join(" ", done.Lines));
        Assert.True((await activities.ClaimAsync(Player, 1, 1)).Ok);
        Assert.Equal(3000 + GameplayRules.StartingCredits, await db.CreditsAsync(Player), 3);
    }

    [Fact]
    public async Task Perform_RejectsClosedActivity()
    {
        using ActivityDatabase db = new();
        IActivityService activities = db.Get<IActivityService>();
        await db.CreatePlayerAsync(Player);
        await db.SetCreditsAsync(Player, 50_000);

        // 周末活动周四还没开
        ActionResult closed = await activities.PerformAsync(Player, 2, 1, 0);
        Assert.False(closed.Ok);
        Assert.Contains("不能做任务", closed.Title);
        Assert.Equal(50_000, await db.CreditsAsync(Player), 3);
    }

    [Fact]
    public async Task Notice_WritesAppConfigAndValidates()
    {
        using ActivityDatabase db = new();
        IActivityService activities = db.Get<IActivityService>();
        IPlayerService players = db.Get<IPlayerService>();
        await db.CreatePlayerAsync(Player);

        Assert.Equal(GameplayRules.DefaultNotice, await players.GetNoticeAsync());

        Assert.True((await activities.SetNoticeAsync("周末双倍经验，记得上线")).Ok);
        Assert.Equal("周末双倍经验，记得上线", await players.GetNoticeAsync());

        // 站外引流 / 空公告都拒绝
        Assert.False((await activities.SetNoticeAsync("加群 12345678 领福利")).Ok);
        Assert.False((await activities.SetNoticeAsync("   ")).Ok);
        Assert.Equal("周末双倍经验，记得上线", await players.GetNoticeAsync());
    }

    private sealed class ActivityDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public FakeClock Clock { get; }

        public ActivityDatabase(DateTime? nowUtc = null)
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            // 默认 2026-11-12 20:00 本地（周四中午 UTC），周末活动与双旦活动都还没开。
            Clock = new FakeClock(nowUtc ?? new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<ApiKeyAuthenticator>();
            services.AddGameplayRuntime(Clock);
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IActivityService, ActivityService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task CreatePlayerAsync(long userId)
        {
            await Get<IPlayerService>().GetSnapshotAsync(userId);
        }

        public async Task<double> CreditsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId);
            return wallet.Credits;
        }

        public async Task SetCreditsAsync(long userId, double credits)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == userId);
            wallet.Credits = credits;
            await context.SaveChangesAsync();
        }

        public async Task AddItemAsync(long userId, string name, int quality, int quantity)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await ItemStore.AddAsync(context, userId, name, quality, quantity, CancellationToken.None, "test");
            await context.SaveChangesAsync();
        }

        public async Task<List<string>> ItemNamesAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            List<PlayerItem> items = await ItemStore.ListAsync(context, userId, CancellationToken.None);
            return [.. items.Select(x => x.Name)];
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
