using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
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
        await TrackContentCatalogAsync(db, cancellationToken);
    }

    /// <summary>
    /// 内容目录留痕（计划 §10 第 7 条）：把当前内容指纹与库里上一份比对。
    /// <para>换了内容包是正常发版，不该拦停；但**必须留一条能查到的记录**——
    /// 玩家背包里存的是 <c>CatalogId</c>，内容删改后这些引用会静默失效，出问题时总要能回答
    /// 「是不是换过内容」。所以这里差异只打日志（Warning），并从这一版起更新记录。</para>
    /// </summary>
    private async Task TrackContentCatalogAsync(BotDbContext db, CancellationToken cancellationToken)
    {
        const string key = "content.catalog";
        string version = GameplayRules.CatalogVersion;
        string fingerprint = ContentCatalog.Fingerprint;
        ContentCatalogSummary summary = ContentCatalog.Summary;

        AppConfig? row = await db.AppConfigs.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        ContentCatalogStamp current = new(version, fingerprint, summary.ToString(), DateTime.UtcNow);

        if (row is null)
        {
            db.AppConfigs.Add(new AppConfig
            {
                Key = key,
                ValueJson = JsonSerializer.Serialize(current),
                UpdatedAtUtc = DateTime.UtcNow
            });
            logger.LogInformation("内容目录首次记录：{Version} {Fingerprint}（{Summary}）",
                version, ContentCatalog.ShortFingerprint, summary);
            return;
        }

        ContentCatalogStamp? previous = null;
        try
        {
            previous = JsonSerializer.Deserialize<ContentCatalogStamp>(row.ValueJson);
        }
        catch (JsonException)
        {
            // 老格式或坏数据：按「没记录过」处理，不要因为一行配置起不来。
        }

        if (previous is not null && string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        string oldFingerprint = previous?.Fingerprint ?? "";
        string oldShort = oldFingerprint.Length > 12 ? oldFingerprint[..12] : oldFingerprint;
        logger.LogWarning(
            "内容目录已变更：{OldVersion}/{OldFingerprint}（{OldSummary}）→ {NewVersion}/{NewFingerprint}（{NewSummary}）。" +
            "若这次改过物品/技能/角色的 Id 或名字，请检查存量背包与角色快照的 CatalogId 是否还有效。",
            previous?.Version ?? "(未记录)", oldShort.Length == 0 ? "(无)" : oldShort,
            previous?.Summary ?? "(未知)",
            version, ContentCatalog.ShortFingerprint, summary);

        row.ValueJson = JsonSerializer.Serialize(current);
        row.UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>写在 <c>AppConfigs</c> 里的内容目录戳。</summary>
    private sealed record ContentCatalogStamp(string Version, string Fingerprint, string Summary, DateTime SeenAtUtc);

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
