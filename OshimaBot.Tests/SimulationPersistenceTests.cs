using FunGame.Core.Entity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Game.Services;
using OshimaBot.Models;
using OshimaBot.Services;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>S9：模拟统计 / 日志数据库化，验证重启恢复与默认值。</summary>
public sealed class SimulationPersistenceTests
{
    [Fact]
    public async Task Restart_RestoresStatisticsAndLastLogs()
    {
        using PersistenceDatabase db = new();
        EfSimulationStatisticsStore first = db.CreateStore();
        await first.LoadAsync(); // 空库
        first.Reset(FunGameService.Characters);

        Character character = FunGameService.Characters[0];
        first.CharacterStatistics[character].Plays = 7;
        first.TeamCharacterStatistics[character].Wins = 3;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await first.RecordAsync(new SimulationRunRecord(
            42, 12345, "{\"IsTeam\":false}", ["精简A", "精简B"],
            ["完整A", "完整B", "完整C"], now, now.AddSeconds(2)));
        await first.PersistAsync();

        // 全新 store 实例 + 同一数据库 = 进程重启
        EfSimulationStatisticsStore second = db.CreateStore();
        await second.LoadAsync();

        Assert.Equal(FunGameService.Characters.Count, second.CharacterStatistics.Count);
        Assert.Equal(7, second.CharacterStatistics[character].Plays);
        Assert.Equal(3, second.TeamCharacterStatistics[character].Wins);
        Assert.Equal(new[] { "精简A", "精简B" }, second.LastRecord);
        Assert.Equal(3, second.LastFullRecord.Count);
        Assert.Equal("完整C", second.LastFullRecord[2]);
    }

    [Fact]
    public async Task Persist_IsUpsertAndNotDuplicate()
    {
        using PersistenceDatabase db = new();
        EfSimulationStatisticsStore store = db.CreateStore();
        await store.LoadAsync();
        store.Reset(FunGameService.Characters);

        await store.PersistAsync();
        int firstCount = await db.CountStatisticsAsync();
        await store.PersistAsync();
        int secondCount = await db.CountStatisticsAsync();

        Assert.Equal(FunGameService.Characters.Count * 2, firstCount); // character + team
        Assert.Equal(firstCount, secondCount);
    }

    [Fact]
    public async Task RecordAsync_WritesSimulationRunRow()
    {
        using PersistenceDatabase db = new();
        EfSimulationStatisticsStore store = db.CreateStore();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        long id = await store.RecordAsync(new SimulationRunRecord(
            99, 777, "{}", ["结果"], ["完整"], now, now.AddSeconds(1)));

        Assert.True(id > 0);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var run = await context.SimulationRuns.SingleAsync();
        Assert.Equal("777", run.Seed);
        Assert.Equal(99, run.UserId);
        Assert.Equal("[\"结果\"]", run.ResultJson);
        Assert.Equal("[\"完整\"]", run.RoundsJson);
        Assert.NotNull(run.FinishedAtUtc);
    }

    [Fact]
    public async Task LoadAsync_ToleratesCorruptStatsJson()
    {
        using PersistenceDatabase db = new();
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            context.SimulationStatistics.Add(new OshimaBot.Data.Entities.SimulationStatistic
            {
                Scope = "character",
                CatalogId = FunGameService.Characters[0].Id.ToString(),
                StatsJson = "{ 不是合法 JSON",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        EfSimulationStatisticsStore store = db.CreateStore();
        await store.LoadAsync();

        Assert.Equal(0, store.CharacterStatistics[FunGameService.Characters[0]].Plays);
    }

    [Fact]
    public async Task RealSimulation_IsRecordedAndPersisted()
    {
        using PersistenceDatabase db = new();
        EfSimulationStatisticsStore store = db.CreateStore();
        await store.LoadAsync();
        store.Reset(FunGameService.Characters);

        // 与生产一致：模拟器静态委托也指向同一个 EF 仓储，结束后恢复全局实例避免污染其它测试。
        ISimulationStatisticsStore previous = FunGameSimulation.StatisticsStore;
        FunGameSimulation.UseStatisticsStore(store);
        try
        {
            FunGameSimulationService service = new(store, store,
                Options.Create(new ServerOptions()), NullLogger<FunGameSimulationService>.Instance);
            SimulationResult? result = await service.RunAsync(new SimulationRequest
            {
                UserId = 42, Seed = 20261110, MaxRespawnTimesMix = 0
            });

            Assert.NotNull(result);
            Assert.Equal(20261110, result.Seed);
            Assert.NotEmpty(await service.GetLastAsync(false));
            Assert.NotNull(await service.GetLastAsync(true));

            using IServiceScope scope = db.Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            Assert.Equal(1, await context.SimulationRuns.CountAsync());
            Assert.Equal(42, (await context.SimulationRuns.SingleAsync()).UserId);
            Assert.True(await context.SimulationStatistics.CountAsync() > 0);
        }
        finally
        {
            FunGameSimulation.UseStatisticsStore(previous);
        }
    }

    [Fact]
    public void Archiving_IsOffByDefault()
    {
        Assert.False(new SimulationOptions().ArchiveRounds);
        Assert.False(new SimulationRunContext().ArchiveRounds);
        Assert.False(new ServerOptions().ArchiveSimulationRounds);
        Assert.True(new ServerOptions().StoreFullSimulationLog);
    }

    [Fact]
    public async Task InMemoryStore_PersistIsNoOp()
    {
        InMemorySimulationStatisticsStore store = new();
        await store.PersistAsync();
        await store.PersistAsync();
        Assert.Empty(store.CharacterStatistics);
    }

    private sealed class PersistenceDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public PersistenceDatabase()
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public EfSimulationStatisticsStore CreateStore() => new(
            Provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EfSimulationStatisticsStore>.Instance);

        public async Task<int> CountStatisticsAsync()
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return await context.SimulationStatistics.CountAsync();
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
