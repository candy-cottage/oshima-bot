using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OshimaBot.Models;

namespace OshimaBot.Services.Gateway;

/// <summary>
/// QQ 官方 WebSocket 单连接客户端（S4）：GET /gateway/bot → Connect → Hello(10) → Identify(2)/Resume(6)
/// → READY → 心跳(1/11) → Dispatch(0) 统一投递 QQEventDispatcher。
/// 出站消息仍走 IQQBotService 的 REST；这里只负责接事件。
/// </summary>
public sealed class QQBotGatewayClient(
    IOptions<QQBotOptions> options,
    IGatewayApiClient gatewayApi,
    IQQBotService qqBot,
    IQQEventDispatcher dispatcher,
    IGatewaySessionStore sessions,
    IGatewayStatus status,
    ILogger<QQBotGatewayClient> logger) : BackgroundService
{
    private readonly QQBotOptions _options = options.Value;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private long _lastSeq;
    private string? _sessionId;
    private bool _canResume;
    private int _heartbeatIntervalMs = GatewayProtocol.DefaultHeartbeatIntervalMs;
    private long _lastHeartbeatAckTicks = DateTime.UtcNow.Ticks;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || _options.Mode != QQBotMode.WebSocket)
        {
            logger.LogInformation("QQ WebSocket 未启用（Enabled={Enabled}, Mode={Mode}），网关客户端不启动。",
                _options.Enabled, _options.Mode);
            return;
        }

        int failures = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                GatewayConnectionOutcome outcome;
                try
                {
                    outcome = await RunConnectionAsync(stoppingToken);
                    failures = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    failures++;
                    status.MarkDisconnected(exception.Message);
                    logger.LogWarning(exception, "QQ WS 连接异常，第 {Failures} 次重连。", failures);
                    outcome = GatewayConnectionOutcome.Identify;
                }

                if (outcome == GatewayConnectionOutcome.Stop)
                {
                    logger.LogError("QQ WS 遇到不可重试的网关错误（intents/权限/封禁），停止重连；请检查 QQBot 配置。");
                    break;
                }

                if (outcome == GatewayConnectionOutcome.RefreshToken)
                {
                    await qqBot.RefreshTokenAsync(stoppingToken);
                }
                if (outcome is GatewayConnectionOutcome.Identify or GatewayConnectionOutcome.RefreshToken)
                {
                    await InvalidateSessionAsync(stoppingToken);
                }

                TimeSpan delay = Backoff(failures);
                logger.LogInformation("QQ WS 将在 {Delay} 后重连（{Outcome}）。", delay, outcome);
                await Task.Delay(delay, stoppingToken);
            }
        }
        finally
        {
            status.MarkDisconnected("网关客户端已停止");
        }
    }

    private async Task<GatewayConnectionOutcome> RunConnectionAsync(CancellationToken cancellationToken)
    {
        GatewayInfo info = await gatewayApi.GetGatewayAsync(cancellationToken);
        string token = await qqBot.GetAccessTokenAsync(cancellationToken);

        if (_sessionId is null)
        {
            GatewaySessionState? persisted = await sessions.LoadAsync(_options.AppId, cancellationToken);
            if (persisted is not null && !string.IsNullOrWhiteSpace(persisted.SessionId))
            {
                _sessionId = persisted.SessionId;
                _lastSeq = Math.Max(_lastSeq, persisted.LastSeq);
                _canResume = true;
                logger.LogInformation("已从 gateway_sessions 恢复会话：SessionId={SessionId}, Seq={Seq}", _sessionId, _lastSeq);
            }
        }

        using ClientWebSocket socket = new();
        using CancellationTokenSource heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? heartbeatTask = null;
        int? closeCode = null;
        string? closeDescription = null;
        GatewayConnectionOutcome? forcedOutcome = null;
        Interlocked.Exchange(ref _lastHeartbeatAckTicks, DateTime.UtcNow.Ticks);

        try
        {
            await socket.ConnectAsync(new Uri(info.Url), cancellationToken);
            logger.LogInformation("QQ WS 已连接：{Url}", info.Url);

            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                (string? raw, int? code, string? description) = await ReceiveAsync(socket, cancellationToken);
                if (raw is null)
                {
                    closeCode = code;
                    closeDescription = description;
                    break;
                }
                if (raw.Length == 0) continue;

                using JsonDocument document = JsonDocument.Parse(raw);
                JsonElement root = document.RootElement;
                int op = root.TryGetProperty("op", out JsonElement opElement) && opElement.ValueKind == JsonValueKind.Number
                    ? opElement.GetInt32()
                    : -1;
                JsonElement data = root.TryGetProperty("d", out JsonElement dataElement) ? dataElement : default;

                switch (op)
                {
                    case GatewayProtocol.OpHello:
                        _heartbeatIntervalMs = GatewayProtocol.ParseHeartbeatInterval(data);
                        heartbeatTask ??= HeartbeatLoopAsync(socket, _heartbeatIntervalMs, heartbeatCts.Token);
                        if (_canResume && !string.IsNullOrWhiteSpace(_sessionId))
                        {
                            await SendRawAsync(socket, GatewayProtocol.BuildResume(token, _sessionId!, _lastSeq), cancellationToken);
                            logger.LogInformation("已发送 Resume：SessionId={SessionId}, Seq={Seq}", _sessionId, _lastSeq);
                        }
                        else
                        {
                            await SendRawAsync(socket, GatewayProtocol.BuildIdentify(token, _options.Intents,
                                _options.ShardId, Math.Max(1, info.Shards)), cancellationToken);
                            logger.LogInformation("已发送 Identify：Intents={Intents}, Shard=[{ShardId},{ShardCount}]",
                                _options.Intents, _options.ShardId, Math.Max(1, info.Shards));
                        }
                        break;

                    case GatewayProtocol.OpDispatch:
                        if (root.TryGetProperty("s", out JsonElement seqElement)
                            && seqElement.ValueKind == JsonValueKind.Number
                            && seqElement.TryGetInt64(out long seq))
                        {
                            _lastSeq = seq;
                            status.MarkSeq(seq);
                            // 每 10 个 seq 落库一次：崩溃重启后 Resume 最多补发少量事件，
                            // 配合 processed_events 去重可避免重复处理。
                            if (seq % 10 == 0 && !string.IsNullOrWhiteSpace(_sessionId))
                            {
                                await sessions.SaveAsync(_options.AppId, _sessionId!, seq, cancellationToken);
                            }
                        }

                        string eventType = root.TryGetProperty("t", out JsonElement typeElement)
                            ? typeElement.GetString() ?? ""
                            : "";
                        if (eventType == "READY")
                        {
                            _sessionId = GatewayProtocol.ParseSessionId(data) ?? _sessionId;
                            _canResume = !string.IsNullOrWhiteSpace(_sessionId);
                            if (_sessionId is not null)
                            {
                                status.MarkConnected(_sessionId);
                                await sessions.SaveAsync(_options.AppId, _sessionId, _lastSeq, cancellationToken);
                            }
                            logger.LogInformation("QQ WS READY：SessionId={SessionId}", _sessionId);
                        }
                        else if (eventType == "RESUMED")
                        {
                            status.MarkConnected(_sessionId ?? "");
                            logger.LogInformation("QQ WS RESUMED：补发事件已完成。");
                        }
                        else if (!string.IsNullOrWhiteSpace(eventType))
                        {
                            Payload? payload = JsonSerializer.Deserialize<Payload>(raw);
                            if (payload is not null)
                            {
                                await dispatcher.DispatchAsync(payload, QQEventSource.WebSocket, cancellationToken);
                            }
                        }
                        break;

                    case GatewayProtocol.OpHeartbeat:
                        // 服务端要求立即回一次心跳。
                        await SendRawAsync(socket, GatewayProtocol.BuildHeartbeat(_lastSeq > 0 ? _lastSeq : null), cancellationToken);
                        break;

                    case GatewayProtocol.OpHeartbeatAck:
                        Interlocked.Exchange(ref _lastHeartbeatAckTicks, DateTime.UtcNow.Ticks);
                        break;

                    case GatewayProtocol.OpReconnect:
                        forcedOutcome = GatewayConnectionOutcome.Resume;
                        logger.LogInformation("收到 op=7 Reconnect，准备重连并尝试 Resume。");
                        await CloseSocketAsync(socket, cancellationToken);
                        break;

                    case GatewayProtocol.OpInvalidSession:
                        logger.LogWarning("收到 op=9 Invalid Session，清除会话并重新 Identify。");
                        await InvalidateSessionAsync(cancellationToken);
                        await SendRawAsync(socket, GatewayProtocol.BuildIdentify(token, _options.Intents,
                            _options.ShardId, Math.Max(1, info.Shards)), cancellationToken);
                        break;

                    default:
                        logger.LogDebug("忽略未处理的 QQ WS opcode：{Op}", op);
                        break;
                }
            }
        }
        finally
        {
            heartbeatCts.Cancel();
            if (heartbeatTask is not null)
            {
                try { await heartbeatTask; }
                catch (OperationCanceledException) { }
                catch (Exception exception) { logger.LogDebug(exception, "心跳任务结束。"); }
            }
            status.MarkDisconnected(closeDescription ?? $"连接关闭（code={closeCode?.ToString() ?? "n/a"}）");
            await CloseSocketAsync(socket, CancellationToken.None);
        }

        if (forcedOutcome is not null) return forcedOutcome.Value;
        return GatewayProtocol.DecideCloseAction(closeCode) switch
        {
            GatewayCloseAction.Resume => GatewayConnectionOutcome.Resume,
            GatewayCloseAction.RefreshToken => GatewayConnectionOutcome.RefreshToken,
            GatewayCloseAction.Stop => GatewayConnectionOutcome.Stop,
            _ => GatewayConnectionOutcome.Identify
        };
    }

    private async Task HeartbeatLoopAsync(ClientWebSocket socket, int intervalMs, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(intervalMs));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            long ackTicks = Interlocked.Read(ref _lastHeartbeatAckTicks);
            if (DateTime.UtcNow.Ticks - ackTicks > TimeSpan.FromMilliseconds(intervalMs * 2.5).Ticks)
            {
                logger.LogWarning("QQ WS 心跳 ACK 超时，判定连接僵死并主动重连。");
                socket.Abort();
                return;
            }
            await SendRawAsync(socket, GatewayProtocol.BuildHeartbeat(_lastSeq > 0 ? _lastSeq : null), cancellationToken);
        }
    }

    private async Task InvalidateSessionAsync(CancellationToken cancellationToken)
    {
        _canResume = false;
        _sessionId = null;
        await sessions.ClearAsync(_options.AppId, cancellationToken);
    }

    private async Task SendRawAsync(ClientWebSocket socket, string payload, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<(string? Raw, int? CloseCode, string? Description)> ReceiveAsync(
        ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream stream = new();
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (null, (int?)result.CloseStatus, result.CloseStatusDescription);
            }
            stream.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            return result.MessageType == WebSocketMessageType.Text
                ? (Encoding.UTF8.GetString(stream.ToArray()), null, null)
                : ("", null, null);
        }
    }

    private static async Task CloseSocketAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", cancellationToken);
        }
        catch (Exception)
        {
            // 断线或对端已关闭时无需再握手。
        }
    }

    private TimeSpan Backoff(int failures)
    {
        int maxSeconds = Math.Max(1, _options.GatewayMaxReconnectDelaySeconds);
        double seconds = Math.Min(maxSeconds, Math.Pow(2, Math.Min(failures, 10)));
        return TimeSpan.FromMilliseconds(seconds * 1000 + Random.Shared.Next(0, 750));
    }
}
