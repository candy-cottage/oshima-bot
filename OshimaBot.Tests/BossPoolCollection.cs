using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// <c>BossPool</c> 是进程级的全服共享状态，涉及它的测试不能并行，否则会互相改写目标血量。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BossPoolCollection
{
    public const string Name = "BossPool";
}
