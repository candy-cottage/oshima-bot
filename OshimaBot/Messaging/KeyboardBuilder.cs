using OshimaBot.Models;

namespace OshimaBot.Messaging;

/// <summary>
/// 自定义 keyboard 构造（用户已确认可用）。强制官方上限：5 行 × 每行 5 按钮、label ≤10 字符。
/// 照搬 v2 的 Button.CreateCmdButton / CreateInteractionButton 工厂语义。
/// </summary>
public sealed class KeyboardBuilder
{
    public const int MaxRows = 5;
    public const int MaxButtonsPerRow = 5;
    public const int MaxLabelLength = 10;

    private readonly KeyboardMessage _keyboard = new() { Content = new KeyboardContent() };

    /// <summary>action.type=2 指令按钮：点击填充输入框或直接发送 data。</summary>
    public static KeyboardButton CmdButton(
        string label,
        string data,
        bool enter = true,
        bool reply = false,
        string? visitedLabel = null,
        int style = 1,
        int permissionType = 2,
        params string[] specifyUserIds)
    {
        string display = Truncate(label);
        return new KeyboardButton
        {
            Id = Guid.NewGuid().ToString(),
            RenderData = new KeyboardRenderData
            {
                Label = display,
                VisitedLabel = Truncate(visitedLabel ?? label),
                Style = style
            },
            Action = new KeyboardAction
            {
                Type = 2,
                Data = data,
                Enter = enter,
                Reply = reply,
                Permission = new KeyboardPermission
                {
                    Type = permissionType,
                    SpecifyUserIds = [.. specifyUserIds]
                }
            }
        };
    }

    /// <summary>action.type=1 回调按钮：会触发 INTERACTION_CREATE，必须调用 PUT /interactions/{id} 回执。</summary>
    public static KeyboardButton InteractionButton(
        string label,
        string data,
        string? visitedLabel = null,
        int style = 1,
        int permissionType = 2,
        params string[] specifyUserIds)
    {
        string display = Truncate(label);
        return new KeyboardButton
        {
            Id = Guid.NewGuid().ToString(),
            RenderData = new KeyboardRenderData
            {
                Label = display,
                VisitedLabel = Truncate(visitedLabel ?? label),
                Style = style
            },
            Action = new KeyboardAction
            {
                Type = 1,
                Data = data,
                Enter = null,
                Permission = new KeyboardPermission
                {
                    Type = permissionType,
                    SpecifyUserIds = [.. specifyUserIds]
                }
            }
        };
    }

    public KeyboardBuilder AddButtons(int perRow, params KeyboardButton[] buttons)
    {
        _keyboard.AppendButtons(perRow, buttons);
        Validate();
        return this;
    }

    public KeyboardBuilder AddNewRow(int perRow, params KeyboardButton[] buttons)
    {
        _keyboard.AppendButtonsWithNewRow(perRow, buttons);
        Validate();
        return this;
    }

    public KeyboardBuilder AddPagination(string command, int currentPage, int totalPages)
    {
        _keyboard.AddPaginationRow(command, currentPage, totalPages);
        Validate();
        return this;
    }

    public KeyboardMessage Build()
    {
        Validate();
        return _keyboard;
    }

    private void Validate()
    {
        KeyboardContent content = _keyboard.Content!;
        if (content.Rows.Count > MaxRows)
        {
            throw new InvalidOperationException($"keyboard 最多 {MaxRows} 行，当前 {content.Rows.Count} 行。");
        }
        foreach (KeyboardRow row in content.Rows)
        {
            if (row.Buttons.Count > MaxButtonsPerRow)
            {
                throw new InvalidOperationException($"keyboard 每行最多 {MaxButtonsPerRow} 个按钮，当前 {row.Buttons.Count} 个。");
            }
        }
    }

    private static string Truncate(string value)
        => value.Length <= MaxLabelLength ? value : value[..MaxLabelLength];
}
