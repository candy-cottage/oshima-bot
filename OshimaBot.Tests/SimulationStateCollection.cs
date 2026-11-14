using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// <c>FunGameSimulation</c> 的运行态与统计仓储是**进程级静态**：跑一局会写 <c>CharacterStatistics</c>，
/// 而 <c>SimulationPersistenceTests</c> 会临时替换全局仓储。涉及这些的测试不能并行，否则会互相串数据。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SimulationStateCollection
{
    public const string Name = "SimulationState";
}
