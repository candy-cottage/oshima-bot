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
/// 世界探索（旧版 ExploreRegion / SettleExplore）。
/// <para>口径：探索许可是**虚物品+配额**（每日 60，11–13 点 / 17–19 点上线各 +30，4:00 刷新）；
/// 消耗 = 人数 × 星级；2 分钟后可结算；遭遇战走真实战斗。</para>
/// </summary>
public sealed class ExploreServiceTests
{
    private const long Player = 42;

    [Fact]
    public async Task Quota_StartsAtDailyBaseAndGrantsMealsOnce()
    {
        // 2026-11-12 20:00 本地（周四晚）——晚餐窗口 17–19 已过，只拿基础 60
        using ExploreDatabase db = new(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
        IExploreService explore = db.Get<IExploreService>();

        ExploreQuota quota = await explore.RefreshQuotaAsync(Player);
        Assert.Equal(ExploreRules.DailyQuota, quota.Left);
        Assert.Equal(GameplayRules.StoreDay(db.Clock.UtcNow), quota.Day);
        Assert.False(quota.LunchClaimed);

        // 拨到第二天中午：跨日重置 + 午餐 +30
        db.Clock.Advance(TimeSpan.FromHours(16));   // → 11-13 12:00 本地（次日 04:00 UTC）
        ExploreQuota next = await explore.RefreshQuotaAsync(Player);
        Assert.True(next.LunchClaimed);
        Assert.Equal(ExploreRules.DailyQuota + ExploreRules.MealBonus, next.Left);
        Assert.Equal(GameplayRules.StoreDay(db.Clock.UtcNow), next.Day);

        // 同一天再刷不会重复发
        ExploreQuota again = await explore.RefreshQuotaAsync(Player);
        Assert.Equal(next.Left, again.Left);
    }

    [Fact]
    public async Task Start_ChargesCharactersTimesStars()
    {
        using ExploreDatabase db = new();
        IExploreService explore = db.Get<IExploreService>();
        await db.CreatePlayerAsync(Player);

        // 银辉城是 5 星，1 人 = 5 许可
        ActionResult one = await explore.StartAsync(Player, 1, [1], useSquad: false);
        Assert.True(one.Ok, one.Title);
        Assert.Contains("5", one.Status);

        (ExploreQuota quota, IReadOnlyList<ExploreRunView> runs) = await explore.InfoAsync(Player);
        Assert.Equal(ExploreRules.DailyQuota - 5, quota.Left);
        ExploreRunView run = Assert.Single(runs);
        Assert.Equal("银辉城", run.RegionName);
        Assert.Equal(5, run.Cost);
        Assert.False(run.Ready);

        // 主城不能探索
        Assert.Contains("铎京城不能探索", (await explore.StartAsync(Player, 0, [1], false)).Title);

        // 越界地区
        Assert.Contains("没有编号", (await explore.StartAsync(Player, 99, [1], false)).Title);

        // 同一角色不能并行
        ActionResult again = await explore.StartAsync(Player, 1, [1], useSquad: false);
        Assert.False(again.Ok);
        Assert.Contains("正在探索", again.Title);
    }

    [Fact]
    public async Task Start_RejectsWhenQuotaExhausted()
    {
        using ExploreDatabase db = new();
        IExploreService explore = db.Get<IExploreService>();
        await db.CreatePlayerAsync(Player);

        // 5 星 × 1 人 = 5，60 许可够跑 12 次（每跑一次就结算，角色才不会被锁）
        for (int i = 0; i < 12; i++)
        {
            Assert.True((await explore.StartAsync(Player, 1, [1], useSquad: false)).Ok);
            db.Clock.Advance(TimeSpan.FromMinutes(ExploreRules.DurationMinutes));
            Assert.True((await explore.ClaimAsync(Player)).Result.Ok);
        }

        (ExploreQuota quota, _) = await explore.InfoAsync(Player);
        Assert.Equal(0, quota.Left);
        ActionResult denied = await explore.StartAsync(Player, 1, [1], useSquad: false);
        Assert.False(denied.Ok);
        Assert.Contains("探索许可不足", denied.Title);
    }

    [Fact]
    public async Task Claim_NeedsTwoMinutesThenResolves()
    {
        // 固定随机 0.5：5 星地区落在「遭遇战」（General 0.10 → Nothing 0.25 → Fight 0.95）
        using ExploreDatabase db = new(random: new FixedDoubleRandomSource(0.5));
        IExploreService explore = db.Get<IExploreService>();
        await db.CreatePlayerAsync(Player);

        Assert.True((await explore.StartAsync(Player, 1, [1], false)).Ok);

        BattleReport early = await explore.ClaimAsync(Player);
        Assert.False(early.Result.Ok);
        Assert.Contains("还没结束", early.Result.Title);
        Assert.Single((await explore.InfoAsync(Player)).Runs);   // 没被清掉

        db.Clock.Advance(TimeSpan.FromMinutes(ExploreRules.DurationMinutes));
        BattleReport done = await explore.ClaimAsync(Player);
        Assert.True(done.Result.Ok, done.Result.Title);
        Assert.NotEmpty(done.Log);                               // 真的打了一场
        Assert.Contains("遭遇战", string.Join(" ", done.Result.Lines));
        Assert.Empty((await explore.InfoAsync(Player)).Runs);    // 运行态已清
    }

    [Fact]
    public async Task Claim_NothingHappensRollConsumesRun()
    {
        // 固定 0.20：落在「空手」（5 星：General 累积 0.10 → Nothing 累积 0.25 → Fight 0.95）
        using ExploreDatabase db = new(random: new FixedDoubleRandomSource(0.20));
        IExploreService explore = db.Get<IExploreService>();
        await db.CreatePlayerAsync(Player);

        Assert.True((await explore.StartAsync(Player, 1, [1], false)).Ok);
        db.Clock.Advance(TimeSpan.FromMinutes(ExploreRules.DurationMinutes));

        BattleReport done = await explore.ClaimAsync(Player);
        Assert.True(done.Result.Ok, done.Result.Title);
        Assert.Contains("空手", string.Join(" ", done.Result.Lines));
        Assert.Empty(done.Log);
        Assert.Empty((await explore.InfoAsync(Player)).Runs);
    }

    [Fact]
    public async Task Claim_WithoutRunsGivesGuidance()
    {
        using ExploreDatabase db = new();
        IExploreService explore = db.Get<IExploreService>();
        await db.CreatePlayerAsync(Player);

        BattleReport report = await explore.ClaimAsync(Player);
        Assert.False(report.Result.Ok);
        Assert.Contains("没有进行中的探索", report.Result.Title);
    }

    private sealed class ExploreDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public FakeClock Clock { get; }

        public ExploreDatabase(DateTime? nowUtc = null, IRandomSource? random = null)
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            // 默认 2026-11-12 20:00 本地（周四），避开午餐/晚餐窗口，配额从 60 起算。
            Clock = new FakeClock(nowUtc ?? new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<ApiKeyAuthenticator>();
            services.AddGameplayRuntime(Clock, random);
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IExploreService, ExploreService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task CreatePlayerAsync(long userId)
        {
            await Get<IPlayerService>().GetSnapshotAsync(userId);
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }

    /// <summary>固定 <c>NextDouble</c> 的随机源，用来卡探索结果的分支。</summary>
    private sealed class FixedDoubleRandomSource(double value) : IRandomSource
    {
        public int Next(int maxExclusive) => 0;

        public int Next(int minInclusive, int maxExclusive) => minInclusive;

        public double NextDouble() => value;
    }
}
