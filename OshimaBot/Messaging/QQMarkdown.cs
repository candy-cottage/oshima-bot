using System.Text;

namespace OshimaBot.Messaging;

/// <summary>
/// QQ 自定义 Markdown 构造助手。官方 2026/04/23 已对单聊/群聊全量开放自定义 Markdown。
/// 支持：标题、粗/斜体/删除线、链接、图片、有序/无序列表、块引用、水平分割线、多行。
/// **不支持表格**（官方格式清单中没有），因此本类刻意不提供 Table 方法。
/// https://bot.q.qq.com/wiki/develop/api-v2/server-inter/message/type/markdown.html
/// </summary>
public static class QQMarkdown
{
    /// <summary>官方未明示 content 上限，按 3000 字符保守分段（错误码 40054007 消息长度超限）。</summary>
    public const int DefaultMaxLength = 3000;

    /// <summary>转义用户可控文本，避免破坏排版或注入 &lt;qqbot-*&gt; 标签。</summary>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        StringBuilder builder = new(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '`': builder.Append("\\`"); break;
                case '*': builder.Append("\\*"); break;
                case '_': builder.Append("\\_"); break;
                case '~': builder.Append("\\~"); break;
                case '>': builder.Append("\\>"); break;
                case '#': builder.Append("\\#"); break;
                case '[': builder.Append("\\["); break;
                case ']': builder.Append("\\]"); break;
                case '(': builder.Append("\\("); break;
                case ')': builder.Append("\\)"); break;
                case '!': builder.Append("\\!"); break;
                case '<': builder.Append("&lt;"); break;
                default: builder.Append(c); break;
            }
        }
        return builder.ToString();
    }

    public static string Heading(string text, int level = 2)
        => $"{new string('#', Math.Clamp(level, 1, 6))} {Escape(text)}";

    public static string Bold(string text) => $"**{Escape(text)}**";
    public static string Italic(string text) => $"*{Escape(text)}*";
    public static string Strike(string text) => $"~~{Escape(text)}~~";
    public static string Quote(string text) => $"> {Escape(text)}";
    public static string Divider() => "***";
    public static string Link(string label, string url) => $"[{Escape(label)}]({url})";
    public static string Image(string url, string? alt = null, string? size = null)
        => $"![{Escape(alt) ?? ""}{size ?? ""}]({url})";

    public static string OrderedList(IEnumerable<string> items)
        => string.Join(Environment.NewLine, items.Select((item, index) => $"{index + 1}. {Escape(item)}"));

    public static string UnorderedList(IEnumerable<string> items)
        => string.Join(Environment.NewLine, items.Select(item => $"- {Escape(item)}"));

    /// <summary>按字符上限切分长回复，优先在换行处断开。</summary>
    public static IReadOnlyList<string> Split(string content, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength) return [content];

        List<string> parts = [];
        int start = 0;
        while (start < content.Length)
        {
            int length = Math.Min(maxLength, content.Length - start);
            int breakAt = content.LastIndexOf('\n', start + length - 1, length);
            if (breakAt > start) length = breakAt - start + 1;
            parts.Add(content.Substring(start, length));
            start += length;
        }
        return parts;
    }
}
