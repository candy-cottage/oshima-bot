namespace OshimaBot.Data.Entities;

/// <summary>内部用户主键。业务代码只使用 Id，外部 OpenID 单独存 UserIdentity。</summary>
public sealed class User
{
    public long Id { get; set; }
    public string Handle { get; set; } = "";
    public string Nickname { get; set; } = "";
    public int Status { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
    public int Version { get; set; }
}

/// <summary>外部身份（QQ user_openid / member_openid 等），支持多身份、换绑与 union_openid 合并。</summary>
public sealed class UserIdentity
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Provider { get; set; } = "qq";
    public string Subject { get; set; } = "";
    public string? UnionSubject { get; set; }
    public string Scene { get; set; } = "c2c";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class UserRole
{
    public long UserId { get; set; }
    public int Role { get; set; }
}

public sealed class ApiKey
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string Name { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public string ScopesJson { get; set; } = "[]";
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>一次性绑定码，用于第三方/管理端绑定 QQ OpenID，替代旧版直接回显接入码。</summary>
public sealed class BindCode
{
    public string Code { get; set; } = "";
    public long UserId { get; set; }
    public string Purpose { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public long? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
