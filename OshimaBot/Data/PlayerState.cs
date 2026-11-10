namespace OshimaBot.Data.Entities;

/// <summary>
/// 玩家进度存档（替代旧版 saved/{uid}.json）。
/// <para>货币已迁到 <c>CorePlayers</c>（按 Core 语义用 <c>double</c> 存 Credits/Materials），
/// 本表只保留 MUD 侧进度字段。日期字段按 Asia/Shanghai 存 yyyy-MM-dd。</para>
/// </summary>
public sealed class PlayerState
{
    public long UserId { get; set; }
    public int SignInStreak { get; set; }
    public string SignedOn { get; set; } = "";
    public string LogonOn { get; set; } = "";
    public string LunchClaimedOn { get; set; } = "";
    public string DinnerClaimedOn { get; set; } = "";
    public int ExploreTimes { get; set; }
    public long HorseRacingPoints { get; set; }
    public long CooperativePoints { get; set; }
    public long ForgePoints { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int Version { get; set; }
}

public sealed class CharacterSkill
{
    public long CharacterId { get; set; }
    public string SkillId { get; set; } = "";
    public int Level { get; set; } = 1;
    public int Slot { get; set; }
}

public sealed class CharacterEquipment
{
    public long CharacterId { get; set; }
    public string Slot { get; set; } = "";
    public long ItemInstanceId { get; set; }
}

public sealed class ItemInstance
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string CatalogId { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public int Quality { get; set; }
    public bool Locked { get; set; }
    public bool Bound { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime AcquiredAtUtc { get; set; } = DateTime.UtcNow;
    public int Version { get; set; }
}

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
