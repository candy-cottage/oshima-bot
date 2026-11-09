using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OshimaBot.Models;

namespace OshimaBot.Data;

/// <summary>
/// 放在 IHostedService 中而非 Program 启动代码里：EF CLI 设计时只构建 Host、不启动 HostedService，
/// 从而避免生成 Migration 时反过来尝试执行 Migration。
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("已跳过数据库自动迁移（Database:ApplyMigrationsOnStartup=false）。");
            return;
        }

        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        await db.Database.MigrateAsync(stoppingToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", stoppingToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", stoppingToken);
        logger.LogInformation("数据库迁移完成，已启用 WAL 与外键约束。");
    }
}
