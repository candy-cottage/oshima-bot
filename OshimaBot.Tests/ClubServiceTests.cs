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

    [Fact]
    public async Task Tasks_ProgressFromDonateRecruitAndCollect()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await db.CreatePlayerAsync(Member, "路人");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);
        long clubId = (await clubs.MineAsync(Master))!.Id;

        Assert.True((await clubs.DonateAsync(Master, 5000)).Ok);        // 捐献 → 任务 1
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);        // 加入 → 任务 2
        await db.AddItemAsync(Master, "测试矿石", 1, 20);                // 提交 → 任务 3

        IReadOnlyList<ClubTaskEntry> tasks = await clubs.TasksAsync(Master);
        Assert.Equal(3, tasks.Count);
        Assert.Equal(5000, tasks[0].Progress);
        Assert.True(tasks[0].Done);
        Assert.Equal(1, tasks[1].Progress);
        Assert.False(tasks[1].Done);

        // 还没完成的任务不能结算
        Assert.False((await clubs.ClaimTaskAsync(Master, 2)).Ok);

        Assert.True((await clubs.ContributeTaskAsync(Master, 1, 20)).Ok);
        tasks = await clubs.TasksAsync(Master);
        Assert.Equal(20, tasks[2].Progress);
        Assert.True(tasks[2].Done);

        double funds = (await clubs.MineAsync(Master))!.Funds;
        ActionResult claimed = await clubs.ClaimTaskAsync(Master, 1);
        Assert.True(claimed.Ok, claimed.Title);
        Assert.Equal(funds + ClubRules.Tasks[0].RewardFunds, (await clubs.MineAsync(Master))!.Funds, 3);

        // 同一个任务一个周期只结算一次
        Assert.False((await clubs.ClaimTaskAsync(Master, 1)).Ok);
        Assert.True((await clubs.TasksAsync(Master))[0].Claimed);

        // 普通成员不能结算
        Assert.False((await clubs.ClaimTaskAsync(Member, 3)).Ok);
    }

    [Fact]
    public async Task Tasks_ContributeConsumesItemAndRejectsLocked()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);

        await db.AddItemAsync(Master, "测试矿石", 2, 5);
        double funds = (await clubs.MineAsync(Master))!.Funds;

        ActionResult submitted = await clubs.ContributeTaskAsync(Master, 1, 3);
        Assert.True(submitted.Ok, submitted.Title);
        Assert.Equal(2, (await db.ItemsAsync(Master)).Single().Quantity);
        // 优秀品质：分解 3 材料，征集折算 3/2 = 1 基金/件
        Assert.Equal(funds + 3, (await clubs.MineAsync(Master))!.Funds, 3);

        await db.SetLockAsync(Master, "测试矿石", true);
        ActionResult locked = await clubs.ContributeTaskAsync(Master, 1, 1);
        Assert.False(locked.Ok);
        Assert.Contains("锁定", locked.Title);
    }

    [Fact]
    public async Task Store_ExchangesFundsForGoodsAndBuysItemsBack()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);
        Assert.True((await clubs.DonateAsync(Master, 2000)).Ok);

        ClubStoreView view = await clubs.StoreAsync(Master);
        Assert.Equal(2000, view.Funds, 3);
        Assert.Equal(ClubRules.Goods.Count, view.Goods.Count);
        ClubStoreGoodSpec diamond = ClubRules.Goods[0];
        Assert.True(ClubRules.IsCurrencyGood(diamond));

        double materials = await db.MaterialsAsync(Master);
        Assert.True((await clubs.StoreBuyAsync(Master, 1, 2)).Ok);
        Assert.Equal(materials + 2, await db.MaterialsAsync(Master), 3);
        Assert.Equal(2000 - diamond.Price * 2, (await clubs.MineAsync(Master))!.Funds, 3);

        // 基金不够就整笔拒绝
        Assert.False((await clubs.StoreBuyAsync(Master, 1, 99)).Ok);

        // 收购：优秀品质分解 3 材料 → 3 * 100 = 300 金币
        await db.AddItemAsync(Master, "测试矿石", 2, 1);
        double credits = await db.CreditsAsync(Master);
        double fundsBefore = (await clubs.MineAsync(Master))!.Funds;
        ActionResult sold = await clubs.StoreSellAsync(Master, 1, 1);
        Assert.True(sold.Ok, sold.Title);
        Assert.Equal(credits + 300, await db.CreditsAsync(Master), 3);
        Assert.Equal(fundsBefore - 300, (await clubs.MineAsync(Master))!.Funds, 3);
    }

    [Fact]
    public async Task Store_OnlyAdminsMaySpendFunds()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await db.CreatePlayerAsync(Member, "路人");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);
        long clubId = (await clubs.MineAsync(Master))!.Id;
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);
        Assert.True((await clubs.DonateAsync(Master, 1000)).Ok);

        ActionResult denied = await clubs.StoreBuyAsync(Member, 1, 1);
        Assert.False(denied.Ok);
        Assert.Contains("社长或管理", denied.Title);

        // 任命管理后就能花基金了
        Assert.True((await clubs.UpdateSettingsAsync(Master, "任命", "2")).Ok);
        Assert.True((await clubs.StoreBuyAsync(Member, 1, 1)).Ok);
    }

    [Fact]
    public async Task Market_MovesItemBetweenMembersAndFeeGoesToFunds()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await db.CreatePlayerAsync(Member, "路人");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);
        long clubId = (await clubs.MineAsync(Master))!.Id;
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);

        await db.AddItemAsync(Master, "测试矿石", 3, 1);
        double funds = (await clubs.MineAsync(Master))!.Funds;

        ActionResult listed = await clubs.MarketListAsync(Master, 1, 1000);
        Assert.True(listed.Ok, listed.Title);
        Assert.Equal(funds + ClubRules.MarketFee(1000), (await clubs.MineAsync(Master))!.Funds, 3);
        Assert.Empty(await db.ItemsAsync(Master));                     // 物品已托管

        ClubMarketEntry entry = Assert.Single(await clubs.MarketAsync(Member));
        Assert.True(entry.Mine == false);
        Assert.False((await clubs.MarketBuyAsync(Master, 1)).Ok);       // 不能买自己的

        double buyer = await db.CreditsAsync(Member);
        double seller = await db.CreditsAsync(Master);
        Assert.True((await clubs.MarketBuyAsync(Member, 1)).Ok);
        Assert.Equal(buyer - 1000, await db.CreditsAsync(Member), 3);
        Assert.Equal(seller + 1000, await db.CreditsAsync(Master), 3);
        Assert.Single(await db.ItemsAsync(Member));
        Assert.Empty(await clubs.MarketAsync(Member));
    }

    [Fact]
    public async Task Market_DelistAndClearReturnItems()
    {
        using ClubDatabase db = new();
        IClubService clubs = db.Get<IClubService>();
        await db.CreatePlayerAsync(Master, "社长");
        await db.CreatePlayerAsync(Member, "路人");
        await clubs.CreateAsync(Master, "ABC", isPublic: true);
        long clubId = (await clubs.MineAsync(Master))!.Id;
        Assert.True((await clubs.JoinAsync(Member, clubId)).Ok);

        await db.AddItemAsync(Member, "测试矿石", 1, 1);
        Assert.True((await clubs.MarketListAsync(Member, 1, 500)).Ok);
        Assert.True((await clubs.MarketDelistAsync(Member, 1)).Ok);
        Assert.Single(await db.ItemsAsync(Member));
        Assert.Empty(await clubs.MarketAsync(Member));

        // 退团时在售挂单自动退回
        Assert.True((await clubs.MarketListAsync(Member, 1, 500)).Ok);
        Assert.True((await clubs.QuitAsync(Member)).Ok);
        Assert.Single(await db.ItemsAsync(Member));

        // 社长清空市场
        await db.AddItemAsync(Master, "测试矿石", 1, 1);
        Assert.True((await clubs.MarketListAsync(Master, 1, 800)).Ok);
        ActionResult cleared = await clubs.MarketClearAsync(Master);
        Assert.True(cleared.Ok, cleared.Title);
        Assert.Single(await db.ItemsAsync(Master));
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


        /// <summary>塞一件测试物品（品质按玩法档位 1–7）。</summary>
        public async Task AddItemAsync(long userId, string name, int quality, int quantity)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await ItemStore.AddAsync(context, userId, name, quality, quantity, default, "test");
            await context.SaveChangesAsync();
        }

        public async Task<List<PlayerItem>> ItemsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return await ItemStore.ListAsync(context, userId, default);
        }

        /// <summary>给某件物品上锁（征集/市场都拒绝锁定物品）。</summary>
        public async Task SetLockAsync(long userId, string name, bool locked)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            List<PlayerItem> items = await ItemStore.ListAsync(context, userId, default);
            PlayerItem item = items.First(x => x.Name == name);
            item.Locked = locked;
            ItemStore.Sync(item);
            await context.SaveChangesAsync();
        }

        public async Task<double> MaterialsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId);
            return wallet.Materials;
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
