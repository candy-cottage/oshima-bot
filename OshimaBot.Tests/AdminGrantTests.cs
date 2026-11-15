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
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 发放记录与撤销（2026-11-15 补的「回滚」）。
/// <para>此前发出去的物品/魔法卡**一点痕迹都没有**：命令层只有「谁敲了命令」，看不出给了谁多少；
/// 发错了只能改库。现在每条发放落一行 <see cref="AdminGrant"/>，撤销按编号定位并标记，不删行。</para>
/// </summary>
public sealed class AdminGrantTests
{
    private const long Admin = 9001;
    private const long Player = 9002;
    private const long Other = 9003;

    [Fact]
    public async Task GrantItem_LeavesARecord_AndRevokeTakesItBackExactlyOnce()
    {
        using AdminGrantDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);

        FunGame.Core.Entity.Item entry = FunGameService.AllItems
            .First(x => x.ItemType == FunGame.Core.Library.Constant.ItemType.Weapon);
        Assert.True((await admin.GrantItemAsync(Admin, Player.ToString(), entry.Name, 10)).Ok);

        ActionResult log = await admin.ListGrantsAsync(Admin, Player.ToString(), 1);
        Assert.True(log.Ok, log.Title);
        AdminGrant grant = Assert.Single(await db.GrantsAsync());
        Assert.Equal(GrantKinds.Item, grant.Kind);
        Assert.Equal(10, grant.Quantity);
        Assert.Contains($"#{grant.Id}", string.Join("\n", log.Lines));

        ActionResult revoked = await admin.RevokeGrantAsync(Admin, grant.Id, "发错人了");
        Assert.True(revoked.Ok, revoked.Title);
        Assert.DoesNotContain(await players.GetBagAsync(Player), x => x.Name == entry.Name);

        AdminGrant after = Assert.Single(await db.GrantsAsync());
        Assert.NotNull(after.RevokedAtUtc);
        Assert.Equal(Admin, after.RevokedBy);
        Assert.Contains("发错人了", after.RevokeNote);
        Assert.Contains("收回", after.RevokeNote);

        // 同一笔只能撤一次
        ActionResult again = await admin.RevokeGrantAsync(Admin, grant.Id, null);
        Assert.False(again.Ok);
        Assert.Contains("已经撤销过", again.Title);
    }

    [Fact]
    public async Task RevokeItem_ReportsTheShortfallWhenPartIsGone()
    {
        using AdminGrantDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);

        FunGame.Core.Entity.Item entry = FunGameService.AllItems
            .First(x => x.ItemType == FunGame.Core.Library.Constant.ItemType.Weapon);
        Assert.True((await admin.GrantItemAsync(Admin, Player.ToString(), entry.Name, 10)).Ok);
        long grantId = Assert.Single(await db.GrantsAsync()).Id;

        // 模拟「已经用掉 4 件」：把它们从背包删掉，撤销时只能收回 6 件
        await db.RemoveSomeItemsAsync(Player, entry.Name, 4);

        ActionResult revoked = await admin.RevokeGrantAsync(Admin, grantId, null);
        Assert.True(revoked.Ok, revoked.Title);
        Assert.Contains("×6 / 10", revoked.Status);
        Assert.Contains(revoked.Lines, line => line.Contains("差 4 件"));
        Assert.DoesNotContain(await players.GetBagAsync(Player), x => x.Name == entry.Name);
    }

    [Fact]
    public async Task RevokeCurrency_TakesWhatIsLeftAndSaysSo()
    {
        using AdminGrantDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);

        double before = (await players.GetSnapshotAsync(Player))!.Credits;   // 注册自带 5000
        Assert.True((await admin.GrantCurrencyAsync(Admin, Player.ToString(), "金币", 10_000)).Ok);
        long grantId = Assert.Single(await db.GrantsAsync()).Id;
        Assert.Equal(before + 10_000d, (await players.GetSnapshotAsync(Player))!.Credits);

        // 花掉 7000，只剩 3000：撤销只能收回 3000，差额要写明
        await db.SetCreditsAsync(Player, 3_000);

        ActionResult revoked = await admin.RevokeGrantAsync(Admin, grantId, "测试");
        Assert.True(revoked.Ok, revoked.Title);
        Assert.Equal(0d, (await players.GetSnapshotAsync(Player))!.Credits);
        Assert.Contains(revoked.Lines, line => line.Contains("差 7000.00"));

        // 撤销也要留账：ledger 里有一条 admin_revoke
        Assert.True(await db.HasLedgerAsync(Player, "admin_revoke"));
    }

    [Fact]
    public async Task RevokeCurrency_RefusesWhenBalanceIsAlreadyZero()
    {
        using AdminGrantDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);

        Assert.True((await admin.GrantCurrencyAsync(Admin, Player.ToString(), "钻石", 500)).Ok);
        long grantId = Assert.Single(await db.GrantsAsync()).Id;
        await db.SetMaterialsAsync(Player, 0);

        ActionResult revoked = await admin.RevokeGrantAsync(Admin, grantId, null);
        Assert.False(revoked.Ok);
        Assert.Contains("花掉", revoked.Lines[0]);

        // 没收回就不许标记：记录仍待处理，等余额有了还能再撤
        AdminGrant grant = Assert.Single(await db.GrantsAsync());
        Assert.Null(grant.RevokedAtUtc);
    }

    [Fact]
    public async Task RevokeMagicCard_MatchesTheBatchByCatalogId()
    {
        using AdminGrantDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);

        FunGame.Core.Entity.Skill magic = FunGameService.AllSkills.First(x => x.IsMagic);
        Assert.True((await admin.GrantMagicCardAsync(Admin, Player.ToString(), magic.Name, 3, 0, 0, 0, 2)).Ok);
        AdminGrant grant = Assert.Single(await db.GrantsAsync());
        Assert.Equal(GrantKinds.Magic, grant.Kind);
        Assert.Equal(2, grant.Quantity);
        Assert.Equal(magic.Id.ToString(), grant.CatalogId);
        Assert.Equal(2, (await players.GetBagAsync(Player)).Count(x => x.CatalogId == grant.CatalogId));

        Assert.True((await admin.RevokeGrantAsync(Admin, grant.Id, null)).Ok);
        Assert.DoesNotContain(await players.GetBagAsync(Player), x => x.CatalogId == grant.CatalogId);
    }

    [Fact]
    public async Task ListGrants_FiltersByTargetAndPaginates()
    {
        using AdminGrantDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);
        await players.RegisterAsync(Other);

        for (int i = 0; i < 25; i++)
        {
            Assert.True((await admin.GrantCurrencyAsync(Admin, Player.ToString(), "金币", 100 + i)).Ok);
        }
        Assert.True((await admin.GrantCurrencyAsync(Admin, Other.ToString(), "金币", 999)).Ok);

        ActionResult first = await admin.ListGrantsAsync(Admin, null, 1);
        Assert.True(first.Ok);
        Assert.Contains("共 26 条", first.Status);

        ActionResult second = await admin.ListGrantsAsync(Admin, null, 2);
        Assert.Contains("第 2/2 页", second.Status);

        ActionResult onlyOther = await admin.ListGrantsAsync(Admin, Other.ToString(), 1);
        Assert.Contains("共 1 条", onlyOther.Status);
    }

    [Fact]
    public async Task Revoke_RejectsUnknownRecord()
    {
        using AdminGrantDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();

        ActionResult missing = await admin.RevokeGrantAsync(Admin, 999, null);
        Assert.False(missing.Ok);
        Assert.Contains("没有 #999", missing.Title);

        Assert.False((await admin.RevokeGrantAsync(Admin, 0, null)).Ok);
    }

    // ---------------- 夹具 ----------------

    private sealed class AdminGrantDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ServiceProvider Provider { get; }

        public FakeClock Clock { get; }

        public AdminGrantDatabase()
        {
            FunGameRuntime.EnsureInitialized();
            Clock = new FakeClock(new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc));
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddGameplayRuntime(clock: Clock);
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IAdminService, AdminService>();
            Provider = services.BuildServiceProvider();

            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task<List<AdminGrant>> GrantsAsync()
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return await db.AdminGrants.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        }

        public async Task SetCreditsAsync(long userId, double value) => await SetCurrencyAsync(userId, value, diamonds: false);

        public async Task SetMaterialsAsync(long userId, double value) => await SetCurrencyAsync(userId, value, diamonds: true);

        private async Task SetCurrencyAsync(long userId, double value, bool diamonds)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await db.CorePlayers.FirstAsync(x => x.UserId == userId);
            if (diamonds) wallet.Materials = value; else wallet.Credits = value;
            await db.SaveChangesAsync();
        }

        /// <summary>模拟「这些物品已经不在了」：直接从背包删掉最旧的若干件。</summary>
        public async Task RemoveSomeItemsAsync(long userId, string name, int count)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            List<PlayerItem> items = await ItemStore.ListAsync(db, userId, CancellationToken.None);
            int remaining = count;
            foreach (PlayerItem item in items.Where(x => x.Name == name))
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, item.Quantity);
                if (item.Quantity <= take) ItemStore.Remove(db, item);
                else item.Quantity -= take;
                remaining -= take;
            }
            await db.SaveChangesAsync();
        }

        public async Task<bool> HasLedgerAsync(long userId, string reason)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return await db.LedgerEntries.AnyAsync(x => x.UserId == userId && x.Reason == reason);
        }

        public void Dispose()
        {
            Provider.Dispose();
            _connection.Dispose();
        }
    }
}
