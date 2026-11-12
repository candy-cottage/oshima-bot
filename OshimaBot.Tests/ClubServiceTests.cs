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
/// 社团层（M6.2）：创建 / 加入 / 审批 / 邀请 / 成员管理 / 设置 / 捐献 / 解散。
/// <para>旧版每个社团一个 JSON 文件、用户身上记 club 编号；这里四张表 + 「一个人只能在一个社团」的唯一索引。</para>
/// </summary>
public sealed class ClubServiceTests
{
    private const long Master = 42;
    private const long Member = 77;
    private const long Outsider = 99;

    [Fact]
    public async Task Create_ValidatesPrefixAndMembership()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");

        ActionResult bad = await clubs.CreateAsync(Master, "ab", isPublic: true);
        Assert.False(bad.Ok);
        Assert.Contains("前缀", bad.Title);

        ActionResult created = await clubs.CreateAsync(Master, "ABC", isPublic: true);
        Assert.True(created.Ok, created.Title);

        ClubDetail? mine = await clubs.MineAsync(Master);
        Assert.NotNull(mine);
        Assert.Equal("ABC", mine!.Prefix);
        Assert.True(mine.IsMaster);
        Assert.True(mine.IsPublic);
        Assert.Equal(1, mine.Members);

        // 已经在社团里就不能再建
        Assert.False((await clubs.CreateAsync(Master, "XYZ", isPublic: true)).Ok);
    }

    [Fact]
    public async Task Join_RespectsPrivacyAndApproval()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await db.CreatePlayerAsync(Member, "路人");
        await db.CreatePlayerAsync(Outsider, "第三人");

        // 私密社团：没邀请进不去
        await clubs.CreateAsync(Master, "PRV", isPublic: false);
        long clubId = (await clubs.MineAsync(Master))!.Id;
        ActionResult denied = await clubs.JoinAsync(Member, clubId);
        Assert.False(denied.Ok);
        Assert.Contains("未公开", denied.Title);

        // 邀请后可以进（还能绕过审批）
        Assert.True((await clubs.InviteAsync(Master, Member.ToString())).Ok);
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);
        Assert.Equal(2, (await clubs.InfoAsync(clubId, Master))!.Members);

        // 开启审批：第三人申请 → 出现在申请人列表 → 批准后入团
        Assert.True((await clubs.UpdateSettingsAsync(Master, "需批准", null)).Ok);
        Assert.True((await clubs.UpdateSettingsAsync(Master, "公开", null)).Ok);
        ActionResult applied = await clubs.JoinAsync(Outsider, clubId);
        Assert.True(applied.Ok, applied.Title);
        Assert.Contains("申请", applied.Title);

        IReadOnlyList<ClubApplicantEntry> applicants = await clubs.ApplicationsAsync(Master);
        ClubApplicantEntry applicant = Assert.Single(applicants);
        Assert.True((await clubs.ApproveAsync(Master, applicant.Index)).Ok);
        Assert.Empty(await clubs.ApplicationsAsync(Master));
        Assert.Equal(3, (await clubs.InfoAsync(clubId, Master))!.Members);
    }

    [Fact]
    public async Task Quit_BlocksMasterAndFreesMembership()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await db.CreatePlayerAsync(Member, "路人");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);
        long clubId = (await clubs.MineAsync(Master))!.Id;
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);

        ActionResult masterQuit = await clubs.QuitAsync(Master);
        Assert.False(masterQuit.Ok);
        Assert.Contains("社长", masterQuit.Title);

        Assert.True((await clubs.QuitAsync(Member)).Ok);
        Assert.Null(await clubs.MineAsync(Member));
    }

    [Fact]
    public async Task KickAndTransfer_FollowRoles()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await db.CreatePlayerAsync(Member, "路人");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);
        long clubId = (await clubs.MineAsync(Master))!.Id;
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);

        // 成员顺序：社长在前，路人在第 2 位
        IReadOnlyList<ClubMemberEntry> members = await clubs.MembersAsync(Master);
        Assert.Equal(ClubRole.Master, members[0].Role);

        // 不能踢社长；可以踢普通成员
        Assert.Contains("社长", (await clubs.KickAsync(Master, 1)).Title);
        Assert.True((await clubs.KickAsync(Master, 2)).Ok);
        Assert.Single(await clubs.MembersAsync(Master));

        // 重新加入后转让社长
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);
        Assert.True((await clubs.TransferAsync(Master, 2)).Ok);
        ClubDetail detail = (await clubs.MineAsync(Member))!;
        Assert.True(detail.IsMaster);
        Assert.Equal(Member, detail.MasterUserId);
    }

    [Fact]
    public async Task Donate_MovesCreditsIntoClubFunds()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);

        double before = await db.CreditsAsync(Master);
        Assert.True((await clubs.DonateAsync(Master, 1000)).Ok);

        ClubDetail detail = (await clubs.MineAsync(Master))!;
        Assert.Equal(1000, detail.Funds, 3);
        Assert.Equal(before - 1000, await db.CreditsAsync(Master), 3);

        // 超过余额就整笔拒绝
        Assert.False((await clubs.DonateAsync(Master, 999_999)).Ok);
        Assert.Equal(1000, (await clubs.MineAsync(Master))!.Funds, 3);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(1, await context.LedgerEntries.CountAsync(x => x.Reason == "club_donate"));
    }

    [Fact]
    public async Task Settings_ToggleRulesAndRename()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await db.CreatePlayerAsync(Member, "路人");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);
        long clubId = (await clubs.MineAsync(Master))!.Id;

        Assert.True((await clubs.UpdateSettingsAsync(Master, "私密", null)).Ok);
        Assert.False((await clubs.InfoAsync(clubId, Master))!.IsPublic);
        Assert.True((await clubs.UpdateSettingsAsync(Master, "需批准", null)).Ok);
        Assert.True((await clubs.InfoAsync(clubId, Master))!.NeedApproval);

        Assert.True((await clubs.UpdateSettingsAsync(Master, "公告", "周末一起跑赛马")).Ok);
        Assert.Equal("周末一起跑赛马", (await clubs.MineAsync(Master))!.Notice);

        Assert.True((await clubs.UpdateSettingsAsync(Master, "名称", "夜航船")).Ok);
        Assert.Equal("夜航船", (await clubs.MineAsync(Master))!.Name);

        // 普通成员不能改设置
        Assert.True((await clubs.InviteAsync(Master, Member.ToString())).Ok);
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);
        Assert.False((await clubs.UpdateSettingsAsync(Member, "公开", null)).Ok);

        // 任命管理后可以改规则
        Assert.True((await clubs.UpdateSettingsAsync(Master, "任命", "2")).Ok);
        Assert.True((await clubs.UpdateSettingsAsync(Member, "公开", null)).Ok);
    }

    [Fact]
    public async Task Disband_ClearsMembership()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await db.CreatePlayerAsync(Member, "路人");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);
        long clubId = (await clubs.MineAsync(Master))!.Id;
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);

        Assert.False((await clubs.DisbandAsync(Member)).Ok);      // 只有社长能解散
        Assert.True((await clubs.DisbandAsync(Master)).Ok);
        Assert.Empty(await clubs.ListAsync());
        Assert.Null(await clubs.MineAsync(Member));
    }

    private sealed class ClubDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public ClubDatabase()
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
            services.AddSingleton<IClubService, ClubService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        /// <summary>建存档（含 5000 初始金币）+ 一条 Users 行（社团里的显示名取自它）。</summary>
        public async Task CreatePlayerAsync(long userId, string nickname)
        {
            await Get<IPlayerService>().GetSnapshotAsync(userId);
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            context.Users.Add(new User { Id = userId, Handle = $"handle{userId}", Nickname = nickname });
            await context.SaveChangesAsync();
        }

        public async Task<double> CreditsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId);
            return wallet.Credits;
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
