using System.Text.Json;
using Microsoft.Extensions.Options;
using OshimaBot.Game.Services;
using OshimaBot.Models;

namespace OshimaBot.Services
{
    public sealed class SimulationRequest
    {
        public bool IsWeb { get; set; } = true;
        public bool IsTeam { get; set; }
        public bool ShowAll { get; set; }
        public int MaxRespawnTimesMix { get; set; } = 1;
        public bool HasMap { get; set; }
        public bool IsDebug { get; set; }
        public bool UseStore { get; set; }
        public bool EnablePeriodicDrop { get; set; } = true;
        public bool BindToCharacter { get; set; } = true;
        public int CharacterLevel { get; set; } = 10;
        public int SkillLevel { get; set; } = 2;
        public int NormalAttackLevel { get; set; } = 2;
        public long FixedWeaponId { get; set; }
        public int? Seed { get; set; }

        /// <summary>S9：触发模拟的内部用户，写入 simulation_runs.user_id；第三方/匿名请求为 null。</summary>
        public long? UserId { get; set; }
    }

    public sealed record SimulationResult(
        IReadOnlyList<string> Messages,
        int Seed,
        DateTimeOffset CompletedAt,
        TimeSpan Duration);

    /// <summary>
    /// S8 前置：每次运行创建独立的 SimulationRunContext，静态运行态不再进程级共享。
    /// S9：一局结束后把结果与统计写入 SQLite（simulation_runs / simulation_statistics），
    /// 内存仓储只作为模拟器运行期的工作副本，重启后由 LoadAsync 恢复。
    /// 引擎仍串行执行（FunGame.Core 目录为只读共享）。
    /// </summary>
    public sealed class FunGameSimulationService(
        ISimulationStatisticsStore statistics,
        ISimulationRunRecorder recorder,
        IOptions<ServerOptions> options,
        ILogger<FunGameSimulationService> logger)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly int _maximumRespawnTimes = options.Value.MaximumRespawnTimes;
        private readonly bool _archiveRounds = options.Value.ArchiveSimulationRounds;
        private readonly bool _storeFullLog = options.Value.StoreFullSimulationLog;

        public bool IsRunning => _gate.CurrentCount == 0;

        public async Task<SimulationResult?> RunAsync(SimulationRequest request, CancellationToken cancellationToken = default)
        {
            if (!await _gate.WaitAsync(0, cancellationToken)) return null;

            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            try
            {
                SimulationRunContext context = new()
                {
                    SeedOverride = request.Seed is > 0 ? request.Seed : null
                };
                SimulationOptions simulationOptions = new()
                {
                    PrintOut = false,
                    IsWeb = request.IsWeb,
                    IsTeam = request.IsTeam,
                    DeathMatchRoundDetail = request.ShowAll,
                    MaxRespawnTimesMix = Math.Clamp(request.MaxRespawnTimesMix, 0, _maximumRespawnTimes),
                    UseStore = request.UseStore,
                    HasMap = request.HasMap,
                    IsDebug = request.IsDebug,
                    BindToCharacter = request.BindToCharacter,
                    CharacterLevel = Math.Clamp(request.CharacterLevel, 1, 60),
                    SkillLevel = Math.Clamp(request.SkillLevel, 1, 8),
                    NormalAttackLevel = Math.Clamp(request.NormalAttackLevel, 1, 8),
                    EnablePeriodicDrop = request.EnablePeriodicDrop,
                    FixedWeaponId = request.FixedWeaponId,
                    ArchiveRounds = _archiveRounds
                };

                List<string> messages = await Task.Run(
                    () => FunGameSimulation.StartSimulationGame(simulationOptions, context),
                    cancellationToken);

                DateTimeOffset finishedAt = DateTimeOffset.UtcNow;
                SimulationResult result = new(messages, context.Seed, finishedAt, finishedAt - startedAt);
                await PersistRunAsync(request, context.Seed, messages, startedAt, finishedAt, cancellationToken);
                return result;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>持久化失败只记日志，不丢弃已经算完的模拟结果。</summary>
        private async Task PersistRunAsync(
            SimulationRequest request, int seed, IReadOnlyList<string> messages,
            DateTimeOffset startedAt, DateTimeOffset finishedAt, CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<string> full = _storeFullLog
                    ? statistics.Read(store => (IReadOnlyList<string>)[.. store.LastFullRecord])
                    : [];
                await recorder.RecordAsync(new SimulationRunRecord(
                    request.UserId, seed, JsonSerializer.Serialize(request), messages, full,
                    startedAt, finishedAt), cancellationToken);
                await statistics.PersistAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "模拟结果入库失败：Seed={Seed}", seed);
            }
        }

        /// <summary>读锁内读取仓储；保留给外部只读调用。</summary>
        public Task<T> ReadAsync<T>(Func<T> reader, CancellationToken cancellationToken = default)
            => Task.FromResult(statistics.Read(_ => reader()));

        public Task<IReadOnlyList<string>> GetLastAsync(bool full, CancellationToken cancellationToken = default)
            => Task.FromResult(statistics.Read(store =>
            {
                IReadOnlyList<string> source = full ? store.LastFullRecord : store.LastRecord;
                return (IReadOnlyList<string>)[.. source];
            }));
    }
}
