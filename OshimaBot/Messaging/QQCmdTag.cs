namespace OshimaBot.Messaging;

/// <summary>
/// QQ 官方文本交互标签构造（文档更新 2026/10/26）：
/// https://bot.qq.com/wiki/develop/api-v2/server-inter/message/trans/text-chain.html
/// text / show 各 ≤100 字符且必须 urlencode —— 旧版 CreateCmdInput 只做了引号转义，这里统一补齐。
/// </summary>
public static class QQCmdTag
{
    public const int MaxTextLength = 100;
    public const string DefaultUnsupportedTips = "当前客户端不支持该操作，请手动输入指令。";

    /// <summary>截断到 100 字符后 urlencode，并转义属性值中的引号。</summary>
    public static string Encode(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        string truncated = value.Length > MaxTextLength ? value[..MaxTextLength] : value;
        return Uri.EscapeDataString(truncated).Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    /// <summary>点击后把 text 插入输入框，用户自行编辑发送。</summary>
    public static string CmdInput(string text, string? show = null, bool reference = false)
        => $"<qqbot-cmd-input text=\"{Encode(text)}\" show=\"{Encode(show ?? text)}\" reference=\"{(reference ? "true" : "false")}\"/>";

    /// <summary>点击后直接发送 text（仅单聊场景支持）。</summary>
    public static string CmdEnter(string text)
        => $"<qqbot-cmd-enter text=\"{Encode(text)}\"/>";

    public static string AtUser(string openId)
        => $"<qqbot-at-user id=\"{EscapeAttribute(openId)}\" />";

    public static string AtEveryone() => "<qqbot-at-everyone />";

    public static string AtChannel(string channelId)
        => $"<#{EscapeAttribute(channelId)}>";

    private static string EscapeAttribute(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
