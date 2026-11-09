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

/// <summary>session_id / last_seq 落库，进程重启后仍可尝试 Resume（超时则回落 Identify）。</summary>
public sealed class GatewaySessionStore(IServiceScopeFactory scopeFactory) : IGatewaySessionStore
{
    public async Task<GatewaySessionState?> LoadAsync(string appId, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        GatewaySession? row = await db.GatewaySessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.BotAppId == appId, cancellationToken);
        return row is null ? null : new GatewaySessionState(row.SessionId, row.LastSeq);
    }

    public async Task SaveAsync(string appId, string sessionId, long lastSeq, CancellationToken cancellationToken = default)
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
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        await db.GatewaySessions.Where(x => x.BotAppId == appId).ExecuteDeleteAsync(cancellationToken);
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
