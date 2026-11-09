using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Models;

namespace OshimaBot.Security;

public enum BindRedeemStatus
{
    Success,
    NotFound,
    Expired,
    Used,
    Conflict,
    InvalidUser
}

public readonly record struct BindRedeemResult(BindRedeemStatus Status, long UserId = 0, string Reason = "")
{
    public static BindRedeemResult Ok(long userId) => new(BindRedeemStatus.Success, userId);
    public static BindRedeemResult Fail(BindRedeemStatus status, string reason) => new(status, 0, reason);
}

public interface IBindCodeService
{
    int TtlSeconds { get; }
    Task<string> IssueAsync(long userId, string purpose = "qq_bind", CancellationToken cancellationToken = default);
    Task<BindRedeemResult> RedeemAsync(string code, ExternalIdentity identity, CancellationToken cancellationToken = default);
}

/// <summary>6 位一次性绑定码，替代旧版「获取接入码」直接回显 OpenID。</summary>
public sealed class BindCodeService(
    IServiceScopeFactory scopeFactory,
    IUserDirectory userDirectory,
    IOptions<ServerOptions> options,
    ILogger<BindCodeService> logger) : IBindCodeService
{
    private readonly int _ttlSeconds = Math.Max(30, options.Value.BindCodeTtlSeconds);

    public int TtlSeconds => _ttlSeconds;

    public async Task<string> IssueAsync(long userId, string purpose = "qq_bind", CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        for (int attempt = 0; attempt < 12; attempt++)
        {
            string code = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (await db.BindCodes.AnyAsync(x => x.Code == code, cancellationToken)) continue;

            BindCode entity = new()
            {
                Code = code,
                UserId = userId,
                Purpose = purpose,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(_ttlSeconds),
                CreatedAtUtc = DateTime.UtcNow
            };
            db.BindCodes.Add(entity);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("已签发绑定码：UserId={UserId}, Purpose={Purpose}, TTL={Ttl}s", userId, purpose, _ttlSeconds);
                return code;
            }
            catch (DbUpdateException)
            {
                // 6 位码碰撞：把失败的实体从跟踪器里摘掉，再换一个重试。
                db.Entry(entity).State = EntityState.Detached;
            }
        }
        throw new InvalidOperationException("绑定码生成失败：连续碰撞，请稍后重试。");
    }

    public async Task<BindRedeemResult> RedeemAsync(string code, ExternalIdentity identity, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return BindRedeemResult.Fail(BindRedeemStatus.NotFound, "绑定码不能为空。");

        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        BindCode? bind = await db.BindCodes.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
        if (bind is null) return BindRedeemResult.Fail(BindRedeemStatus.NotFound, "绑定码不存在。");
        if (bind.UsedAtUtc is not null) return BindRedeemResult.Fail(BindRedeemStatus.Used, "绑定码已被使用。");
        if (bind.ExpiresAtUtc <= DateTime.UtcNow) return BindRedeemResult.Fail(BindRedeemStatus.Expired, "绑定码已过期。");

        IdentityLinkStatus link = await userDirectory.LinkAsync(bind.UserId, identity, cancellationToken);
        switch (link)
        {
            case IdentityLinkStatus.UserNotFound:
                return BindRedeemResult.Fail(BindRedeemStatus.InvalidUser, "绑定码关联的用户不存在。");
            case IdentityLinkStatus.Conflict:
                return BindRedeemResult.Fail(BindRedeemStatus.Conflict, "该 QQ 身份已绑定其它账号。");
        }

        bind.UsedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("绑定码已兑换：UserId={UserId}, Provider={Provider}, Subject={Subject}",
            bind.UserId, identity.Provider, identity.Subject);
        return BindRedeemResult.Ok(bind.UserId);
    }
}
