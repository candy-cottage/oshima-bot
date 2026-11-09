using Microsoft.Extensions.Caching.Memory;
using OshimaBot.Models;
using OshimaBot.Services;

namespace OshimaBot.Messaging;

/// <summary>
/// 出站消息统一入口：修正旧版 msgSeq 恒为 1 的缺陷（同一 msg_id 第二次回复会触发 40054005），
/// 按 msg_id 在被动窗口内递增序号；第三方消息则直接写回结果对象。
/// </summary>
public sealed class MessageSender(IQQBotService qqBot, IMemoryCache cache, ILogger<MessageSender> logger)
{
    private static readonly TimeSpan PassiveWindow = TimeSpan.FromMinutes(5);
    private readonly object _seqLock = new();

    public async Task SendAsync(IBotMessage message, BotReply reply, CancellationToken cancellationToken = default)
    {
        if (message is ThirdPartyMessage thirdParty)
        {
            thirdParty.Result = reply.Markdown?.Content ?? reply.Text;
            thirdParty.IsCompleted = true;
            return;
        }

        string? msgId = message.SendProactive ? null : message.Id;
        int? msgSeq = string.IsNullOrWhiteSpace(msgId) ? null : NextSeq(msgId);

        if (reply.Markdown is not null && !string.IsNullOrWhiteSpace(reply.Markdown.Content))
        {
            if (message.IsGroup)
            {
                await qqBot.SendGroupMarkdownAsync(message.OpenId, reply.Markdown, reply.Keyboard, msgId, msgSeq, cancellationToken);
            }
            else
            {
                await qqBot.SendC2CMarkdownAsync(message.OpenId, reply.Markdown, reply.Keyboard, msgId, msgSeq, cancellationToken);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(reply.Text)) return;

        string content = message.IsGroup ? "\r\n" + reply.Text.Trim() : reply.Text.Trim();
        if (message.IsGroup)
        {
            await qqBot.SendGroupMessageAsync(message.OpenId, content, msgId: msgId, msgSeq: msgSeq, cancellationToken: cancellationToken);
        }
        else
        {
            await qqBot.SendC2CMessageAsync(message.OpenId, content, msgId: msgId, msgSeq: msgSeq, cancellationToken: cancellationToken);
        }
    }

    /// <summary>同一 msg_id 的回复序号 1,2,3…；5 分钟后随被动窗口一起失效。</summary>
    private int NextSeq(string msgId)
    {
        lock (_seqLock)
        {
            string key = $"OshimaBot:MsgSeq:{msgId}";
            int next = cache.TryGetValue(key, out int current) ? current + 1 : 1;
            cache.Set(key, next, PassiveWindow);
            logger.LogDebug("被动回复序号：MsgId={MsgId}, MsgSeq={MsgSeq}", msgId, next);
            return next;
        }
    }
}
