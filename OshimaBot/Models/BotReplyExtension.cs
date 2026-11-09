using OshimaBot.Messaging;

namespace OshimaBot.Models;

/// <summary>
/// 照搬 v2 `OshimaServers/Model/QQBot.cs:106-232` 的 BotReplyExtension 全部扩展，
/// 并按 2026/10/26 官方规范做合规修正：CreateCmdInput 走 QQCmdTag（urlencode + 100 字符截断），
/// 分页/new-row 逻辑保持原样。v2 的 params IEnumerable&lt;Button&gt;[] 收敛为 params KeyboardButton[]。
/// </summary>
public static class BotReplyExtension
{
    /// <summary>按每行上限自动换行追加按钮。</summary>
    public static KeyboardMessage AppendButtons(this KeyboardMessage kb, int btnCountPerRow, params KeyboardButton[] buttons)
    {
        KeyboardContent content = kb.Content ??= new KeyboardContent();
        btnCountPerRow = Math.Clamp(btnCountPerRow, 1, KeyboardBuilder.MaxButtonsPerRow);

        List<KeyboardRow> rows = content.Rows;
        KeyboardRow? currentRow = rows.Count > 0 ? rows[^1] : null;
        foreach (KeyboardButton button in buttons)
        {
            if (currentRow is null || currentRow.Buttons.Count >= btnCountPerRow)
            {
                currentRow = new KeyboardRow();
                rows.Add(currentRow);
            }
            currentRow.Buttons.Add(button);
        }
        return kb;
    }

    /// <summary>强制另起一行后追加按钮。</summary>
    public static KeyboardMessage AppendButtonsWithNewRow(this KeyboardMessage kb, int btnCountPerRow, params KeyboardButton[] buttons)
    {
        KeyboardContent content = kb.Content ??= new KeyboardContent();
        btnCountPerRow = Math.Clamp(btnCountPerRow, 1, KeyboardBuilder.MaxButtonsPerRow);

        List<KeyboardRow> rows = content.Rows;
        KeyboardRow? currentRow = null;
        foreach (KeyboardButton button in buttons)
        {
            if (currentRow is null || currentRow.Buttons.Count >= btnCountPerRow)
            {
                currentRow = new KeyboardRow();
                rows.Add(currentRow);
            }
            currentRow.Buttons.Add(button);
        }
        return kb;
    }

    /// <summary>
    /// 通用分页行。页码命令按新版 MUD 语法生成 "{command} p{n}"（v2 直接拼 "{command}{n}"）。
    /// </summary>
    public static KeyboardMessage AddPaginationRow(this KeyboardMessage kb, string command, int currentPage, int totalPages)
    {
        string prefix = command.TrimEnd();
        List<KeyboardButton> buttons = [];

        if (currentPage > 1)
        {
            buttons.Add(KeyboardBuilder.CmdButton("<<", $"{prefix} p1"));
            buttons.Add(KeyboardBuilder.CmdButton("<", $"{prefix} p{currentPage - 1}"));
        }

        buttons.Add(KeyboardBuilder.CmdButton($"{currentPage} / {totalPages}", $"{prefix} ", enter: false));

        if (currentPage < totalPages)
        {
            buttons.Add(KeyboardBuilder.CmdButton(">", $"{prefix} p{currentPage + 1}"));
            buttons.Add(KeyboardBuilder.CmdButton(">>", $"{prefix} p{totalPages}"));
        }

        return kb.AppendButtonsWithNewRow(KeyboardBuilder.MaxButtonsPerRow, [.. buttons]);
    }

    /// <summary>创建 qqbot-cmd-input 标签；thisIsText=true 时交换 this 与 text 的角色（与 v2 一致）。</summary>
    public static string CreateCmdInput(this string show, string text, bool thisIsText = false)
    {
        string inputText = thisIsText ? show : text;
        string inputShow = thisIsText ? text : show;
        return QQCmdTag.CmdInput(inputText, inputShow);
    }

    /// <summary>展示文本与填充内容相同的 cmd-input 标签。</summary>
    public static string CreateCmdInput(this string text) => QQCmdTag.CmdInput(text);
}
