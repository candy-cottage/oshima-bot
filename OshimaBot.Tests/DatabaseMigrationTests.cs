using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
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
