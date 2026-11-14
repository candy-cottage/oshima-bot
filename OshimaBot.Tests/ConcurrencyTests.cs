using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 乐观并发的回归。
/// <para>背景：<c>Version</c> 被配置成 <c>IsConcurrencyToken()</c>，但 EF 不会自己递增它。
/// 之前全仓没有任何地方 <c>Version++</c>，令牌恒为 0，两个并发写都能提交——后写的静默覆盖先写的，
/// <c>MutateAsync</c> 的 <c>DbUpdateConcurrencyException</c> 分支等于死代码。
/// 现在 <see cref="BotDbContext.SaveChanges(bool)"/> 会在保存前把 Modified 行的 Version +1，
/// 这条用例保证它真的会拦下第二个写入者。</para>
/// </summary>
public sealed class ConcurrencyTests : IDisposable
{
    private const long Player = 7001;

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public ConcurrencyTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<BotDbContext>(options => options.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private BotDbContext NewContext() => _provider.CreateScope().ServiceProvider.GetRequiredService<BotDbContext>();

    [Fact]
    public async Task VersionToken_RejectsTheSecondWriter()
    {
        await using (BotDbContext seed = NewContext())
        {
            seed.CorePlayers.Add(new CorePlayerRecord
            {
                UserId = Player, Credits = 100, Materials = 0,
                UpdatedAtUtc = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc)
            });
            await seed.SaveChangesAsync();
        }

        // 两个上下文各读一份，模拟两条消息同时处理同一个玩家。
        await using BotDbContext first = NewContext();
        await using BotDbContext second = NewContext();
        CorePlayerRecord a = await first.CorePlayers.SingleAsync(x => x.UserId == Player);
        CorePlayerRecord b = await second.CorePlayers.SingleAsync(x => x.UserId == Player);
        Assert.Equal(0, a.Version);

        a.Credits += 100;              // 第一条：100 → 200
        b.Credits += 500;              // 第二条基于同一个旧值：100 → 600
        await first.SaveChangesAsync();

        Assert.Equal(1, a.Version);    // 保存一次就 +1，否则令牌永远不生效

        // 第二条必须被拒：否则玩家会拿到 600，第一条的 100 白扣/白加。
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using BotDbContext verify = NewContext();
        Assert.Equal(200, (await verify.CorePlayers.SingleAsync(x => x.UserId == Player)).Credits);
    }

    [Fact]
    public async Task VersionToken_AllowsSequentialWrites()
    {
        await using (BotDbContext seed = NewContext())
        {
            seed.CorePlayers.Add(new CorePlayerRecord { UserId = Player, Credits = 0 });
            await seed.SaveChangesAsync();
        }

        // 顺序写不该被拦：每次重新读，Version 跟着走。
        for (int i = 1; i <= 3; i++)
        {
            await using BotDbContext db = NewContext();
            CorePlayerRecord wallet = await db.CorePlayers.SingleAsync(x => x.UserId == Player);
            wallet.Credits += 10;
            await db.SaveChangesAsync();
            Assert.Equal(i, wallet.Version);
        }

        await using BotDbContext final = NewContext();
        Assert.Equal(30, (await final.CorePlayers.SingleAsync(x => x.UserId == Player)).Credits);
    }
}
