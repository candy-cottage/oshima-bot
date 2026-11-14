using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 改名提交的频率限制（<see cref="RenameThrottle"/>）：被拒冷却 + 滚动窗口配额。
/// <para>这两条不是玩法，是为了让审核队列不被刷爆：此前只有「同一目标只能有一份待审」，
/// 拒绝后可以立刻重提，管理员会被无限循环刷。</para>
/// </summary>
public sealed class RenameThrottleTests
{
    private const long Player = 11;
    private static readonly DateTime StartUtc = new(2026, 11, 15, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RejectedRename_IsCooledDownBeforeTheNextAttempt()
    {
        using ThrottleDatabase db = new(StartUtc);
        IPlayerService players = db.Get<IPlayerService>();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Player, "旧名字");

        Assert.True((await players.RenameAsync(Player, "夜航船")).Ok);
        Assert.True((await clubs.RejectRenameAsync(Player, 1)).Ok);

        // 冷却期内：换名字也不行
        ActionResult blocked = await players.RenameAsync(Player, "另一个名字");
        Assert.False(blocked.Ok);
        Assert.Contains("刚被拒绝", blocked.Title);
        Assert.Contains("还要等", string.Join("", blocked.Lines));
        Assert.Empty(await players.MyRenameRequestsAsync(Player));

        // 差一分钟还在冷却
        db.Clock.Advance(TimeSpan.FromHours(GameplayRules.RenameRejectCooldownHours) - TimeSpan.FromMinutes(1));
        Assert.False((await players.RenameAsync(Player, "另一个名字")).Ok);

        // 过了冷却就能提
        db.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True((await players.RenameAsync(Player, "另一个名字")).Ok);
    }

    [Fact]
    public async Task RenameQuota_CapsSubmissionsInsideTheWindow()
    {
        using ThrottleDatabase db = new(StartUtc);
        IPlayerService players = db.Get<IPlayerService>();
        await db.CreatePlayerAsync(Player, "旧名字");

        // 已经提交过 3 次（窗口内、不同目标），第 4 次要被拦
        await db.SeedHistoryAsync(Player, count: GameplayRules.RenameQuotaPerWindow, atUtc: StartUtc.AddHours(-1));

        ActionResult blocked = await players.RenameAsync(Player, "夜航船");
        Assert.False(blocked.Ok);
        Assert.Contains("最多", blocked.Title);
        Assert.Contains("已提交 3 次", string.Join("", blocked.Lines));

        // 窗口滑过去以后恢复
        db.Clock.Advance(TimeSpan.FromHours(GameplayRules.RenameQuotaWindowHours) + TimeSpan.FromMinutes(1));
        Assert.True((await players.RenameAsync(Player, "夜航船")).Ok);
    }

    [Fact]
    public async Task ClubRename_SharesTheSameBudgetAsCharacterRename()
    {
        using ThrottleDatabase db = new(StartUtc);
        IClubService clubs = db.Get<IClubService>();
        IPlayerService players = db.Get<IPlayerService>();
        await db.CreatePlayerAsync(Player, "旧名字");

        Assert.True((await clubs.CreateAsync(Player, "ABC", isPublic: true)).Ok);

        // 两次角色改名都批准（批准不进冷却），第三次给社团名，第四次换回角色名——被配额拦。
        Assert.True((await players.RenameAsync(Player, "夜航船")).Ok);
        Assert.True((await clubs.ApproveRenameAsync(Player, 1)).Ok);
        Assert.True((await players.RenameAsync(Player, "夜航船2")).Ok);
        Assert.True((await clubs.ApproveRenameAsync(Player, 1)).Ok);

        Assert.True((await clubs.UpdateSettingsAsync(Player, "名称", "夜航船社")).Ok, "第 3 次应仍在配额内");

        // 第 4 次：社团那边还有待审，所以换回角色名来验证「配额是跨目标的」
        ActionResult blocked = await players.RenameAsync(Player, "夜航船3");
        Assert.False(blocked.Ok);
        Assert.Contains("最多", blocked.Title);
    }

    [Fact]
    public async Task ApprovalDoesNotStartACooldown()
    {
        using ThrottleDatabase db = new(StartUtc);
        IPlayerService players = db.Get<IPlayerService>();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Player, "旧名字");

        Assert.True((await players.RenameAsync(Player, "夜航船")).Ok);
        Assert.True((await clubs.ApproveRenameAsync(Player, 1)).Ok);
        Assert.Equal("夜航船", (await players.GetCharactersAsync(Player)).Single().Name);

        // 批准后可以马上再提（只是占配额，不进冷却）
        Assert.True((await players.RenameAsync(Player, "夜航船2")).Ok);
    }

    /// <summary>带可拨时钟的内存库；改名限制全部走 <c>clock.UtcNow</c> 与库里的时间戳。</summary>
    private sealed class ThrottleDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ServiceProvider Provider { get; }

        public FakeClock Clock { get; }

        public ThrottleDatabase(DateTime nowUtc)
        {
            FunGameRuntime.EnsureInitialized();
            Clock = new FakeClock(nowUtc);
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<ApiKeyAuthenticator>();
            services.AddGameplayRuntime(clock: Clock);
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IClubService, ClubService>();
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
                context.Users.Add(new User { Id = userId, Handle = $"h{userId}", Nickname = nickname });
                await context.SaveChangesAsync();
            }
            await Get<IPlayerService>().GetSnapshotAsync(userId);
        }

        /// <summary>塞历史提交记录，用来打满窗口配额（目标各不相同，免得撞上「已有一份待审」）。</summary>
        public async Task SeedHistoryAsync(long userId, int count, DateTime atUtc)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            for (int i = 0; i < count; i++)
            {
                context.RenameRequests.Add(new RenameRequest
                {
                    UserId = userId,
                    TargetType = RenameTargets.Character,
                    TargetId = $"seed-{i}",
                    RequestedName = $"历史{i}",
                    Status = 2,
                    CreatedAtUtc = atUtc,
                    ReviewedAtUtc = atUtc
                });
            }
            await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }
}
