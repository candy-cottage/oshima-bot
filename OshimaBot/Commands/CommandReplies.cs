using System.Text;
using OshimaBot.Gameplay;
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
            // 引用块状态条：QQ 渲染成左侧竖线的强调块，比裸加粗更有「卡片头」层次。
            builder.AppendLine(QQMarkdown.StatusBar(status)).AppendLine();
        }
        if (lines is not null)
        {
            foreach (string line in lines) builder.AppendLine(line);
        }

        // 结尾指令分两类：
        // - 没有参数的通用指令（/状态 /背包 /帮助）→ 按钮，点一下就发；
        // - 带参数的（/设主战 1、/撤销发放 3）→ 保留可点链接，它带着这一条的上下文。
        // 调用方自己排了键盘时（帮助、各类分页）不再追加按钮，避免超过 5 行上限。
        List<string> links = [];
        List<string> generic = [];
        if (actions is not null)
        {
            foreach (string action in actions)
            {
                if (keyboard is null && IsGenericCommand(action)) generic.Add(action);
                else links.Add(action);
            }
        }
        if (generic.Count > 0) keyboard = BuildButtonRow(generic);

        if (links.Count > 0)
        {
            // 正文与「可执行」之间加细分割线，四段式的最后一段不再糊在列表末尾。
            builder.AppendLine().AppendLine(QQMarkdown.Divider());
            foreach (string action in links) builder.AppendLine(QQCmdTag.CmdInput(action));
        }
        return BotReply.FromMarkdown(builder.ToString().TrimEnd(), keyboard);
    }

    /// <summary>「没有参数、点一下就发」的指令：/状态、/背包。带空格的视为带参数。</summary>
    internal static bool IsGenericCommand(string action)
        => action.Length > 1 && action[0] == '/' && !action.Contains(' ');

    /// <summary>把一串通用指令排成按钮（每行 5 个）。</summary>
    internal static KeyboardMessage BuildButtonRow(IReadOnlyList<string> commands)
    {
        KeyboardBuilder builder = new();
        for (int offset = 0; offset < commands.Count; offset += KeyboardBuilder.MaxButtonsPerRow)
        {
            builder.AddButtons(KeyboardBuilder.MaxButtonsPerRow,
                [.. commands.Skip(offset).Take(KeyboardBuilder.MaxButtonsPerRow)
                    .Select(item => KeyboardBuilder.CmdButton(item.TrimStart('/'), item))]);
        }
        return builder.Build();
    }

    /// <summary>错误回复固定三段：发生了什么 / 没有发生什么 / 下一步怎么做。</summary>
    public static BotReply Error(string what, string notHappened, string next, string? action = null)
        => Compose("⚠️ 指令未能完成", null,
        [
            QQCmdTag.LinkCommands(QQMarkdown.Escape(what)),
            "",
            $"**没有发生**：{QQMarkdown.Escape(notHappened)}",
            $"**下一步**：{QQCmdTag.LinkCommands(QQMarkdown.Escape(next))}"
        ],
        action is null ? null : [action]);

    /// <summary>查询类结果：标题 + 状态 + 代码块正文 + 行动指令（可选键盘）。</summary>
    public static BotReply Detail(string title, string body, string? status = null,
        IEnumerable<string>? actions = null, KeyboardMessage? keyboard = null)
        => Compose(title, status, ["```", body.Replace("```", "``", StringComparison.Ordinal), "```"], actions, keyboard);

    public static string Empty(string fallback) => fallback;

    /// <summary>纯文本或结果为空时的兜底（保持旧版返回语义）。</summary>
    public static BotReply TextOr(string result, string fallback)
        => string.IsNullOrWhiteSpace(result) ? BotReply.FromText(fallback) : BotReply.FromText(result);

    /// <summary>
    /// 日志分页：按**块**切页（一块 = 一个回合 / 一次空投 / 一段角色数据），页边界永远落在块之间。
    /// <para>按字符硬切会把一行战报劈成两页，翻页还要人脑拼接；按块切则每页都是完整事件。</para>
    /// <para>只有单块本身就超过一页（例如角色数据段）时才在块内按行再切。</para>
    /// </summary>
    public static BotReply LogPages(
        string title, IReadOnlyList<string> blocks, int page, string command,
        string? status = null, IEnumerable<string>? extraActions = null)
    {
        if (blocks.Count == 0) return BotReply.FromText("暂无记录，请先运行一次模拟。");

        const int budget = QQMarkdown.DefaultMaxLength - 600;   // 留标题/状态/行动指令/代码围栏
        List<List<string>> pages = [];
        List<string> current = [];
        int length = 0;

        foreach (string block in blocks)
        {
            if (block.Length > budget)
            {
                if (current.Count > 0) { pages.Add(current); current = []; length = 0; }
                foreach (string piece in SplitBlock(block, budget)) pages.Add([piece]);
                continue;
            }
            if (current.Count > 0 && length + block.Length + 2 > budget)
            {
                pages.Add(current);
                current = [];
                length = 0;
            }
            current.Add(block);
            length += block.Length + 2;
        }
        if (current.Count > 0) pages.Add(current);

        int total = pages.Count;
        int shown = Math.Clamp(page, 1, total);
        string pageStatus = $"第 {shown}/{total} 页{(string.IsNullOrWhiteSpace(status) ? "" : " · " + status)}";

        List<string> body = ["```"];
        foreach (string block in pages[shown - 1])
        {
            body.Add(block.Replace("```", "``", StringComparison.Ordinal));
        }
        body.Add("```");

        List<string> actions = [];
        if (shown > 1) actions.Add($"{command} p{shown - 1}");
        if (shown < total) actions.Add($"{command} p{shown + 1}");

        // 「/上次的日志」这类通用跳转同样做成按钮：它们没有参数，点一下就发。
        KeyboardBuilder builder = new();
        if (extraActions is not null)
        {
            List<string> commands = [.. extraActions];
            for (int offset = 0; offset < commands.Count; offset += KeyboardBuilder.MaxButtonsPerRow)
            {
                builder.AddButtons(KeyboardBuilder.MaxButtonsPerRow,
                    [.. commands.Skip(offset).Take(KeyboardBuilder.MaxButtonsPerRow)
                        .Select(item => KeyboardBuilder.CmdButton(item.TrimStart('/'), item))]);
            }
        }
        if (total > 1) builder.AddPagination(command, shown, total);

        return Compose(title, pageStatus, body, actions, builder.IsEmpty ? null : builder.Build());
    }

    /// <summary>把超长块按行切到 maxLength 以内；单行超长时硬切，保证一定有进展。</summary>
    private static IEnumerable<string> SplitBlock(string block, int maxLength)
    {
        StringBuilder piece = new();
        foreach (string line in block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (piece.Length > 0 && piece.Length + line.Length + 1 > maxLength)
            {
                yield return piece.ToString().TrimEnd();
                piece.Clear();
            }
            if (line.Length > maxLength)
            {
                if (piece.Length > 0) { yield return piece.ToString().TrimEnd(); piece.Clear(); }
                for (int offset = 0; offset < line.Length; offset += maxLength)
                {
                    yield return line.Substring(offset, Math.Min(maxLength, line.Length - offset));
                }
                continue;
            }
            if (piece.Length > 0) piece.Append('\n');
            piece.Append(line);
        }
        if (piece.Length > 0) yield return piece.ToString().TrimEnd();
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

    /// <summary>玩法仓储结果统一渲染：成功走 4 段式，失败走错误三段式。</summary>
    public static BotReply FromResult(ActionResult result)
        => result.Ok
            ? Compose(result.Title, result.Status, result.Lines, result.Actions)
            : Error(result.Title, result.Status,
                result.Lines.FirstOrDefault() ?? "稍后重试，或输入 /帮助 查看用法",
                result.Actions.FirstOrDefault());

    /// <summary>Markdown 列表分页（不包代码块，保留 - / @编号 的排版），附翻页指令与按钮。</summary>
    public static BotReply ListPage(
        string title, string status, IReadOnlyList<string> lines, int page, string command,
        IEnumerable<string>? extraActions = null, KeyboardBuilder? keyboard = null)
    {
        List<List<string>> chunks = [];
        List<string> current = [];
        int length = 0;
        foreach (string line in lines)
        {
            if (current.Count > 0 && length + line.Length + 1 > QQMarkdown.DefaultMaxLength - 400)
            {
                chunks.Add(current);
                current = [];
                length = 0;
            }
            current.Add(line);
            length += line.Length + 1;
        }
        if (current.Count > 0) chunks.Add(current);

        int total = Math.Max(1, chunks.Count);
        int currentPage = Math.Clamp(page, 1, total);
        List<string> actions = [];
        if (currentPage > 1) actions.Add($"{command} p{currentPage - 1}");
        if (currentPage < total) actions.Add($"{command} p{currentPage + 1}");

        KeyboardBuilder builder = keyboard ?? new KeyboardBuilder();
        if (extraActions is not null && keyboard is null)
        {
            // 底部这些「点一下就发」的通用入口（/状态 /背包 /签到 …）做成按钮，
            // 不再各占一行文字 —— 它们没有参数，做成按钮比一行行链接更省地方。
            List<string> commands = [.. extraActions];
            for (int offset = 0; offset < commands.Count; offset += KeyboardBuilder.MaxButtonsPerRow)
            {
                KeyboardBuilder row = builder;
                row.AddButtons(KeyboardBuilder.MaxButtonsPerRow,
                    [.. commands.Skip(offset).Take(KeyboardBuilder.MaxButtonsPerRow)
                        .Select(item => KeyboardBuilder.CmdButton(item.TrimStart('/'), item))]);
            }
        }
        else if (extraActions is not null)
        {
            // 调用方自己排了键盘（例如整块都是按钮），文字提示照旧保留。
            actions.AddRange(extraActions);
        }
        if (total > 1) builder.AddPagination(command, currentPage, total);
        return Compose(title, $"{status} · 第 {currentPage}/{total} 页",
            chunks.Count == 0 ? ["- （空）"] : chunks[currentPage - 1], actions,
            builder.IsEmpty ? null : builder.Build());
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
            // 用法整条做成链接：显示「/我的角色 [@编号|页码]」，点一下填入「/我的角色 」。
            lines.Add($"- {QQCmdTag.CmdInput("/" + command.Name + " ", command.Usage)}　{command.Description}");
        }

        List<string> actions = [];
        if (total > 1) actions.Add($"/帮助 p{(current < total ? current + 1 : 1)}");

        KeyboardBuilder keyboardBuilder = new KeyboardBuilder()
            .AddButtons(5,
                KeyboardBuilder.CmdButton("战斗帮助", "/战斗帮助"),
                KeyboardBuilder.CmdButton("状态", "/状态"),
                KeyboardBuilder.CmdButton("背包", "/背包"),
                KeyboardBuilder.CmdButton("抽卡", "/抽卡"),
                KeyboardBuilder.CmdButton("取消", "/取消"));
        if (total > 1) keyboardBuilder.AddPagination("/帮助", current, total);

        return Compose("📖 OshimaBot 指令", $"第 {current}/{total} 页 · 共 {commands.Count} 条指令 · / 可省略",
            lines,
            actions,
            keyboardBuilder.Build());
    }
}
