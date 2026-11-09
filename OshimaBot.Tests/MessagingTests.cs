using OshimaBot.Messaging;
using OshimaBot.Models;
using Xunit;

namespace OshimaBot.Tests;

public sealed class MessagingTests
{
    [Fact]
    public void CmdInput_UrlEncodesTextAndShow()
    {
        string tag = QQCmdTag.CmdInput("/背包 查看", "点这里");
        Assert.StartsWith("<qqbot-cmd-input text=\"", tag);
        Assert.DoesNotContain("背包", tag);
        Assert.DoesNotContain("点这里", tag);
        Assert.Contains("%", tag);
        Assert.Contains("reference=\"false\"", tag);
    }

    [Fact]
    public void CmdInput_TruncatesTo100Characters()
    {
        string encoded = QQCmdTag.Encode(new string('a', 150));
        Assert.Equal(100, encoded.Length);
    }

    [Fact]
    public void CmdEnter_UsesEnterTag()
    {
        string tag = QQCmdTag.CmdEnter("/签到");
        Assert.StartsWith("<qqbot-cmd-enter text=\"", tag);
        Assert.DoesNotContain("qqbot-cmd-input", tag);
    }

    [Fact]
    public void AtUser_DoesNotInjectTags()
        => Assert.Equal("<qqbot-at-user id=\"abc&quot;123\" />", QQCmdTag.AtUser("abc\"123"));

    [Fact]
    public void Escape_NeutralizesCmdTagInjection()
    {
        string escaped = QQMarkdown.Escape("<qqbot-cmd-input text=\"x\"/>");
        Assert.DoesNotContain("<qqbot", escaped);
        Assert.Contains("&lt;", escaped);
    }

    [Fact]
    public void Split_RespectsMaxLength()
    {
        IReadOnlyList<string> parts = QQMarkdown.Split(new string('a', 7000), 3000);
        Assert.Equal(3, parts.Count);
        Assert.All(parts, part => Assert.True(part.Length <= 3000));
    }

    [Fact]
    public void CmdButton_FillsRequiredUnsupportedTips()
    {
        KeyboardButton button = KeyboardBuilder.CmdButton("签到", "/签到");
        Assert.Equal(2, button.Action.Type);
        Assert.False(string.IsNullOrWhiteSpace(button.Action.UnsupportTips));
        Assert.Equal(2, button.Action.Permission.Type);
        Assert.Equal("签到", button.RenderData.Label);
    }

    [Fact]
    public void InteractionButton_UsesCallbackType()
    {
        KeyboardButton button = KeyboardBuilder.InteractionButton("确认", "confirm:1");
        Assert.Equal(1, button.Action.Type);
        Assert.Null(button.Action.Enter);
    }

    [Fact]
    public void KeyboardBuilder_EnforcesFiveRowLimit()
    {
        KeyboardBuilder builder = new();
        for (int row = 0; row < 5; row++)
        {
            builder.AddNewRow(1, KeyboardBuilder.CmdButton($"B{row}", $"/b{row}"));
        }
        Assert.Throws<InvalidOperationException>(() => builder.AddNewRow(1, KeyboardBuilder.CmdButton("X", "/x")));
    }

    [Fact]
    public void AppendButtons_WrapsAutomatically()
    {
        KeyboardMessage keyboard = new();
        KeyboardButton[] buttons = Enumerable.Range(0, 7)
            .Select(index => KeyboardBuilder.CmdButton($"B{index}", $"/b{index}"))
            .ToArray();
        keyboard.AppendButtons(3, buttons);
        Assert.Equal(3, keyboard.Content!.Rows.Count);
        Assert.Equal(3, keyboard.Content.Rows[0].Buttons.Count);
        Assert.Equal(1, keyboard.Content.Rows[2].Buttons.Count);
    }

    [Fact]
    public void AddPaginationRow_UsesMudPageSyntax()
    {
        KeyboardMessage keyboard = new();
        keyboard.AddPaginationRow("/bag", 1, 3);
        KeyboardRow row = keyboard.Content!.Rows[0];
        Assert.Equal(3, row.Buttons.Count);
        Assert.Equal("/bag p2", row.Buttons[1].Action.Data);
        Assert.Equal("/bag p3", row.Buttons[2].Action.Data);
    }

    [Fact]
    public void CreateCmdInput_Extension_RespectsThisIsText()
    {
        string normal = "显示".CreateCmdInput("/go 城门");
        Assert.Contains("text=\"%2Fgo", normal);
        Assert.Contains("show=\"%E6%98%BE", normal);

        string swapped = "显示".CreateCmdInput("/go 城门", thisIsText: true);
        Assert.Contains("text=\"%E6%98%BE", swapped);
    }

    [Fact]
    public void ReplyComposer_ProducesFourPartMarkdown()
    {
        BotReply reply = ReplyComposer.Compose(
            "铎京 · 中央广场",
            "HP 100/100 · 金币 1,250",
            ["铁匠铺 @1 可锻造", "城门 @3 可前往"],
            ["/go @3"]);
        Assert.NotNull(reply.Markdown);
        Assert.StartsWith("## ", reply.Markdown!.Content);
        Assert.Contains("**HP", reply.Markdown.Content);
        Assert.Contains("qqbot-cmd-input", reply.Markdown.Content);
    }
}
