using System.Text.RegularExpressions;

namespace OshimaBot.Tests;

/// <summary>
/// 断言用的 markdown 反解：把 <c>&lt;qqbot-cmd-input text="…" show="…"/&gt;</c> 还原成它**显示**的文字。
/// <para>回复正文里的指令现在都是可点标签，标签里的内容是 urlencode 过的，
/// 直接 <c>Contains("/状态")</c> 永远找不到 —— 先反解再断言，才是在看「玩家实际看到的字」。</para>
/// </summary>
internal static class MarkdownText
{
    private static readonly Regex CommandTag = new(
        "<qqbot-cmd-input text=\"([^\"]*)\" show=\"([^\"]*)\"[^>]*/>", RegexOptions.Compiled);

    /// <summary>标签 → 显示文本（show）。</summary>
    public static string Decode(string markdown)
        => CommandTag.Replace(markdown, match => Uri.UnescapeDataString(match.Groups[2].Value));

    /// <summary>标签 → 点击后填进输入框的文本（text）。</summary>
    public static List<string> Inserts(string markdown)
        => [.. CommandTag.Matches(markdown).Select(match => Uri.UnescapeDataString(match.Groups[1].Value))];

    /// <summary>键盘上所有按钮实际发送的指令。</summary>
    public static List<string> Buttons(Models.BotReply reply)
        => reply.Keyboard?.Content?.Rows
            .SelectMany(row => row.Buttons)
            .Select(button => button.Action?.Data ?? "")
            .ToList() ?? [];
}
