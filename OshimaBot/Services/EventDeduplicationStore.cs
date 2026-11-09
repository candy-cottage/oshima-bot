using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Services;

/// <summary>
/// 事件幂等：以 processed_events 的唯一主键 (EventId, MsgSeq) 判定首次到达，
/// 替代 S1 之前 Webhook 里进程内的 IMemoryCache（重启即失效、多实例不共享）。
/// </summary>
public interface IEventDeduplicationStore
{
    Task<bool> TryMarkAsync(string eventId, int msgSeq = 0, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(string eventId, int msgSeq = 0, CancellationToken cancellationToken = default);
    Task ForgetAsync(string eventId, int msgSeq = 0, CancellationToken cancellationToken = default);
    Task<int> PurgeAsync(TimeSpan retention, CancellationToken cancellationToken = default);
}

public sealed class EventDeduplicationStore(
    IServiceScopeFactory scopeFactory,
    ILogger<EventDeduplicationStore> logger) : IEventDeduplicationStore
{
    public async Task<bool> TryMarkAsync(string eventId, int msgSeq = 0, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventId,
            MsgSeq = msgSeq,
            ReceivedAtUtc = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // SQLite 唯一键冲突：同一事件重复推送（平台重试 / webhook+WS 双通道）。
            logger.LogDebug("重复 QQ 事件，已跳过：EventId={EventId}", eventId);
            return false;
        }
    }

    public async Task MarkProcessedAsync(string eventId, int msgSeq = 0, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        DateTime now = DateTime.UtcNow;
        await db.ProcessedEvents
            .Where(x => x.EventId == eventId && x.MsgSeq == msgSeq)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ProcessedAtUtc, now), cancellationToken);
    }

    /// <summary>处理失败时撤销占位，允许平台重推后重新处理。</summary>
    public async Task ForgetAsync(string eventId, int msgSeq = 0, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        await db.ProcessedEvents
            .Where(x => x.EventId == eventId && x.MsgSeq == msgSeq)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> PurgeAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        DateTime cutoff = DateTime.UtcNow - retention;
        return await db.ProcessedEvents
            .Where(x => x.ReceivedAtUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>定期清理幂等表，避免 processed_events 无限增长。</summary>
public sealed class ProcessedEventCleanupService(
    IEventDeduplicationStore store,
    Microsoft.Extensions.Options.IOptions<Models.DatabaseOptions> options,
    ILogger<ProcessedEventCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan retention = TimeSpan.FromHours(Math.Max(1, options.Value.ProcessedEventRetentionHours));
        using PeriodicTimer timer = new(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                int removed = await store.PurgeAsync(retention, stoppingToken);
                if (removed > 0) logger.LogInformation("已清理过期事件幂等记录：{Count} 条", removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "清理过期事件幂等记录失败。");
            }
        }
    }
}
