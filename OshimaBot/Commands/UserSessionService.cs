using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Commands;

public interface IUserSessionService
{
    Task<UserSessionState?> GetAsync(long userId, CancellationToken cancellationToken = default);
    Task SetPendingAsync(long userId, string action, string payloadJson, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task ClearPendingAsync(long userId, CancellationToken cancellationToken = default);
    Task SetPageCursorAsync(long userId, string cursor, CancellationToken cancellationToken = default);
}

public sealed record UserSessionState(
    string CurrentRegion, string? PendingAction, string PageCursor, DateTime? PendingExpiresAtUtc);

/// <summary>会话状态入库，替代旧版内存字典 + SemaphoreSlim；userId=0 的匿名请求直接跳过。</summary>
public sealed class UserSessionService(IServiceScopeFactory scopeFactory) : IUserSessionService
{
    public async Task<UserSessionState?> GetAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return null;
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        UserSession? row = await db.UserSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (row is null) return null;
        bool pendingValid = row.PendingExpiresAtUtc is null || row.PendingExpiresAtUtc > DateTime.UtcNow;
        return new UserSessionState(row.CurrentRegion,
            pendingValid ? row.PendingActionJson : null, row.PageCursor, row.PendingExpiresAtUtc);
    }

    public async Task SetPendingAsync(long userId, string action, string payloadJson, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return;
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        UserSession? row = await db.UserSessions.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (row is null)
        {
            row = new UserSession { UserId = userId };
            db.UserSessions.Add(row);
        }
        row.PendingActionJson = JsonSerializer.Serialize(new { action, payload = payloadJson });
        row.PendingExpiresAtUtc = DateTime.UtcNow + ttl;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearPendingAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return;
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        await db.UserSessions.Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.PendingActionJson, "")
                .SetProperty(x => x.PendingExpiresAtUtc, (DateTime?)null)
                .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
    }

    public async Task SetPageCursorAsync(long userId, string cursor, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return;
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        UserSession? row = await db.UserSessions.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (row is null)
        {
            db.UserSessions.Add(new UserSession
            {
                UserId = userId, PageCursor = cursor, UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            row.PageCursor = cursor;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
