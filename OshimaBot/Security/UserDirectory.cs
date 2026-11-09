using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Models;

namespace OshimaBot.Security;

public enum IdentityLinkStatus
{
    Linked,
    AlreadyLinked,
    Conflict,
    UserNotFound
}

public interface IUserDirectory
{
    /// <summary>按 (provider, subject) 解析内部 users.id；首次出现懒注册建档。</summary>
    Task<BotUserPrincipal> ResolveAsync(ExternalIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>把外部身份绑定到指定内部用户（绑定码兑换时调用）。</summary>
    Task<IdentityLinkStatus> LinkAsync(long userId, ExternalIdentity identity, CancellationToken cancellationToken = default);
}

public sealed class UserDirectory(
    IServiceScopeFactory scopeFactory,
    IOptions<ServerOptions> options,
    ILogger<UserDirectory> logger) : IUserDirectory
{
    private readonly ServerOptions _options = options.Value;

    public async Task<BotUserPrincipal> ResolveAsync(ExternalIdentity identity, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ResolveCoreAsync(identity, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 并发懒注册撞上 UNIQUE(provider, subject)：回滚后按已有身份重查。
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            UserIdentity? existing = await db.UserIdentities.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Provider == identity.Provider && x.Subject == identity.Subject, cancellationToken);
            if (existing is null) throw;
            return await BuildPrincipalAsync(db, existing.UserId, false, cancellationToken);
        }
    }

    private async Task<BotUserPrincipal> ResolveCoreAsync(ExternalIdentity identity, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        UserIdentity? existing = await db.UserIdentities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Provider == identity.Provider && x.Subject == identity.Subject, cancellationToken);
        if (existing is not null)
        {
            DateTime now = DateTime.UtcNow;
            await db.Users.Where(x => x.Id == existing.UserId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastSeenAtUtc, now), cancellationToken);
            return await BuildPrincipalAsync(db, existing.UserId, false, cancellationToken);
        }

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        long userId = 0;
        bool created = false;
        if (!string.IsNullOrWhiteSpace(identity.UnionSubject))
        {
            UserIdentity? union = await db.UserIdentities.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UnionSubject == identity.UnionSubject, cancellationToken);
            if (union is not null) userId = union.UserId;
        }

        if (userId == 0)
        {
            User user = new()
            {
                Handle = "U" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                Nickname = "",
                Status = 0,
                CreatedAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            userId = user.Id;
            created = true;
        }

        db.UserIdentities.Add(new UserIdentity
        {
            UserId = userId,
            Provider = identity.Provider,
            Subject = identity.Subject,
            UnionSubject = identity.UnionSubject,
            Scene = identity.Scene,
            CreatedAtUtc = DateTime.UtcNow
        });
        await EnsureRolesAsync(db, userId, identity, cancellationToken);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = userId,
            Action = created ? "user.register" : "identity.union_link",
            Target = $"{identity.Provider}:{identity.Subject}",
            PayloadJson = $"{{\"scene\":\"{identity.Scene}\"}}",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        BotUserPrincipal principal = await BuildPrincipalAsync(db, userId, created, cancellationToken);
        logger.LogInformation("QQ 用户懒注册：UserId={UserId}, Handle={Handle}, Created={Created}",
            userId, principal.Handle, created);
        return principal;
    }

    public async Task<IdentityLinkStatus> LinkAsync(long userId, ExternalIdentity identity, CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        if (!await db.Users.AnyAsync(x => x.Id == userId, cancellationToken)) return IdentityLinkStatus.UserNotFound;

        UserIdentity? existing = await db.UserIdentities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Provider == identity.Provider && x.Subject == identity.Subject, cancellationToken);
        if (existing is not null)
        {
            return existing.UserId == userId ? IdentityLinkStatus.AlreadyLinked : IdentityLinkStatus.Conflict;
        }

        db.UserIdentities.Add(new UserIdentity
        {
            UserId = userId,
            Provider = identity.Provider,
            Subject = identity.Subject,
            UnionSubject = identity.UnionSubject,
            Scene = identity.Scene,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = userId,
            Action = "identity.bind_code_redeem",
            Target = $"{identity.Provider}:{identity.Subject}",
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return IdentityLinkStatus.Linked;
        }
        catch (DbUpdateException)
        {
            return IdentityLinkStatus.Conflict;
        }
    }

    private async Task EnsureRolesAsync(BotDbContext db, long userId, ExternalIdentity identity, CancellationToken cancellationToken)
    {
        HashSet<int> current = (await db.UserRoles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.Role).ToListAsync(cancellationToken)).ToHashSet();

        if (current.Add((int)BotRole.Player))
        {
            db.UserRoles.Add(new UserRole { UserId = userId, Role = (int)BotRole.Player });
        }
        // 超管双轨：配置白名单 OpenID + user_roles，避免数据库被清空后彻底失去管理能力。
        if (string.Equals(identity.Provider, "qq", StringComparison.Ordinal)
            && _options.SuperAdminOpenIds.Contains(identity.Subject, StringComparer.Ordinal)
            && current.Add((int)BotRole.Owner))
        {
            db.UserRoles.Add(new UserRole { UserId = userId, Role = (int)BotRole.Owner });
            logger.LogInformation("按白名单授予 Owner：UserId={UserId}", userId);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<BotUserPrincipal> BuildPrincipalAsync(
        BotDbContext db, long userId, bool isNew, CancellationToken cancellationToken)
    {
        User user = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, cancellationToken);
        List<int> roles = await db.UserRoles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.Role).ToListAsync(cancellationToken);
        return new BotUserPrincipal(user.Id, user.Handle, user.Nickname, ApiKeyDefaults.ParseRoles(roles), isNew);
    }
}
