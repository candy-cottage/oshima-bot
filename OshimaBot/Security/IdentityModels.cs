using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OshimaBot.Security;

/// <summary>角色层级：数值越大权限越高，HasRole 按层级比较。</summary>
public enum BotRole
{
    Player = 0,
    Operator = 1,
    Admin = 2,
    Owner = 3
}

/// <summary>外部身份坐标：业务代码永远不直接拿 OpenID 当用户主键。</summary>
public sealed record ExternalIdentity(string Provider, string Subject, string Scene, string? UnionSubject = null)
{
    public static ExternalIdentity Qq(string openId, string scene = "c2c", string? unionOpenId = null)
        => new("qq", openId, scene, unionOpenId);
}

public sealed record BotUserPrincipal(
    long UserId, string Handle, string Nickname, IReadOnlyList<BotRole> Roles, bool IsNew)
{
    public const string Scheme = "QQBotEvent";

    public bool HasRole(BotRole required) => Roles.Any(role => role >= required);

    public ClaimsPrincipal ToClaimsPrincipal()
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(Nickname) ? Handle : Nickname),
            new("handle", Handle)
        ];
        claims.AddRange(Roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
    }
}

public sealed record ApiKeyIdentity(
    long KeyId, long? UserId, string Name, IReadOnlyList<string> Scopes, IReadOnlyList<BotRole> Roles)
{
    public ClaimsPrincipal ToClaimsPrincipal(string scheme)
    {
        List<Claim> claims =
        [
            new("api_key_id", KeyId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, Name)
        ];
        if (UserId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, UserId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        claims.AddRange(Scopes.Select(scope => new Claim("scope", scope)));
        claims.AddRange(Roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }
}

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string Policy = "ApiKey";
    public const string HeaderName = "X-API-Key";

    /// <summary>API Key 为高熵随机串，数据库只存 SHA-256 十六进制摘要，不存明文。</summary>
    public static string Hash(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    public static IReadOnlyList<string> ParseScopes(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static IReadOnlyList<BotRole> ParseRoles(IEnumerable<int> values)
        => values.Select(value => (BotRole)value)
            .Where(Enum.IsDefined)
            .Distinct()
            .ToArray();
}
