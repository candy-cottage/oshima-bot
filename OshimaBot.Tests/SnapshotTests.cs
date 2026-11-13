using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// 人物存档：备份 → 改动 → 还原。
/// <para>旧版的「还原存档」是个空壳（<c>RestoreSaved</c> 整个方法体被注释掉，只返回「维护中」），
/// 所以这批测试是**新功能的规格**，不是等价对照：谁把还原做成「只退钱不退物」，
/// 或者让玩家靠还原刷物品，这里会直接红。</para>
/// </summary>
public sealed class SnapshotTests
{
    private const long Player = 880001;

    [Fact]
    public async Task Restore_BringsBackWalletCharacterAndItems()
    {
        using SnapshotDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        ISnapshotService snapshots = db.Get<ISnapshotService>();

        await players.RegisterAsync(Player);
        await TestItems.SeedAsync(db.Provider, Player, 2);

        PlayerSnapshot before = (await players.GetSnapshotAsync(Player))!;
        Assert.True((await snapshots.SaveAsync(Player, "打 Boss 前")).Ok);

        // 备份之后把状态改掉：烧掉一半金币、拆掉一件物品
        await SetWalletAsync(db, credits: before.Credits / 2, materials: before.Materials + 50);
        Assert.NotEmpty(await players.GetBagAsync(Player));
        Assert.True((await players.SalvageAsync(Player, 1, 1)).Ok);
        PlayerSnapshot changed = (await players.GetSnapshotAsync(Player))!;
        Assert.NotEqual(before.Credits, changed.Credits);
        Assert.Equal(before.ItemCount - 1, changed.ItemCount);

        ActionResult restored = await snapshots.RestoreLatestAsync(Player);

        Assert.True(restored.Ok, restored.Status);
        PlayerSnapshot after = (await players.GetSnapshotAsync(Player))!;
        Assert.Equal(before.Credits, after.Credits, 6);
        Assert.Equal(before.Materials, after.Materials, 6);
        Assert.Equal(before.ItemCount, after.ItemCount);
        Assert.Equal(before.CharacterCount, after.CharacterCount);
    }

    /// <summary>角色行是整块重建的（自增键会变），所以小队成员必须按 Guid 重新对上。</summary>
    [Fact]
    public async Task Restore_KeepsSquadMembersPointingAtTheRightCharacters()
    {
        using SnapshotDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        ISnapshotService snapshots = db.Get<ISnapshotService>();

        await players.RegisterAsync(Player);
        ActionResult squad = await players.SquadAsync(Player, "加入", 1);
        Assert.True(squad.Ok, squad.Status);

        Assert.True((await snapshots.SaveAsync(Player, "编好队")).Ok);
        Assert.True((await players.SquadAsync(Player, "移除", 1)).Ok);   // 备份之后把人撤了

        ActionResult restored = await snapshots.RestoreLatestAsync(Player);
        Assert.True(restored.Ok, restored.Status);

        PlayerSnapshot after = (await players.GetSnapshotAsync(Player))!;
        Assert.Equal(1, after.SquadCount);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        SquadMember member = await context.SquadMembers.SingleAsync();
        CoreCharacterRecord character = await context.CoreCharacters.SingleAsync();
        Assert.Equal(character.LocalId, member.CharacterId);   // 指向重建后的那一行，而不是快照里的旧键
    }

    [Fact]
    public async Task Restore_IsLimitedToOnePerGameDay()
    {
        using SnapshotDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        ISnapshotService snapshots = db.Get<ISnapshotService>();

        await players.RegisterAsync(Player);
        Assert.True((await snapshots.SaveAsync(Player, "")).Ok);
        Assert.True((await snapshots.RestoreLatestAsync(Player)).Ok);

        ActionResult second = await snapshots.RestoreLatestAsync(Player);
        Assert.False(second.Ok);
        Assert.Contains("今天已经还原过", second.Title);
    }

    /// <summary>
    /// 快照之后把物品卖给别人、再还原，等于同一件物品出现两次。
    /// 检测到快照之后的玩家间成交就拒绝，这条是反刷的核心。
    /// </summary>
    [Fact]
    public async Task Restore_RefusesWhenAPlayerTradeHappenedAfterTheSnapshot()
    {
        using SnapshotDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        ISnapshotService snapshots = db.Get<ISnapshotService>();

        await players.RegisterAsync(Player);
        Assert.True((await snapshots.SaveAsync(Player, "")).Ok);

        await AddLedgerAsync(db, "market_sale");

        ActionResult blocked = await snapshots.RestoreLatestAsync(Player);
        Assert.False(blocked.Ok);
        Assert.Contains("玩家间交易", blocked.Title);
        Assert.Contains(blocked.Lines, line => line.Contains("market_sale"));
    }

    [Fact]
    public async Task Restore_LeavesASafetySnapshotBehind()
    {
        using SnapshotDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        ISnapshotService snapshots = db.Get<ISnapshotService>();

        await players.RegisterAsync(Player);
        Assert.True((await snapshots.SaveAsync(Player, "手滑之前")).Ok);
        Assert.True((await snapshots.RestoreLatestAsync(Player)).Ok);

        IReadOnlyList<SnapshotSummary> all = await snapshots.ListAsync(Player);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, x => x.Kind == SnapshotRules.KindPreRestore);
        Assert.Contains(all, x => x.Kind == SnapshotRules.KindManual);
    }

    [Fact]
    public async Task Snapshots_ArePrunedToTheConfiguredLimit()
    {
        using SnapshotDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        ISnapshotService snapshots = db.Get<ISnapshotService>();

        await players.RegisterAsync(Player);
        for (int i = 0; i < SnapshotRules.MaxSnapshotsPerUser + 3; i++)
        {
            Assert.True((await snapshots.SaveAsync(Player, $"#{i}")).Ok);
        }

        IReadOnlyList<SnapshotSummary> all = await snapshots.ListAsync(Player);
        Assert.Equal(SnapshotRules.MaxSnapshotsPerUser, all.Count);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(SnapshotRules.MaxSnapshotsPerUser,
            await context.SaveSnapshots.CountAsync(x => x.UserId == Player));
    }

    [Fact]
    public async Task Restore_WithoutAnyBackupSaysSo()
    {
        using SnapshotDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        ISnapshotService snapshots = db.Get<ISnapshotService>();

        await players.RegisterAsync(Player);
        ActionResult result = await snapshots.RestoreLatestAsync(Player);

        Assert.False(result.Ok);
        Assert.Contains("没有可还原", result.Title);
    }

    private static async Task SetWalletAsync(SnapshotDatabase db, double credits, double materials)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == Player);
        wallet.Credits = credits;
        wallet.Materials = materials;
        await context.SaveChangesAsync();
    }

    /// <summary>伪造一条「快照之后发生的玩家间成交」流水。</summary>
    private static async Task AddLedgerAsync(SnapshotDatabase db, string reason)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == Player);
        context.LedgerEntries.Add(new LedgerEntry
        {
            UserId = Player, Currency = "credits", Delta = 100, BalanceAfter = wallet.Credits,
            Reason = reason, RefType = "listing", RefId = "1",
            // 快照是用假时钟建的，这里也用它，+1 分钟保证「之后」
            CreatedAtUtc = db.Clock.UtcNow.AddMinutes(1)
        });
        await context.SaveChangesAsync();
    }

    private sealed class SnapshotDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public FakeClock Clock { get; }

        public SnapshotDatabase()
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            Clock = new FakeClock(new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc));

            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddGameplayRuntime(Clock);
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IEconomyService, EconomyService>();
            services.AddSingleton<ISnapshotService, SnapshotService>();
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
