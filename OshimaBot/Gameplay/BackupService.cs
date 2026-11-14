using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Models;

namespace OshimaBot.Gameplay;

/// <summary>一次备份的结果。</summary>
public sealed record BackupResult(string Path, long Bytes, string TakenAt, int KeptCount);

/// <summary>已有的备份文件。</summary>
public sealed record BackupEntry(string Name, long Bytes, string TakenAt);

public interface IBackupService
{
    Task<BackupResult> BackupAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<BackupEntry> List(int limit = 10);
}

/// <summary>
/// 数据库快照（运维用）。
/// <para>用 SQLite 的 <c>VACUUM INTO</c> 而不是复制文件：库开着 WAL，直接 <c>cp</c> 可能拿到
/// 只写了一半的页；<c>VACUUM INTO</c> 是事务性读取，跑在服务运行期间也能得到一致快照。</para>
/// <para>只做备份，不做还原——还原需要在服务停止后替换文件，见 <c>ops/README.md</c> 的步骤。</para>
/// </summary>
public sealed class BackupService(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    IGameClock clock,
    ILogger<BackupService> logger) : IBackupService
{
    /// <summary>保留最近多少份，避免把磁盘吃满。</summary>
    public const int KeepCount = 10;

    public async Task<BackupResult> BackupAsync(CancellationToken cancellationToken = default)
    {
        string directory = ResolveDirectory();
        Directory.CreateDirectory(directory);

        // 带毫秒：连打两次备份时不会撞名（VACUUM INTO 遇到已存在的文件会直接失败）。
        string stamp = clock.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        string path = Path.Combine(directory, $"oshimobot-{stamp}.db");

        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        // VACUUM 不能在事务里跑，所以这里不开事务——它本身就是一致性读。
        // 路径走参数绑定，避免把配置里的引号拼进 SQL。
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [path], cancellationToken);

        int kept = Prune(directory);
        long bytes = new FileInfo(path).Length;
        logger.LogInformation("数据库备份完成：{Path}（{Bytes} 字节），保留 {Kept} 份", path, bytes, kept);
        return new BackupResult(path, bytes, stamp, kept);
    }

    public IReadOnlyList<BackupEntry> List(int limit = 10)
    {
        string directory = ResolveDirectory();
        if (!Directory.Exists(directory)) return [];

        return [.. new DirectoryInfo(directory).GetFiles("oshimobot-*.db")
            .OrderByDescending(x => x.Name)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(x => new BackupEntry(x.Name, x.Length, x.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))];
    }

    private string ResolveDirectory()
    {
        string configured = string.IsNullOrWhiteSpace(options.Value.BackupDirectory)
            ? "backups"
            : options.Value.BackupDirectory;
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    private static int Prune(string directory)
    {
        FileInfo[] files = [.. new DirectoryInfo(directory).GetFiles("oshimobot-*.db")
            .OrderByDescending(x => x.Name)];
        foreach (FileInfo stale in files.Skip(KeepCount))
        {
            try
            {
                stale.Delete();
            }
            catch (IOException)
            {
                // 删不掉就留着，下次备份再试——不能因为清理失败把备份本身搞失败。
            }
        }
        return Math.Min(files.Length, KeepCount);
    }
}
