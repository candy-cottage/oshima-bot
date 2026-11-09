using System.Text;
using OshimaBot.Messaging;
using OshimaBot.Models;

namespace OshimaBot.Commands;

/// <summary>
/// MUD 回复构造：4 段式（场景 → 状态 → 列表 → 可点击行动）、错误三段式、长文本分页。
/// 官方不支持表格，因此列表 + @编号 + CmdInput / 按钮是排版与交互主力。
/// </summary>
public static class CommandReplies
{
    public const int HelpPageSize = 6;

    public static BotReply Compose(
        string title,
        string? status = null,
        IEnumerable<string>? lines = null,
        IEnumerable<string>? actions = null,
        KeyboardMessage? keyboard = null)
    {
        StringBuilder builder = new();
        builder.AppendLine(QQMarkdown.Heading(title, 2));
        if (!string.IsNullOrWhiteSpace(status))
        {
            builder.AppendLine(QQMarkdown.Bold(status)).AppendLine();
        }
        if (lines is not null)
        {
            foreach (string line in lines) builder.AppendLine(line);
        }
        if (actions is not null)
        {
            builder.AppendLine();
            foreach (string action in actions) builder.AppendLine(QQCmdTag.CmdInput(action));
        }
        return BotReply.FromMarkdown(builder.ToString().TrimEnd(), keyboard);
    }

    /// <summary>错误回复固定三段：发生了什么 / 没有发生什么 / 下一步怎么做。</summary>
    public static BotReply Error(string what, string notHappened, string next, string? action = null)
        => Compose("⚠️ 指令未能完成", null,
        [
            QQMarkdown.Escape(what),
            "",
            $"**没有发生**：{QQMarkdown.Escape(notHappened)}",
            $"**下一步**：{QQMarkdown.Escape(next)}"
        ],
        action is null ? null : [action]);

    /// <summary>查询类结果：标题 + 状态 + 代码块正文 + 行动指令。</summary>
    public static BotReply Detail(string title, string body, string? status = null, IEnumerable<string>? actions = null)
        => Compose(title, status, ["```", body.Replace("```", "``", StringComparison.Ordinal), "```"], actions);

    public static string Empty(string fallback) => fallback;

    /// <summary>纯文本或结果为空时的兜底（保持旧版返回语义）。</summary>
    public static BotReply TextOr(string result, string fallback)
        => string.IsNullOrWhiteSpace(result) ? BotReply.FromText(fallback) : BotReply.FromText(result);

    /// <summary>把模拟日志按旧规则裁剪后分页，页数与翻页按钮同时给出。</summary>
    public static BotReply CodePage(string title, IReadOnlyCollection<string> messages, int page, string command)
    {
        if (messages.Count == 0) return BotReply.FromText("暂无记录，请先运行一次模拟。");

        List<string> selected = [.. messages];
        if (selected.Count > 30)
        {
            selected = [.. selected[..15], $"……已省略 {messages.Count - 30} 段日志……", .. selected[^15..]];
        }

        StringBuilder body = new();
        foreach (string message in selected)
        {
            if (body.Length > 48_000)
            {
                body.AppendLine("……日志过长，后续内容已省略……");
                break;
            }
            body.AppendLine(message);
        }
        return Paginated(title, body.ToString(), page, command);
    }

    /// <summary>按 3000 字符切分，附 pN/M 与翻页指令/按钮。</summary>
    public static BotReply Paginated(string title, string body, int page, string command, string? status = null)
    {
        // 预留标题/状态/行动指令的字符，避免整条 content 超出保守上限。
        IReadOnlyList<string> parts = QQMarkdown.Split(body, QQMarkdown.DefaultMaxLength - 300);
        int total = Math.Max(1, parts.Count);
        int current = Math.Clamp(page, 1, total);
        string pageStatus = $"第 {current}/{total} 页{(string.IsNullOrWhiteSpace(status) ? "" : " · " + status)}";

        List<string> lines = ["```", parts[current - 1].Replace("```", "``", StringComparison.Ordinal), "```"];
        List<string> actions = [];
        if (current > 1) actions.Add($"{command} p{current - 1}");
        if (current < total) actions.Add($"{command} p{current + 1}");

        KeyboardMessage? keyboard = null;
        if (total > 1)
        {
            keyboard = new KeyboardBuilder().AddPagination(command, current, total).Build();
        }
        return Compose(title, pageStatus, lines, actions, keyboard);
    }

    /// <summary>帮助完全由注册表生成，保证「文档与实现同源」。</summary>
    public static BotReply Help(CommandRegistry registry, int page)
    {
        IReadOnlyList<ICommand> commands = registry.All;
        int total = Math.Max(1, (commands.Count + HelpPageSize - 1) / HelpPageSize);
        int current = Math.Clamp(page, 1, total);

        List<string> lines = [];
        string? lastGroup = null;
        foreach (ICommand command in commands.Skip((current - 1) * HelpPageSize).Take(HelpPageSize))
        {
            if (lastGroup != command.Group)
            {
                lines.Add(QQMarkdown.Bold($"【{command.Group}】"));
                lastGroup = command.Group;
            }
            lines.Add($"- {QQMarkdown.Bold(command.Usage)}　{command.Description}");
        }

        List<string> actions = ["/战斗帮助", "/取消"];
        if (total > 1) actions.Insert(0, $"/帮助 p{(current < total ? current + 1 : 1)}");

        KeyboardBuilder keyboardBuilder = new KeyboardBuilder()
            .AddButtons(5,
                KeyboardBuilder.CmdButton("帮助", "/帮助"),
                KeyboardBuilder.CmdButton("战斗帮助", "/战斗帮助"),
                KeyboardBuilder.CmdButton("开始时间", "/查询服务器启动时间"),
                KeyboardBuilder.CmdButton("查角色", "/查角色 1"),
                KeyboardBuilder.CmdButton("模拟", "/FunGame模拟"));
        if (total > 1) keyboardBuilder.AddPagination("/帮助", current, total);

        return Compose("📖 OshimaBot 指令", $"第 {current}/{total} 页 · 共 {commands.Count} 条指令 · / 可省略",
            lines,
            actions,
            keyboardBuilder.Build());
    }
}
