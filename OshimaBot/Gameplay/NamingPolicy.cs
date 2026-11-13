using System.Text.RegularExpressions;

namespace OshimaBot.Gameplay;

/// <summary>
/// 用户可自定义文本（社团名、公告、私信、角色名）的命名与内容规范。
/// <para>这一层只做**机器能判的**部分：长度、控制字符、冒充官方、站外引流、刷屏重复。
/// 真正的内容审核（人工复核、举报处置）走 <c>RenameRequests</c> 审核队列，
/// 上线后还要接平台侧的内容安全接口——**自动通过不等于审核通过**。</para>
/// </summary>
public static class NamingPolicy
{
    /// <summary>不允许被普通玩家占用的名号（防冒充官方/客服）。</summary>
    private static readonly string[] ReservedWords =
    [
        "官方", "管理员", "客服", "系统", "运营", "GM", "admin", "system", "support"
    ];

    /// <summary>站外引流 / 联系方式：社团名与公告里一律拒绝。</summary>
    private static readonly Regex ContactPattern = new(
        @"(https?://|www\.|加群|进群|群号|扣群|微信|weixin|wechat|vx|qq群|扫码|私聊|代练|代充|出售|收购)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>连续 5 个以上相同字符（刷屏/占位）。</summary>
    private static readonly Regex RepeatPattern = new(@"(.)\1{4,}", RegexOptions.Compiled);

    private static readonly Regex QqNumberPattern = new(@"[qQ]{2}\s*\d{5,}", RegexOptions.Compiled);

    /// <param name="field">用于错误文案，例如「社团名」「公告」。</param>
    public static (bool Ok, string Reason) Check(string? text, int min, int max, string field)
    {
        string value = (text ?? "").Trim();
        if (value.Length < min) return (false, $"{field}至少 {min} 个字");
        if (value.Length > max) return (false, $"{field}最多 {max} 个字");

        foreach (char c in value)
        {
            // 控制字符、零宽字符常被用来伪造排版或绕过词表。
            if (char.IsControl(c) && c is not ('\n' or '\r' or '\t')) return (false, $"{field}含有不可见字符");
            if (c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF') return (false, $"{field}含有不可见字符");
        }
        if (value.Contains("  ", StringComparison.Ordinal)) return (false, $"{field}不要连续空格");

        foreach (string reserved in ReservedWords)
        {
            if (value.Contains(reserved, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"{field}不能包含「{reserved}」这类容易被误认为官方身份的词");
            }
        }

        if (ContactPattern.IsMatch(value) || QqNumberPattern.IsMatch(value))
        {
            return (false, $"{field}不能包含站外链接、联系方式或交易引流内容");
        }
        if (RepeatPattern.IsMatch(value)) return (false, $"{field}里有大段重复字符");

        return (true, "");
    }

    /// <summary>公告允许换行，其余规则同命名。</summary>
    public static (bool Ok, string Reason) CheckNotice(string? text)
        => Check(text, 0, 200, "公告");

    public static (bool Ok, string Reason) CheckMessage(string? text)
        => Check(text, 1, 200, "私信内容");
}

/// <summary>
/// <c>RenameRequests.TargetType</c> 的取值。名字类内容都要过审核，目前覆盖社团名与角色名。
/// </summary>
public static class RenameTargets
{
    public const string Club = "club";
    public const string Character = "character";

    public static string Display(string targetType) => targetType switch
    {
        Club => "社团名",
        Character => "角色名",
        _ => "名称"
    };
}
