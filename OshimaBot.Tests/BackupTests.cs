using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 数据库备份（运维）。用 <c>VACUUM INTO</c> 而不是复制文件：库开着 WAL，
/// 直接拷可能拿到只写了一半的页；VACUUM INTO 是一致性读，服务运行中也能跑。
/// </summary>
public sealed class BackupTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "oshimobot-backup-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响结论
        }
    }

    [Fact]
    public async Task Backup_WritesAConsistentSnapshot()
    {
        using BackupDatabase db = new(_directory);
        IBackupService backups = db.Get<IBackupService>();

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            context.CorePlayers.Add(new CorePlayerRecord { UserId = 4321, Credits = 1234.5 });
            await context.SaveChangesAsync();
        }

        BackupResult result = await backups.BackupAsync();

        Assert.True(File.Exists(result.Path));
        Assert.True(result.Bytes > 0);
        Assert.EndsWith(".db", result.Path);
        Assert.StartsWith("oshimobot-", Path.GetFileName(result.Path));

        // 备份文件必须是一个能读的 SQLite 库，且带着备份前的数据
        await using SqliteConnection probe = new($"Data Source={result.Path};Mode=ReadOnly");
        await probe.OpenAsync();
        await using SqliteCommand command = probe.CreateCommand();
        command.CommandText = "SELECT Credits FROM CorePlayers WHERE UserId = 4321";
        object? credits = await command.ExecuteScalarAsync();
        Assert.Equal(1234.5, Convert.ToDouble(credits), 3);

        Assert.Single(backups.List());
    }

    [Fact]
    public async Task Backup_KeepsOnlyTheNewestTen()
    {
        using BackupDatabase db = new(_directory);
        IBackupService backups = db.Get<IBackupService>();
        Directory.CreateDirectory(_directory);

        // 先放 12 个旧文件（名字按时间递增，排序可预期）
        for (int i = 0; i < 12; i++)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_directory, $"oshimobot-2020010{i % 10}-00000{i % 10}-000.db"), "old");
        }

        await backups.BackupAsync();

        IReadOnlyList<BackupEntry> listed = backups.List(50);
        Assert.Equal(BackupService.KeepCount, listed.Count);
        Assert.Equal(10, Directory.GetFiles(_directory, "oshimobot-*.db").Length);
        // 新备份不会被自己删掉
        Assert.Contains(listed, x => x.Name.StartsWith("oshimobot-2", StringComparison.Ordinal));
    }

    private sealed class BackupDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public BackupDatabase(string backupDirectory)
        {
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new DatabaseOptions
            {
                ApplyMigrationsOnStartup = false,
                BackupDirectory = backupDirectory
            }));
            services.AddSingleton<IGameClock>(new FakeClock(new DateTime(2026, 11, 14, 22, 0, 0, DateTimeKind.Utc)));
            services.AddSingleton<IBackupService, BackupService>();

            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
