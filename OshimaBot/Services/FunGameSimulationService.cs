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

    public sealed class FunGameSimulationService(IOptions<ServerOptions> options)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly int _maximumRespawnTimes = options.Value.MaximumRespawnTimes;

        public bool IsRunning => FunGameSimulation.IsRuning;

        public async Task<SimulationResult?> RunAsync(SimulationRequest request, CancellationToken cancellationToken = default)
        {
            if (!await _gate.WaitAsync(0, cancellationToken)) return null;

            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            try
            {
                FunGameSimulation.SeedOverride = request.Seed is > 0 ? request.Seed : null;
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
                    () => FunGameSimulation.StartSimulationGame(simulationOptions),
                    CancellationToken.None);

                return new SimulationResult(
                    messages,
                    FunGameSimulation.Seed,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow - startedAt);
            }
            finally
            {
                FunGameSimulation.SeedOverride = null;
                _gate.Release();
            }
        }

        public async Task<T> ReadAsync<T>(Func<T> reader, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                return reader();
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<IReadOnlyList<string>> GetLastAsync(bool full, CancellationToken cancellationToken = default)
            => ReadAsync(() =>
            {
                IReadOnlyList<string> source = full ? FunGameSimulation.LastFullRecord : FunGameSimulation.LastRecord;
                return (IReadOnlyList<string>)[.. source];
            }, cancellationToken);
    }
}
