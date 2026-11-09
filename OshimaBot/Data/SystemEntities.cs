namespace OshimaBot.Data.Entities;

/// <summary>事件幂等：替代 IMemoryCache，重启/多实例后仍可去重。清理任务按 ReceivedAtUtc 删除过期行。</summary>
public sealed class ProcessedEvent
{
    public string EventId { get; set; } = "";
    public int MsgSeq { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
}

/// <summary>WebSocket Resume 所需会话信息。</summary>
public sealed class GatewaySession
{
    public string BotAppId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public long LastSeq { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>替代 rounds_archive.zip 与内存中的 LastRecord。</summary>
public sealed class SimulationRun
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string Seed { get; set; } = "";
    public string OptionsJson { get; set; } = "{}";
    public string ResultJson { get; set; } = "{}";
    public string? RoundsJson { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
}

/// <summary>替代 CharacterStatistics.json / TeamCharacterStatistics.json 与静态字典。</summary>
public sealed class SimulationStatistic
{
    public string Scope { get; set; } = "character";
    public string CatalogId { get; set; } = "";
    public string StatsJson { get; set; } = "{}";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>少量动态 KV，替代 rainbot/config.json、daily.json、giftbox.json 等配置文件。</summary>
public sealed class AppConfig
{
    public string Key { get; set; } = "";
    public string ValueJson { get; set; } = "{}";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
