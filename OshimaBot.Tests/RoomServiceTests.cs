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
/// 多人房间层（S1）：建房 / 加入 / 退出 / 列表 / 信息 / 开局 / 空闲解散。
/// <para>房间是进程内状态，但成员的角色要从数据库按 <c>CombatUnits</c> 装配，所以整类用真实 SQLite 内存库。</para>
/// </summary>
public sealed class RoomServiceTests
{
    private const long Host = 42;
    private const long Guest = 77;
    private const string GroupA = "GROUP-A";
    private const string GroupB = "GROUP-B";

    [Fact]
    public async Task Create_ReturnsScopedRoomWithSevenCharCode()
    {
        using RoomDatabase db = new();
        IRoomService rooms = db.Get<IRoomService>();

        ActionResult created = await rooms.CreateAsync(Host, GroupA, nameof(RoomKind.Mix), null);

        Assert.True(created.Ok, created.Title);
        RoomSummary room = Assert.Single(rooms.List(GroupA));
        Assert.Equal(RoomRules.CodeLength, room.Code.Length);
        Assert.Equal("混战房间", room.KindName);
        Assert.Equal(10, room.MaxUsers);
        Assert.Equal(1, room.Members);
        Assert.Empty(rooms.List(GroupB));                    // 按群隔离
        Assert.Null(rooms.Info(GroupB, room.Code, Host));    // 别的群看不到
    }

    [Fact]
    public async Task Create_RejectsPrivateChatAndSecondRoom()
    {
        using RoomDatabase db = new();
        IRoomService rooms = db.Get<IRoomService>();

        ActionResult noScope = await rooms.CreateAsync(Host, "", nameof(RoomKind.Mix), null);
        Assert.False(noScope.Ok);
        Assert.Contains("群里", noScope.Title);

        Assert.True((await rooms.CreateAsync(Host, GroupA, "混战", null)).Ok);
        ActionResult twice = await rooms.CreateAsync(Host, GroupA, "团战", null);
        Assert.False(twice.Ok);
        Assert.Contains("已经在", twice.Title);

        ActionResult unknownKind = await rooms.CreateAsync(Guest, GroupA, "扫雷", null);
        Assert.False(unknownKind.Ok);
        Assert.Contains("类型无效", unknownKind.Title);
    }

    [Fact]
    public async Task HorseRacing_AllowsOnlyOneRoomPerGroup()
    {
        using RoomDatabase db = new();
        IRoomService rooms = db.Get<IRoomService>();

        Assert.True((await rooms.CreateAsync(Host, GroupA, "赛马", null)).Ok);
        ActionResult second = await rooms.CreateAsync(Guest, GroupA, "赛马", null);
        Assert.False(second.Ok);
        Assert.Contains("赛马房间", second.Title);

        // 换群可以建
        Assert.True((await rooms.CreateAsync(Guest, GroupB, "赛马", null)).Ok);
    }

    [Fact]
    public async Task Join_ChecksCodePasswordAndCapacity()
    {
        using RoomDatabase db = new();
        IRoomService rooms = db.Get<IRoomService>();
        await rooms.CreateAsync(Host, GroupA, "共斗", "secret");
        string code = rooms.List(GroupA)[0].Code;

        ActionResult wrongPassword = await rooms.JoinAsync(Guest, GroupA, code, "nope");
        Assert.False(wrongPassword.Ok);
        Assert.Contains("密码", wrongPassword.Title);

        Assert.True((await rooms.JoinAsync(Guest, GroupA, code.ToLowerInvariant(), "secret")).Ok);   // 房号大小写不敏感

        // 共斗上限 4 人：再加 3 个应该被第 3 个之后的挡住
        Assert.True((await rooms.JoinAsync(101, GroupA, code, "secret")).Ok);
        Assert.True((await rooms.JoinAsync(102, GroupA, code, "secret")).Ok);
        ActionResult full = await rooms.JoinAsync(103, GroupA, code, "secret");
        Assert.False(full.Ok);
        Assert.Contains("已满", full.Title);

        ActionResult unknown = await rooms.JoinAsync(104, GroupA, "ZZZZZZZ", null);
        Assert.False(unknown.Ok);
        Assert.Contains("找不到房间", unknown.Title);
    }

    [Fact]
    public async Task Quit_TransfersMasterThenDisbands()
    {
        using RoomDatabase db = new();
        IRoomService rooms = db.Get<IRoomService>();
        await rooms.CreateAsync(Host, GroupA, "混战", null);
        string code = rooms.List(GroupA)[0].Code;
        await rooms.JoinAsync(Guest, GroupA, code, null);

        Assert.True((await rooms.QuitAsync(Host, GroupA)).Ok);
        RoomDetail? afterHostLeft = rooms.Info(GroupA, code, Guest);
        Assert.NotNull(afterHostLeft);
        Assert.Equal(Guest, afterHostLeft!.MasterId);
        Assert.True(afterHostLeft.IsMaster);

        Assert.True((await rooms.QuitAsync(Guest, GroupA)).Ok);
        Assert.Empty(rooms.List(GroupA));                    // 最后一人离开 → 解散
        Assert.False((await rooms.QuitAsync(Guest, GroupA)).Ok);
    }

    [Fact]
    public async Task Sweep_DisbandsIdleRoomsAfterTenMinutes()
    {
        FakeClock clock = new(new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc));
        using RoomDatabase db = new(clock);
        IRoomService rooms = db.Get<IRoomService>();
        await rooms.CreateAsync(Host, GroupA, "混战", null);

        clock.Advance(TimeSpan.FromMinutes(RoomRules.ExpireMinutes - 1));
        Assert.Single(rooms.List(GroupA));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Empty(rooms.List(GroupA));

        // 解散后同一个人可以立刻再建（占用标记也要跟着清掉）
        Assert.True((await rooms.CreateAsync(Host, GroupA, "混战", null)).Ok);
    }

    [Fact]
    public async Task Start_RequiresMasterAndTwoMembers()
    {
        using RoomDatabase db = new();
        IRoomService rooms = db.Get<IRoomService>();
        await db.CreateSaveAsync(Host, "房主");
        await db.CreateSaveAsync(Guest, "客人");
        await rooms.CreateAsync(Host, GroupA, "混战", null);
        string code = rooms.List(GroupA)[0].Code;

        ActionResult alone = await rooms.StartAsync(Host, GroupA);
        Assert.False(alone.Ok);
        Assert.Contains("人数不足", alone.Title);

        await rooms.JoinAsync(Guest, GroupA, code, null);
        ActionResult notMaster = await rooms.StartAsync(Guest, GroupA);
        Assert.False(notMaster.Ok);
        Assert.Contains("房主", notMaster.Title);
    }

    [Fact]
    public async Task Start_MixRunsBattleAndRoomStaysReusable()
    {
        using RoomDatabase db = new();
        IRoomService rooms = db.Get<IRoomService>();
        await db.CreateSaveAsync(Host, "甲");
        await db.CreateSaveAsync(Guest, "乙");
        await rooms.CreateAsync(Host, GroupA, "混战", null);
        string code = rooms.List(GroupA)[0].Code;
        await rooms.JoinAsync(Guest, GroupA, code, null);

        ActionResult report = await rooms.StartAsync(Host, GroupA);

        Assert.True(report.Ok, report.Title);
        Assert.Contains("参战 2 人", string.Join(" ", report.Lines));
        RoomDetail? detail = rooms.Info(GroupA, code, Host);
        Assert.NotNull(detail);
        Assert.False(detail!.Playing);                       // 打完回到等待状态，可以再开
        Assert.Equal(2, detail.Members.Count);
    }

    [Fact]
    public async Task Start_TeamSplitsIntoTwoTeams()
    {
        using RoomDatabase db = new();
        IRoomService rooms = db.Get<IRoomService>();
        await db.CreateSaveAsync(Host, "红方");
        await db.CreateSaveAsync(Guest, "蓝方");
        await rooms.CreateAsync(Host, GroupA, "团战", null);
        string code = rooms.List(GroupA)[0].Code;
        await rooms.JoinAsync(Guest, GroupA, code, null);

        ActionResult report = await rooms.StartAsync(Host, GroupA);

        Assert.True(report.Ok, report.Title);
        string content = string.Join(" ", report.Lines);
        Assert.Contains("红队", content);
        Assert.Contains("蓝队", content);
    }

    [Fact]
    public async Task Start_NotReadyKindsKeepRoomIdle()
    {
        using RoomDatabase db = new();
        IRoomService rooms = db.Get<IRoomService>();
        await db.CreateSaveAsync(Host, "甲");
        await db.CreateSaveAsync(Guest, "乙");
        await rooms.CreateAsync(Host, GroupA, "赛马", null);
        string code = rooms.List(GroupA)[0].Code;
        await rooms.JoinAsync(Guest, GroupA, code, null);

        ActionResult report = await rooms.StartAsync(Guest, GroupA);   // 赛马房任何人都能开

        Assert.False(report.Ok);
        Assert.Contains("还没接上玩法", report.Title);
        RoomDetail? detail = rooms.Info(GroupA, code, Host);
        Assert.NotNull(detail);
        Assert.False(detail!.Playing);                                 // 状态没被锁死
    }

    private sealed class RoomDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public RoomDatabase(IGameClock? clock = null)
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
            services.AddGameplayRuntime(clock);
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IRoomService, RoomService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        /// <summary>建存档 + 补一条 Users 行（房间成员显示名取自它）。</summary>
        public async Task CreateSaveAsync(long userId, string nickname)
        {
            await Get<IPlayerService>().GetSnapshotAsync(userId);
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            context.Users.Add(new OshimaBot.Data.Entities.User
            {
                Id = userId, Handle = $"handle{userId}", Nickname = nickname
            });
            await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
