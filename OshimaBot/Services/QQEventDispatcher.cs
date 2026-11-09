using System.Text.Json;
using OshimaBot.Messaging;
using OshimaBot.Models;

namespace OshimaBot.Services;

public enum QQEventSource
{
    Webhook,
    WebSocket
}

public enum EventDispatchStatus
{
    Accepted,
    Duplicate,
    Ignored,
    Invalid,
    Failed
}

public readonly record struct EventDispatchResult(EventDispatchStatus Status, string? Reason = null)
{
    public static EventDispatchResult Accepted() => new(EventDispatchStatus.Accepted);
    public static EventDispatchResult Duplicate() => new(EventDispatchStatus.Duplicate, "事件已处理过。");
    public static EventDispatchResult Ignored(string? reason = null) => new(EventDispatchStatus.Ignored, reason);
    public static EventDispatchResult Invalid(string reason) => new(EventDispatchStatus.Invalid, reason);
    public static EventDispatchResult Failed(string reason) => new(EventDispatchStatus.Failed, reason);
}

/// <summary>消息投递抽象：Webhook 与 WebSocket 共用同一个出口，便于单测替换。</summary>
public interface IBotMessageSink
{
    ValueTask QueueAsync(IBotMessage message, CancellationToken cancellationToken = default);
}

public interface IQQEventDispatcher
{
    Task<EventDispatchResult> DispatchAsync(Payload payload, QQEventSource source, CancellationToken cancellationToken = default);
}

/// <summary>
/// 统一事件管线（S3）：去重 → 互动回执 → 事件转 IBotMessage → 投递 BotMessageQueue。
/// Webhook 与 WebSocket 两条入口都走这里，指令层（RainBotService）零改动。
/// </summary>
public sealed class QQEventDispatcher(
    IEventDeduplicationStore deduplication,
    IInteractionAckService interactionAck,
    IBotMessageSink sink,
    ILogger<QQEventDispatcher> logger) : IQQEventDispatcher
{
    private const string InteractionPrefix = "INTERACTION_CREATE:";
    private static readonly HashSet<string> HandledEvents = new(StringComparer.Ordinal)
    {
        "C2C_MESSAGE_CREATE",
        "GROUP_AT_MESSAGE_CREATE",
        "INTERACTION_CREATE"
    };

    public async Task<EventDispatchResult> DispatchAsync(Payload payload, QQEventSource source, CancellationToken cancellationToken = default)
    {
        if (payload is null) return EventDispatchResult.Invalid("Payload 为空。");
        if (payload.Op != 0) return EventDispatchResult.Ignored("非 Dispatch(op=0) Payload。");

        string eventKey = ResolveEventKey(payload, source);
        bool firstSeen = await deduplication.TryMarkAsync(eventKey, 0, cancellationToken);
        if (!firstSeen)
        {
            logger.LogDebug("忽略重复 QQ 事件：Source={Source}, EventId={EventId}", source, eventKey);
            return EventDispatchResult.Duplicate();
        }

        try
        {
            if (!HandledEvents.Contains(payload.EventType))
            {
                await deduplication.MarkProcessedAsync(eventKey, 0, cancellationToken);
                logger.LogDebug("忽略未订阅的 QQ 事件：{EventType}", payload.EventType);
                return EventDispatchResult.Ignored($"未处理的事件类型 {payload.EventType}。");
            }

            if (string.Equals(payload.EventType, "INTERACTION_CREATE", StringComparison.Ordinal))
            {
                // 官方：interaction_id 取 d.id，不带 "INTERACTION_CREATE:" 前缀。
                await interactionAck.AckAsync(ExtractInteractionId(payload), 0, cancellationToken);
            }

            IBotMessage? message = ParseMessage(payload);
            if (message is null || string.IsNullOrWhiteSpace(message.OpenId) || string.IsNullOrWhiteSpace(message.Detail))
            {
                await deduplication.MarkProcessedAsync(eventKey, 0, cancellationToken);
                return EventDispatchResult.Invalid("事件消息数据无效。");
            }

            // 互动事件只能用 event_id 被动回复；消息事件用 d.id 作为 msg_id。
            message.EventId = string.Equals(payload.EventType, "INTERACTION_CREATE", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(payload.Id)
                ? payload.Id
                : null;

            await sink.QueueAsync(message, cancellationToken);
            await deduplication.MarkProcessedAsync(eventKey, 0, cancellationToken);
            logger.LogInformation("已接收 QQ {EventType} 事件：Source={Source}, EventId={EventId}, MessageId={MessageId}",
                payload.EventType, source, eventKey, message.Id);
            return EventDispatchResult.Accepted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 撤销占位，让平台重推时有机会重新处理。
            await deduplication.ForgetAsync(eventKey, 0, CancellationToken.None);
            logger.LogError(exception, "QQ 事件分发失败：Source={Source}, EventId={EventId}", source, eventKey);
            return EventDispatchResult.Failed("事件分发失败。");
        }
    }

    private static string ResolveEventKey(Payload payload, QQEventSource source)
    {
        if (!string.IsNullOrWhiteSpace(payload.Id)) return payload.Id;
        // 官方 Payload 顶层 id 为事件 ID；WS 下行必然带 s，作为兜底键。
        if (payload.SequenceNumber > 0) return $"{source}:seq:{payload.SequenceNumber}";
        return $"{source}:noid:{Guid.NewGuid():N}";
    }

    private static string ExtractInteractionId(Payload payload)
    {
        if (payload.Data.ValueKind == JsonValueKind.Object
            && payload.Data.TryGetProperty("id", out JsonElement id)
            && id.ValueKind == JsonValueKind.String)
        {
            string? value = id.GetString();
            if (!string.IsNullOrWhiteSpace(value)) return value!;
        }
        return payload.Id.StartsWith(InteractionPrefix, StringComparison.Ordinal)
            ? payload.Id[InteractionPrefix.Length..]
            : payload.Id;
    }

    private static IBotMessage? ParseMessage(Payload payload)
    {
        try
        {
            return payload.EventType switch
            {
                "C2C_MESSAGE_CREATE" => payload.Data.Deserialize<C2CMessage>(),
                "GROUP_AT_MESSAGE_CREATE" => payload.Data.Deserialize<GroupAtMessage>(),
                "INTERACTION_CREATE" => payload.Data.Deserialize<InteractionEvent>(),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
