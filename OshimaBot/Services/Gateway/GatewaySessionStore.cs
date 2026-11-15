using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Services.Gateway;

public interface IGatewaySessionStore
{
    Task<GatewaySessionState?> LoadAsync(string appId, CancellationToken cancellationToken = default);
    Task SaveAsync(string appId, string sessionId, long lastSeq, CancellationToken cancellationToken = default);
    Task ClearAsync(string appId, CancellationToken cancellationToken = default);
}

/// <summary>
/// session_id / last_seq 落库，进程重启后仍可尝试 Resume（超时则回落 Identify）。
/// <para>这里对数据库错误一律**降级**而不是抛出：会话续连只是优化，读不到最多多一次 Identify；
/// 但如果这里抛异常，它会顺着网关的重连循环冒到宿主，把整个进程停掉
/// （2026-11-16 首次启动闪退就是这么来的：迁移还没建表，读 gateway_sessions 直接 no such table）。</para>
/// </summary>
public sealed class GatewaySessionStore(
    IServiceScopeFactory scopeFactory,
    ILogger<GatewaySessionStore> logger) : IGatewaySessionStore
{
    public async Task<GatewaySessionState?> LoadAsync(string appId, CancellationToken cancellationToken = default)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            GatewaySession? row = await db.GatewaySessions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.BotAppId == appId, cancellationToken);
            return row is null ? null : new GatewaySessionState(row.SessionId, row.LastSeq);
        }
        catch (SqliteException exception)
        {
            logger.LogWarning("读取网关会话失败，按「没有会话」处理（将重新 Identify）：{Message}", exception.Message);
            return null;
        }
    }

    public async Task SaveAsync(string appId, string sessionId, long lastSeq, CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveCoreAsync(appId, sessionId, lastSeq, cancellationToken);
        }
        catch (SqliteException exception)
        {
            // 存不下只是下次重启少一次 Resume，不值得把连接搞挂。
            logger.LogWarning("保存网关会话失败，本次不落库：{Message}", exception.Message);
        }
    }

    private async Task SaveCoreAsync(string appId, string sessionId, long lastSeq, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        GatewaySession? row = await db.GatewaySessions.FirstOrDefaultAsync(x => x.BotAppId == appId, cancellationToken);
        if (row is null)
        {
            db.GatewaySessions.Add(new GatewaySession
            {
                BotAppId = appId,
                SessionId = sessionId,
                LastSeq = lastSeq,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            row.SessionId = sessionId;
            row.LastSeq = lastSeq;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(string appId, CancellationToken cancellationToken = default)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await db.GatewaySessions.Where(x => x.BotAppId == appId).ExecuteDeleteAsync(cancellationToken);
        }
        catch (SqliteException exception)
        {
            // 清不掉也不影响后面重新 Identify。
            logger.LogWarning("清理网关会话失败，忽略：{Message}", exception.Message);
        }
    }
}

public sealed record GatewayStatusSnapshot(
    bool Connected, string? SessionId, long LastSeq, DateTime? ConnectedAtUtc, string? LastError);

public interface IGatewayStatus
{
    GatewayStatusSnapshot Snapshot { get; }
    void MarkConnected(string sessionId);
    void MarkSeq(long seq);
    void MarkDisconnected(string? error = null);
}

/// <summary>供 /health 观测的连接状态（进程内快照）。</summary>
public sealed class GatewayStatus : IGatewayStatus
{
    private readonly object _lock = new();
    private GatewayStatusSnapshot _snapshot = new(false, null, 0, null, null);

    public GatewayStatusSnapshot Snapshot
    {
        get { lock (_lock) return _snapshot; }
    }

    public void MarkConnected(string sessionId)
    {
        lock (_lock)
        {
            _snapshot = _snapshot with
            {
                Connected = true,
                SessionId = sessionId,
                ConnectedAtUtc = DateTime.UtcNow,
                LastError = null
            };
        }
    }

    public void MarkSeq(long seq)
    {
        lock (_lock) _snapshot = _snapshot with { LastSeq = seq };
    }

    public void MarkDisconnected(string? error = null)
    {
        lock (_lock) _snapshot = _snapshot with { Connected = false, LastError = error };
    }
}
