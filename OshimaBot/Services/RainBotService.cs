using OshimaBot.Commands;
using OshimaBot.Messaging;
using OshimaBot.Models;

namespace OshimaBot.Services;

/// <summary>
/// S7 起指令层只负责「路由结果 → 发送」：指令注册、解析、权限、审计都在 Commands/ 下。
/// 保留 Handler / HandlerByConsole 两个入口，Webhook、WS、第三方 HTTP 无需改动。
/// </summary>
public sealed class RainBotService(CommandRouter router, MessageSender messageSender)
{
    public async Task<bool> Handler(IBotMessage message, OtherData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.Detail = message.Detail?.Trim() ?? "";
        if (message.Detail.Length == 0) return false;

        BotReply? reply = await router.RouteAsync(message, data, cancellationToken);
        if (reply is null) return false;

        await messageSender.SendAsync(message, reply, cancellationToken);
        return true;
    }

    public Task<bool> HandlerByConsole(IBotMessage message, OtherData data, CancellationToken cancellationToken = default)
        => Handler(message, data, cancellationToken);
}
