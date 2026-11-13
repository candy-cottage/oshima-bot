using System.Globalization;

namespace OshimaBot.Gameplay;

/// <summary>
/// 可注入的时钟：业务判定（签到日、任务日、冷却比较、定时任务起止）统一走它，
/// 便于用假时钟验证「到点可结算」这类逻辑。
/// <para>审计字段（<c>CreatedAtUtc</c> / <c>UpdatedAtUtc</c>）仍用系统时间，不影响可复现性。</para>
/// </summary>
public interface IGameClock
{
    /// <summary>当前 UTC 时间。</summary>
    DateTime UtcNow { get; }

    /// <summary>游戏日（Asia/Shanghai）<c>yyyy-MM-dd</c>。</summary>
    string Today();
}

/// <summary>基于 <see cref="TimeProvider"/> 的默认时钟；测试可注入 <c>FakeTimeProvider</c>。</summary>
public sealed class GameClock(TimeProvider provider) : IGameClock
{
    public DateTime UtcNow => provider.GetUtcNow().UtcDateTime;

    public string Today() => GameplayRules.GameDay(UtcNow);
}

/// <summary>
/// 可注入的随机源。业务公式里禁止直接用 <c>Random.Shared</c>，
/// 否则同种子回归与概率验证都做不了。
/// </summary>
public interface IRandomSource
{
    int Next(int maxExclusive);

    int Next(int minInclusive, int maxExclusive);

    double NextDouble();
}

/// <summary>进程级随机（等价于原来的 <c>Random.Shared</c>，只是换成了可替换的接缝）。</summary>
public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int maxExclusive) => Random.Shared.Next(maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);

    public double NextDouble() => Random.Shared.NextDouble();
}

/// <summary>可复现的随机源（线性同余），用于测试与对拍。</summary>
public sealed class SeededRandomSource(int seed) : IRandomSource
{
    private readonly Random _random = new(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    public double NextDouble() => _random.NextDouble();
}

/// <summary>
/// 把 <see cref="IRandomSource"/> 包成 <see cref="Random"/>，用于只能接收 <c>Random</c> 的
/// Core 接口（例如魔法卡预览 <c>GenerateMagicCard(Random)</c>）。
/// <para>包一层以后预览也受种子控制，同种子回归才能覆盖到它。</para>
/// </summary>
public sealed class RandomSourceAdapter(IRandomSource source) : Random
{
    public override int Next() => source.Next(int.MaxValue);

    public override int Next(int maxValue) => source.Next(maxValue);

    public override int Next(int minValue, int maxValue) => source.Next(minValue, maxValue);

    public override double NextDouble() => source.NextDouble();
}
