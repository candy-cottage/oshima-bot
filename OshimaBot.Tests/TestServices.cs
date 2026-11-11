using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Data;
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

/// <summary>
/// 旧版注册不给任何初始物品（0 件），所以需要物品的测试必须自己造。
/// </summary>
internal static class TestItems
{
    /// <summary>用内容包的前 N 件物品垫底（与旧版「注册不带物品」解耦，测试需要什么就自己发）。</summary>
    public static async Task SeedAsync(IServiceProvider provider, long userId, int count)
    {
        using IServiceScope scope = provider.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var templates = OshimaBot.Game.Services.FunGameService.AllItems;
        for (int i = 0; i < Math.Min(count, templates.Count); i++)
        {
            var template = templates[i];
            await ItemStore.AddAsync(db, userId, template.Name, 1, 1, CancellationToken.None,
                template.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        await db.SaveChangesAsync();
    }
}

/// <summary>必定命中抽卡的随机源：<c>NextDouble() = 0.9</c>，用于验证费用与入包逻辑。</summary>
internal sealed class AlwaysHitRandomSource : IRandomSource
{
    public int Next(int maxExclusive) => 0;

    public int Next(int minInclusive, int maxExclusive) => minInclusive;

    public double NextDouble() => 0.9;
}
