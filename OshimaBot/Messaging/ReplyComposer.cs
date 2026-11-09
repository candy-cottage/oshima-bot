using System.Text;
using OshimaBot.Models;

namespace OshimaBot.Messaging;

/// <summary>
/// MUD 4 段式回复：场景/标题 → 状态条 → 列表 → 可点击行动。
/// 按钮客户端与不支持按钮的客户端各有一条通路（keyboard 由调用方另挂）。
/// </summary>
public static class ReplyComposer
{
    public static BotReply Compose(
        string title,
        string? status = null,
        IEnumerable<string>? lines = null,
        IEnumerable<string>? actions = null)
    {
        StringBuilder builder = new();
        builder.AppendLine(QQMarkdown.Heading(title, 2));

        if (!string.IsNullOrWhiteSpace(status))
        {
            builder.AppendLine(QQMarkdown.Bold(status));
            builder.AppendLine();
        }

        if (lines is not null)
        {
            foreach (string line in lines)
            {
                builder.AppendLine(QQMarkdown.Escape(line));
            }
        }

        if (actions is not null)
        {
            builder.AppendLine();
            builder.AppendLine(QQMarkdown.Strike("可执行"));
            foreach (string action in actions)
            {
                builder.AppendLine(QQCmdTag.CmdInput(action));
            }
        }

        return BotReply.FromMarkdown(builder.ToString().TrimEnd());
    }
}
