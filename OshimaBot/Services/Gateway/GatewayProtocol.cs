using System.Text.Json;

namespace OshimaBot.Services.Gateway;

/// <summary>GET /gateway/bot 返回值中本项目需要的字段。</summary>
public sealed record GatewayInfo(string Url, int Shards, int MaxConcurrency, int Remaining);

/// <summary>持久化在 gateway_sessions 表中，供断线与进程重启后 Resume。</summary>
public sealed record GatewaySessionState(string SessionId, long LastSeq);

public enum GatewayCloseAction
{
    /// <summary>用 session_id + seq 恢复。</summary>
    Resume,

    /// <summary>清 session，重新 Identify。</summary>
    Identify,

    /// <summary>凭据失效，刷新 AccessToken 后 Identify。</summary>
    RefreshToken,

    /// <summary>配置/权限错误，停止重连以免被官方限流。</summary>
    Stop
}

public enum GatewayConnectionOutcome
{
    Resume,
    Identify,
    RefreshToken,
    Stop
}

/// <summary>QQ 官方 WebSocket 协议编解码与错误码策略（纯函数，便于单测）。</summary>
public static class GatewayProtocol
{
    public const int OpDispatch = 0;
    public const int OpHeartbeat = 1;
    public const int OpIdentify = 2;
    public const int OpResume = 6;
    public const int OpReconnect = 7;
    public const int OpInvalidSession = 9;
    public const int OpHello = 10;
    public const int OpHeartbeatAck = 11;

    public const int DefaultHeartbeatIntervalMs = 45_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>token 格式官方要求 "QQBot {AccessToken}"，intents 用位掩码，单分片 [0,1]。</summary>
    public static string BuildIdentify(string accessToken, int intents, int shardId = 0, int shardCount = 1)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["op"] = OpIdentify,
            ["d"] = new Dictionary<string, object?>
            {
                ["token"] = $"QQBot {accessToken}",
                ["intents"] = intents,
                ["shard"] = new[] { shardId, Math.Max(1, shardCount) },
                ["properties"] = new Dictionary<string, string>
                {
                    ["$os"] = "windows",
                    ["$browser"] = "OshimaBot",
                    ["$device"] = "OshimaBot"
                }
            }
        }, JsonOptions);

    public static string BuildResume(string accessToken, string sessionId, long seq)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["op"] = OpResume,
            ["d"] = new Dictionary<string, object?>
            {
                ["token"] = $"QQBot {accessToken}",
                ["session_id"] = sessionId,
                ["seq"] = seq
            }
        }, JsonOptions);

    /// <summary>首次连接 d 必须为 null，之后携带最新收到的 s。</summary>
    public static string BuildHeartbeat(long? lastSeq)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["op"] = OpHeartbeat,
            ["d"] = lastSeq
        }, JsonOptions);

    public static int ParseHeartbeatInterval(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("heartbeat_interval", out JsonElement interval)
            && interval.ValueKind == JsonValueKind.Number
            && interval.TryGetInt32(out int ms)
            && ms > 0)
        {
            return Math.Max(1_000, ms);
        }
        return DefaultHeartbeatIntervalMs;
    }

    public static string? ParseSessionId(JsonElement data)
        => data.ValueKind == JsonValueKind.Object
           && data.TryGetProperty("session_id", out JsonElement id)
           && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;

    /// <summary>
    /// 官方 websocket.html 错误码表：
    /// 4009/4008 可 RESUME；4006/4007/4900~4913 需重新 IDENTIFY；
    /// 4001/4002/4010~4014/4914/4915 属于不可重试的配置或权限错误。
    /// </summary>
    public static GatewayCloseAction DecideCloseAction(int? closeCode) => closeCode switch
    {
        null or 1000 or 1001 => GatewayCloseAction.Resume,
        4009 or 4008 => GatewayCloseAction.Resume,
        4004 => GatewayCloseAction.RefreshToken,
        4001 or 4002 or 4010 or 4011 or 4012 or 4013 or 4014 or 4914 or 4915 => GatewayCloseAction.Stop,
        _ => GatewayCloseAction.Identify
    };
}
