using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Models;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 迁移能不能真的跑起来（2026-11-15 补）。
/// <para>测试里的库一律 <c>EnsureCreated()</c>——按**当前模型**建表，绕过了迁移。
/// 于是迁移写错（漏了索引、类型不对、快照漂移）时，本地测试全绿，只有部署到真库才会炸。
/// 这条用例把「从空库一路迁移到最新」真跑一遍，至少能拦住「迁移本身不成立」。</para>
/// </summary>
public sealed class DatabaseMigrationTests
{
    [Fact]
    public void Migrations_ApplyToAnEmptyDatabase()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        DbContextOptions<BotDbContext> options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(connection)
            .Options;
        using (BotDbContext db = new(options))
        {
            db.Database.Migrate();   // 从零按顺序执行全部迁移
        }

        Assert.Contains("AdminGrants", TableNames(connection));
        Assert.Contains("CorePlayers", TableNames(connection));
        Assert.Contains("LedgerEntries", TableNames(connection));
        Assert.Contains("RenameRequests", TableNames(connection));
    }

    /// <summary>
    /// 迁移完成要打「就绪」信号：网关等它，否则首次启动会抢在建表之前查 gateway_sessions。
    /// 这里连 InitializeAsync 与 readiness 一起跑，防止有人把 MarkReady 挪走或漏掉。
    /// </summary>
    [Fact]
    public async Task Initializer_MarksReadinessOnlyAfterMigrating()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<BotDbContext>(options => options.UseSqlite(connection));
        await using ServiceProvider provider = services.BuildServiceProvider();

        DatabaseReadiness readiness = new();
        Assert.False(readiness.Ready.IsCompleted);

        DatabaseInitializer initializer = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = true }),
            Options.Create(new ServerOptions()),
            readiness,
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);   // ExecuteAsync 里迁移完才 MarkReady

        await readiness.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("GatewaySessions", TableNames(connection));
    }

    private static List<string> TableNames(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        using SqliteDataReader reader = command.ExecuteReader();
        List<string> names = [];
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }
}
