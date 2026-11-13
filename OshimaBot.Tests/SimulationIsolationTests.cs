using FunGame.Core.Entity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Services;
using Xunit;

namespace OshimaBot.Tests;

public sealed class SimulationIsolationTests
{
    [Fact]
    public void RunContext_MapAndRandomArePerInstance()
    {
        SimulationRunContext first = new();
        SimulationRunContext second = new();

        Assert.NotSame(first.Map, second.Map);
        Assert.NotSame(first.Random, second.Random);
        Assert.Equal("", first.Msg);
        Assert.Null(first.SeedOverride);
    }

    [Fact]
    public void BeginRun_IsolatesMutableStateBetweenRuns()
    {
        SimulationRunContext first = new() { Msg = "A", SeedOverride = 11 };
        SimulationRunContext second = new() { Msg = "B", SeedOverride = 22 };

        using (FunGameSimulation.BeginRun(first))
        {
            FunGameSimulation.Msg += "1";
            FunGameSimulation.WriteLine("2");
            FunGameSimulation.IsRuning = true;
            Assert.Equal("A12\r\n", first.Msg);
            Assert.Equal(11, FunGameSimulation.SeedOverride);
            Assert.True(FunGameSimulation.IsRuning);
        }
        Assert.NotSame(first, FunGameSimulation.Current);

        using (FunGameSimulation.BeginRun(second))
        {
            Assert.Equal("B", FunGameSimulation.Msg);
            Assert.Equal(22, FunGameSimulation.SeedOverride);
            Assert.False(FunGameSimulation.IsRuning); // 不串上一局的运行中标记
        }
        Assert.Equal("A12\r\n", first.Msg);
        Assert.Equal("B", second.Msg);
    }

    [Fact]
    public void BeginRun_NestedScopeRestoresPreviousContext()
    {
        SimulationRunContext outer = new() { Msg = "OUT" };
        using (FunGameSimulation.BeginRun(outer))
        {
            using (FunGameSimulation.BeginRun(new SimulationRunContext { Msg = "IN" }))
            {
                Assert.Equal("IN", FunGameSimulation.Msg);
            }
            Assert.Equal("OUT", FunGameSimulation.Msg);
        }
    }

    [Fact]
    public async Task BeginRun_FlowsIntoTaskRun()
    {
        SimulationRunContext context = new() { Msg = "X" };
        using (FunGameSimulation.BeginRun(context))
        {
            await Task.Run(() => FunGameSimulation.WriteLine("Y"));
        }
        Assert.Equal("XY\r\n", context.Msg);
    }

    [Fact]
    public void StaticStatistics_AreBackedByStore()
    {
        Assert.Same(FunGameSimulation.StatisticsStore.CharacterStatistics,
            FunGameSimulation.CharacterStatistics);
        Assert.Same(FunGameSimulation.StatisticsStore.TeamCharacterStatistics,
            FunGameSimulation.TeamCharacterStatistics);
        Assert.NotNull(FunGameSimulation.StatisticsStore.Read(store => store.LastRecord));
    }

    [Fact]
    public void StatisticsStore_ResetRebuildsRowsForCurrentCatalog()
    {
        FunGameRuntime.EnsureInitialized();
        InMemorySimulationStatisticsStore store = new();
        store.LastRecord = ["stale"];

        store.Reset(FunGameService.Characters);

        Assert.Equal(FunGameService.Characters.Count, store.CharacterStatistics.Count);
        Assert.Equal(FunGameService.Characters.Count, store.TeamCharacterStatistics.Count);
        Assert.Empty(store.LastRecord);
        Assert.Empty(store.LastFullRecord);
        Assert.All(store.CharacterStatistics.Values, stats => Assert.NotNull(stats));
    }

    [Fact]
    public async Task SimulationService_ReadsLastRecordFromStoreNotStatics()
    {
        InMemorySimulationStatisticsStore store = new();
        store.LastRecord = ["A"];
        store.LastFullRecord = ["A", "B"];
        FunGameSimulationService service = new(store, new NoopRunRecorder(),
            Options.Create(new ServerOptions()), NullLogger<FunGameSimulationService>.Instance);

        Assert.Equal(new[] { "A" }, await service.GetLastAsync(false));
        Assert.Equal(2, (await service.GetLastAsync(true)).Count);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task QueryService_ReadsStatisticsFromInjectedStore()
    {
        FunGameRuntime.EnsureInitialized();
        InMemorySimulationStatisticsStore store = new();
        Character character = FunGameService.Characters[0];
        store.CharacterStatistics[character] = new CharacterStatistics();
        FunGameQueryService queries = new(store, new SystemRandomSource());

        string stats = await queries.GetStatsAsync(1, false);
        Assert.Contains("参赛", stats);

        List<string> ranking = await queries.GetRankingAsync(false, true);
        Assert.Single(ranking);
        Assert.Contains("技术得分", ranking[0]);
    }

    private sealed class NoopRunRecorder : ISimulationRunRecorder
    {
        public Task<long> RecordAsync(SimulationRunRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);
    }
}
