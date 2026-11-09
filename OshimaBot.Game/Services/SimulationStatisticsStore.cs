using FunGame.Core.Entity;

namespace OshimaBot.Game.Services;

/// <summary>
/// 跨运行共享的统计与最近日志。S8 前置把读写从 FunGameSimulation 的静态字典收敛到仓储接口；
/// S9 可替换为 EF Core（simulation_statistics / simulation_runs）实现而无需改动模拟器。
/// </summary>
public interface ISimulationStatisticsStore
{
    Dictionary<Character, CharacterStatistics> CharacterStatistics { get; }
    Dictionary<Character, CharacterStatistics> TeamCharacterStatistics { get; }
    IReadOnlyList<string> LastRecord { get; set; }
    IReadOnlyList<string> LastFullRecord { get; set; }

    /// <summary>按当前内容目录重建统计行（进程启动或目录热重载时调用）。</summary>
    void Reset(IEnumerable<Character> characters);

    /// <summary>在读锁内执行读取，避免与正在写入的模拟互相干扰。</summary>
    T Read<T>(Func<ISimulationStatisticsStore, T> reader);
}

public sealed class InMemorySimulationStatisticsStore : ISimulationStatisticsStore
{
    private readonly Lock _sync = new();

    public Dictionary<Character, CharacterStatistics> CharacterStatistics { get; } = [];
    public Dictionary<Character, CharacterStatistics> TeamCharacterStatistics { get; } = [];
    public IReadOnlyList<string> LastRecord { get; set; } = [];
    public IReadOnlyList<string> LastFullRecord { get; set; } = [];

    public void Reset(IEnumerable<Character> characters)
    {
        lock (_sync)
        {
            CharacterStatistics.Clear();
            TeamCharacterStatistics.Clear();
            LastRecord = [];
            LastFullRecord = [];
            foreach (Character character in characters)
            {
                CharacterStatistics.Add(character, new());
                TeamCharacterStatistics.Add(character, new());
            }
        }
    }

    public T Read<T>(Func<ISimulationStatisticsStore, T> reader)
    {
        lock (_sync) return reader(this);
    }
}
