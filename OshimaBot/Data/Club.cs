namespace OshimaBot.Data.Entities;

/// <summary>社团内的身份。数值可直接比较：社长 &gt; 管理 &gt; 普通成员。</summary>
public enum ClubRole
{
    Member = 0,
    Admin = 1,
    Master = 2
}

/// <summary>
/// 社团（旧版 <c>Club</c>，原本按社团编号落 JSON 文件，这里改数据库）。
/// <para><see cref="Funds"/> 对应旧版 <c>ClubPoins</c>，由成员捐献金币累加，后续社团商店/市场会用到。</para>
/// </summary>
public sealed class Club
{
    public long Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>3–4 位前缀，显示成「前缀·名称」，旧版建团时必填。</summary>
    public string Prefix { get; set; } = "";

    public string Notice { get; set; } = "";
    public long MasterUserId { get; set; }
    public bool IsPublic { get; set; } = true;

    /// <summary>加入是否需要社长/管理批准。</summary>
    public bool NeedApproval { get; set; }

    public double Funds { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>并发令牌：加入/退出/捐献同时发生时不至于写丢。</summary>
    public int Version { get; set; }
}

/// <summary>社团成员。一个用户只能在一个社团里（<see cref="UserId"/> 唯一索引兜底）。</summary>
public sealed class ClubMember
{
    public long Id { get; set; }
    public long ClubId { get; set; }
    public long UserId { get; set; }
    public ClubRole Role { get; set; } = ClubRole.Member;
    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>个人累计捐献（旧版只记社团总额，这里顺手记个人）。</summary>
    public double Contribution { get; set; }
}

/// <summary>入团申请（社团需要批准时产生）。</summary>
public sealed class ClubApplication
{
    public long Id { get; set; }
    public long ClubId { get; set; }
    public long UserId { get; set; }
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>邀请：被邀请的人可以绕过「未公开 / 需批准」的限制直接加入。</summary>
public sealed class ClubInvite
{
    public long Id { get; set; }
    public long ClubId { get; set; }
    public long UserId { get; set; }
    public DateTime InvitedAtUtc { get; set; } = DateTime.UtcNow;
}
