using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 管理员发放（旧版 <c>生成 N 个 X</c> / <c>生成指定魔法卡</c>）。
/// <para>上限规则按 2026-11-14 的决定：物品与魔法卡单次 100，金币与钻石**不设上限**。</para>
/// </summary>
public sealed class AdminServiceTests
{
    private const long Admin = 8001;
    private const long Player = 8002;

    [Fact]
    public async Task GrantItem_StacksInBagAndRejectsOverCap()
    {
        using AdminDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);

        FunGame.Core.Entity.Item entry = FunGameService.AllItems
            .First(x => x.ItemType is FunGame.Core.Library.Constant.ItemType.Weapon);

        ActionResult granted = await admin.GrantItemAsync(Admin, Player.ToString(), entry.Name, 10);
        Assert.True(granted.Ok, granted.Title);
        BagEntry stack = Assert.Single(await players.GetBagAsync(Player), x => x.Name == entry.Name);
        Assert.Equal(10, stack.Quantity);                              // 10 件并成一叠

        ActionResult again = await admin.GrantItemAsync(Admin, Player.ToString(), entry.Name, 5);
        Assert.True(again.Ok, again.Title);
        Assert.Equal(15, Assert.Single(await players.GetBagAsync(Player), x => x.Name == entry.Name).Quantity);

        ActionResult tooMany = await admin.GrantItemAsync(Admin, Player.ToString(), entry.Name, AdminService.MaxGrantPerCall + 1);
        Assert.False(tooMany.Ok);
        Assert.Contains("单次最多发 100 件", tooMany.Title);

        Assert.False((await admin.GrantItemAsync(Admin, Player.ToString(), "并不存在的物品", 1)).Ok);

        // 内容包里没有「魔法卡」这种目录物品（卡是随机生成的），
        // 所以「魔法卡请用专门指令」那条分支是防御性的，这里只锁「名字不存在」的拒绝。
        Assert.False((await admin.GrantItemAsync(Admin, "查无此人", entry.Name, 1)).Ok);
    }

    [Fact]
    public async Task GrantItem_DefaultsToSelf()
    {
        using AdminDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Admin);

        FunGame.Core.Entity.Item entry = FunGameService.AllItems
            .First(x => x.ItemType == FunGame.Core.Library.Constant.ItemType.Weapon);
        Assert.True((await admin.GrantItemAsync(Admin, null, entry.Name, 1)).Ok);
        Assert.Contains(await players.GetBagAsync(Admin), x => x.Name == entry.Name);
    }

    [Fact]
    public async Task GrantCurrency_HasNoCapAndWritesLedger()
    {
        using AdminDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);

        PlayerSnapshot before = (await players.GetSnapshotAsync(Player))!;

        // 用户决定：金币/钻石不设上限，所以 1,000,000 也必须能发（物品那边是 100）。
        ActionResult cash = await admin.GrantCurrencyAsync(Admin, Player.ToString(), "金币", 1_000_000);
        Assert.True(cash.Ok, cash.Title);
        Assert.Equal(before.Credits + 1_000_000, (await players.GetSnapshotAsync(Player))!.Credits, 3);

        // 钻石带小数：内部不取整（与全局口径一致）
        ActionResult gems = await admin.GrantCurrencyAsync(Admin, Player.ToString(), "钻石", 250.5);
        Assert.True(gems.Ok, gems.Title);
        Assert.Equal(before.Materials + 250.5, (await players.GetSnapshotAsync(Player))!.Materials, 3);

        Assert.False((await admin.GrantCurrencyAsync(Admin, Player.ToString(), "金币", 0)).Ok);
        Assert.False((await admin.GrantCurrencyAsync(Admin, Player.ToString(), "金币", -5)).Ok);
        Assert.False((await admin.GrantCurrencyAsync(Admin, Player.ToString(), "银币", 5)).Ok);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        List<double> grants = await context.LedgerEntries
            .Where(x => x.UserId == Player && x.Reason == "admin_grant")
            .OrderBy(x => x.Id).Select(x => x.Delta).ToListAsync();
        Assert.Equal([1_000_000d, 250.5d], grants);
    }

    [Fact]
    public async Task GrantMagicCard_HonoursQualityWhenStatsAreRandom()
    {
        using AdminDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);

        FunGame.Core.Entity.Skill magic = FunGameService.AllSkills.First(x => x.IsMagic);

        // 在内容包里挑一个魔法卡上确实存在的品质，避免品质池为空
        int tier = 5;   // 传说
        ActionResult result = await admin.GrantMagicCardAsync(Admin, Player.ToString(), magic.Id.ToString(), tier, 0, 0, 0, 2);
        Assert.True(result.Ok, result.Title);

        List<BagEntry> cards = [.. (await players.GetBagAsync(Player))
            .Where(x => x.Name.Length > 0)];
        List<BagEntry> granted = [.. cards.Where(x => x.Quality == tier)];
        Assert.Equal(2, granted.Count);                                 // 两张独立，不并叠
        Assert.Contains(result.Lines, x => x.Contains("按该品质的区间随机分配"));
    }

    [Fact]
    public async Task GrantMagicCard_StatsDecideQualityAndItSaysSo()
    {
        using AdminDatabase db = new();
        IAdminService admin = db.Get<IAdminService>();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(Player);

        FunGame.Core.Entity.Skill magic = FunGameService.AllSkills.First(x => x.IsMagic);

        // 三围总量 15 → 引擎把品质提到「稀有」(tier 3)，覆盖请求的 5。
        // 这是引擎规则（旧版同一条生成路径），回复必须如实说明，而不是假装发的是传说。
        ActionResult result = await admin.GrantMagicCardAsync(Admin, Player.ToString(), magic.Id.ToString(), 5, 10, 5, 0, 1);
        Assert.True(result.Ok, result.Title);
        Assert.Contains(result.Lines, x => x.Contains("三围总量 15"));
        Assert.Contains(result.Lines, x => x.Contains("稀有"));

        BagEntry card = Assert.Single(await players.GetBagAsync(Player), x => x.Quality == 3);
        Assert.Equal("稀有", GameplayRules.QualityName(card.Quality));

        Assert.False((await admin.GrantMagicCardAsync(Admin, Player.ToString(), magic.Id.ToString(), 9, 0, 0, 0, 1)).Ok);
        Assert.False((await admin.GrantMagicCardAsync(Admin, Player.ToString(), "并不存在的魔法", 5, 0, 0, 0, 1)).Ok);
        Assert.False((await admin.GrantMagicCardAsync(Admin, Player.ToString(), magic.Id.ToString(), 5, 0, 0, 0,
            AdminService.MaxGrantPerCall + 1)).Ok);
        Assert.False((await admin.GrantMagicCardAsync(Admin, Player.ToString(), magic.Id.ToString(), 5, -1, 0, 0, 1)).Ok);
    }

    // ---------------- 测试宿主 ----------------

    private sealed class AdminDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public AdminDatabase()
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddGameplayRuntime();
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IAdminService, AdminService>();

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
