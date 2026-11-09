using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using System.Text.Encodings.Web;

namespace OshimaBot.Security;

/// <summary>数据库侧 API Key 校验：只认 api_keys 表中的 SHA-256 摘要，支持吊销/过期/按 key 分权。</summary>
public sealed class ApiKeyAuthenticator(
    IServiceScopeFactory scopeFactory,
    IUserDirectory directory,
    ILogger<ApiKeyAuthenticator> logger)
{
    public async Task<ApiKeyIdentity?> AuthenticateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        string hash = ApiKeyDefaults.Hash(key);

        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        ApiKey? row = await db.ApiKeys.AsNoTracking()
            .FirstOrDefaultAsync(x => x.KeyHash == hash, cancellationToken);
        if (row is null || row.RevokedAtUtc is not null) return null;
        if (row.ExpiresAtUtc is not null && row.ExpiresAtUtc <= DateTime.UtcNow) return null;

        // S6 遗留边界：引导密钥的 user_id 为 NULL，第三方通道因此拿不到存档身份。
        // 每个 ApiKey 首次认证时懒建一个独立内部用户（provider=apikey），之后走纯读路径。
        long? userId = row.UserId;
        if (userId is null)
        {
            try
            {
                ExternalIdentity identity = new("apikey", $"key-{row.Id}", "thirdparty");
                BotUserPrincipal principal = await directory.ResolveAsync(identity, cancellationToken);
                await db.ApiKeys.Where(x => x.Id == row.Id && x.UserId == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.UserId, (long?)principal.UserId), cancellationToken);
                userId = principal.UserId;
                logger.LogInformation("API Key 已绑定内部用户：KeyId={KeyId}, UserId={UserId}", row.Id, userId);
            }
            catch (Exception exception)
            {
                // 身份写入失败不阻断只读查询；需身份的 P1 指令会提示「身份尚未就绪」。
                logger.LogWarning(exception, "API Key 身份懒注册失败：KeyId={KeyId}", row.Id);
            }
        }

        List<int> roleValues = userId is null
            ? []
            : await db.UserRoles.AsNoTracking()
                .Where(x => x.UserId == userId.Value).Select(x => x.Role).ToListAsync(cancellationToken);
        return new ApiKeyIdentity(row.Id, userId, row.Name,
            ApiKeyDefaults.ParseScopes(row.ScopesJson), ApiKeyDefaults.ParseRoles(roleValues));
    }
}

/// <summary>把 X-API-Key / Bearer 换成标准 ClaimsPrincipal，供 Policy 与 S7 指令权限使用。</summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string key = Request.Headers[ApiKeyDefaults.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            string authorization = Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                key = authorization[7..].Trim();
            }
        }
        if (string.IsNullOrWhiteSpace(key)) return AuthenticateResult.NoResult();

        ApiKeyAuthenticator authenticator = Context.RequestServices.GetRequiredService<ApiKeyAuthenticator>();
        ApiKeyIdentity? identity = await authenticator.AuthenticateAsync(key, Context.RequestAborted);
        if (identity is null) return AuthenticateResult.Fail("API Key 无效、已吊销或已过期。");

        AuthenticationTicket ticket = new(identity.ToClaimsPrincipal(ApiKeyDefaults.Scheme), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }
}
