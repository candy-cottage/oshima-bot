namespace OshimaBot.Data.Entities;

public sealed class Squad
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class SquadMember
{
    public long SquadId { get; set; }
    public long CharacterId { get; set; }
    public int Slot { get; set; }
}

public sealed class TrainingSession
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long CharacterId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime? SettledAtUtc { get; set; }
}

public sealed class DailyQuest
{
    public long UserId { get; set; }
    public string QuestKey { get; set; } = "";
    public string QuestDate { get; set; } = "";
    public int Status { get; set; }
    public string ProgressJson { get; set; } = "{}";
    public DateTime? ClaimedAtUtc { get; set; }
}

public sealed class SignInRecord
{
    public long UserId { get; set; }
    public string SignInDate { get; set; } = "";
    public string RewardJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ActivityProgress
{
    public long UserId { get; set; }
    public string ActivityKey { get; set; } = "";
    public string ProgressJson { get; set; } = "{}";
    public string ClaimedJson { get; set; } = "[]";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ExplorationRun
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string RegionKey { get; set; } = "";
    public string StateJson { get; set; } = "{}";
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime? SettledAtUtc { get; set; }
}

public sealed class ForgingJob
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string RecipeKey { get; set; } = "";
    public string StateJson { get; set; } = "{}";
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime? SettledAtUtc { get; set; }
}

public sealed class MailMessage
{
    public long Id { get; set; }
    public long? SenderUserId { get; set; }
    public long ReceiverUserId { get; set; }
    public string Subject { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime? ReadAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RenameRequest
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string TargetType { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string RequestedName { get; set; } = "";
    public int Status { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
}

/// <summary>MUD 会话态：当前场景、待确认动作、分页游标，替代内存字典与 SemaphoreSlim。</summary>
public sealed class UserSession
{
    public long UserId { get; set; }
    public string CurrentRegion { get; set; } = "";
    public string PendingActionJson { get; set; } = "";
    public DateTime? PendingExpiresAtUtc { get; set; }
    public string PageCursor { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
