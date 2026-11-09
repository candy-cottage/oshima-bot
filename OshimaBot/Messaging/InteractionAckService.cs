using OshimaBot.Services;

namespace OshimaBot.Messaging;

/// <summary>
/// INTERACTION_CREATE 必须回执 PUT /interactions/{id}，否则客户端会一直 loading 直到超时。
/// 仅 type=11（消息按钮）/type=12（快捷菜单）需要，其它类型调用也不报错。
/// </summary>
public interface IInteractionAckService
{
    Task<bool> AckAsync(string interactionId, int code = 0, CancellationToken cancellationToken = default);
}

public sealed class InteractionAckService(
    IQQBotService qqBot,
    ILogger<InteractionAckService> logger) : IInteractionAckService
{
    public async Task<bool> AckAsync(string interactionId, int code = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(interactionId))
        {
            logger.LogWarning("互动事件缺少 interaction_id，跳过回执。");
            return false;
        }

        try
        {
            bool ok = await qqBot.AckInteractionAsync(interactionId, code, cancellationToken);
            if (!ok) logger.LogWarning("互动事件回执未成功：InteractionId={InteractionId}", interactionId);
            return ok;
        }
        catch (Exception exception)
        {
            // 回执失败不能阻断指令处理（例如 QQBot 尚未配置凭据的本地开发环境）。
            logger.LogWarning(exception, "互动事件回执异常：InteractionId={InteractionId}", interactionId);
            return false;
        }
    }
}
