using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OshimaBot.Data.Entities;
using OshimaBot.Models;
using OshimaBot.Security;
using OshimaBot.Services;

namespace OshimaBot.Data;

/// <summary>
/// 放在 IHostedService 中而非 Program 启动代码里：EF CLI 设计时只构建 Host、不启动 HostedService，
/// 从而避免生成 Migration 时反过来尝试执行 Migration。
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    IOptions<ServerOptions> serverOptions,
    ILogger<DatabaseInitializer> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => InitializeAsync(stoppingToken);

    /// <summary>公开以便集成测试直接等待完成（BackgroundService.StartAsync 不会等待 ExecuteAsync）。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        if (options.Value.ApplyMigrationsOnStartup)
        {
            await db.Database.MigrateAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken);
            logger.LogInformation("数据库迁移完成，已启用 WAL 与外键约束。");
        }
        else
        {
            logger.LogInformation("已跳过数据库自动迁移（Database:ApplyMigrationsOnStartup=false）。");
        }

        // S9：迁移完成后把模拟统计与最近一次日志从数据库恢复到内存工作副本。
        // 用 GetService 而非构造函数注入，保证只构建 DbContext 的集成测试也能复用本类。
        EfSimulationStatisticsStore? statistics = scope.ServiceProvider.GetService<EfSimulationStatisticsStore>();
        if (statistics is not null) await statistics.LoadAsync(cancellationToken);

        await SeedBootstrapApiKeyAsync(db, cancellationToken);
    }

    /// <summary>
    /// 兼容旧配置的过渡：若 Server:ApiKey 非空，首次启动时把 SHA-256 摘要写入 api_keys。
    /// 认证只查库；写入后应从配置移除明文，后续密钥直接由数据库管理（可吊销/过期/分权）。
    /// </summary>
    private async Task SeedBootstrapApiKeyAsync(BotDbContext db, CancellationToken cancellationToken)
    {
        string plain = serverOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(plain)) return;

        string hash = ApiKeyDefaults.Hash(plain);
        if (await db.ApiKeys.AnyAsync(x => x.KeyHash == hash, cancellationToken)) return;

        db.ApiKeys.Add(new ApiKey
        {
            Name = "bootstrap",
            KeyHash = hash,
            ScopesJson = "[\"thirdparty\",\"bind\"]",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("已从 Server:ApiKey 引导写入 API Key 摘要；请从配置中移除明文，改用 api_keys 表管理密钥。");
    }
}
