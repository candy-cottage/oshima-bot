using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Gameplay;

namespace OshimaBot.Tests;

/// <summary>
/// 测试用的玩法服务注册：把 <see cref="IGameClock"/> / <see cref="IRandomSource"/> 一起挂上，
/// 需要固定时间或固定随机时传入假实现即可。
/// </summary>
internal static class TestServices
{
    public static IServiceCollection AddGameplayRuntime(
        this IServiceCollection services, IGameClock? clock = null, IRandomSource? random = null)
    {
        services.AddSingleton(clock ?? new GameClock(TimeProvider.System));
        services.AddSingleton(random ?? new SystemRandomSource());
        return services;
    }
}

/// <summary>可手拨的假时钟，用于验证「到点可结算」这类时间相关逻辑。</summary>
internal sealed class FakeClock(DateTime startUtc) : IGameClock
{
    public DateTime UtcNow { get; private set; } = startUtc;

    public string Today() => GameplayRules.GameDay(UtcNow);

    public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
}
