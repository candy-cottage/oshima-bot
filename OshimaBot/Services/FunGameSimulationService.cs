using OshimaBot.Game.Services;
using OshimaBot.Models;
using Microsoft.Extensions.Options;

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
    }

    public sealed record SimulationResult(
        IReadOnlyList<string> Messages,
        int Seed,
        DateTimeOffset CompletedAt,
        TimeSpan Duration);

    /// <summary>
    /// S8 前置：每次运行创建独立的 SimulationRunContext，静态运行态不再进程级共享；
    /// 统计与最近日志改由 ISimulationStatisticsStore 持有。引擎仍串行执行（FunGame.Core 目录为只读共享），
    /// 但两个运行之间不会串种子 / 消息 / 地图 / 运行状态。
    /// </summary>
    public sealed class FunGameSimulationService(
        ISimulationStatisticsStore statistics,
        IOptions<ServerOptions> options)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly int _maximumRespawnTimes = options.Value.MaximumRespawnTimes;

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
                    FixedWeaponId = request.FixedWeaponId
                };

                List<string> messages = await Task.Run(
                    () => FunGameSimulation.StartSimulationGame(simulationOptions, context),
                    cancellationToken);

                return new SimulationResult(messages, context.Seed, DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow - startedAt);
            }
            finally
            {
                _gate.Release();
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
