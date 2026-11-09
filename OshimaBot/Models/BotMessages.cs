using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using OshimaBot.Messaging;

namespace OshimaBot.Models
{
    public sealed class Payload
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("op")] public int Op { get; set; }
        [JsonPropertyName("d")] public JsonElement Data { get; set; }
        [JsonPropertyName("s")] public int SequenceNumber { get; set; }
        [JsonPropertyName("t")] public string EventType { get; set; } = "";
    }

    public sealed class ValidationRequest
    {
        [JsonPropertyName("plain_token")] public string PlainToken { get; set; } = "";
        [JsonPropertyName("event_ts")] public string EventTs { get; set; } = "";
    }

    public sealed class ValidationResponse
    {
        [JsonPropertyName("plain_token")] public string PlainToken { get; set; } = "";
        [JsonPropertyName("signature")] public string Signature { get; set; } = "";
    }

    public sealed class Author
    {
        [JsonPropertyName("user_openid")] public string UserOpenId { get; set; } = "";
        [JsonPropertyName("member_openid")] public string MemberOpenId { get; set; } = "";
    }

    public interface IBotMessage
    {
        string Id { get; }
        bool IsGroup { get; }
        string Detail { get; set; }
        string Timestamp { get; }
        string OpenId { get; }
        string AuthorOpenId { get; }
        long FunGameUID { get; set; }
        bool SendProactive { get; set; }

        /// <summary>互动事件的被动回复使用 event_id（与 msg_id 互斥）；消息事件为 null。</summary>
        string? EventId { get; set; }

        /// <summary>S6 起由事件管线注入；第三方接口则由 ApiKey ClaimsPrincipal 注入。</summary>
        ClaimsPrincipal? Principal { get; set; }
    }

    public sealed class ThirdPartyMessage : IBotMessage
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("isgroup")] public bool IsGroup { get; set; }
        [JsonPropertyName("group_openid")] public string GroupOpenId { get; set; } = "";
        [JsonPropertyName("detail")] public string Detail { get; set; } = "";
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
        [JsonPropertyName("openid")] public string OpenId { get; set; } = "";
        [JsonPropertyName("authoropenid")] public string AuthorOpenId { get; set; } = "";
        [JsonIgnore] public long FunGameUID { get; set; }
        [JsonIgnore] public bool SendProactive { get; set; }
        [JsonIgnore] public string? EventId { get; set; }
        [JsonIgnore] public ClaimsPrincipal? Principal { get; set; }
        [JsonIgnore] public bool IsCompleted { get; set; }
        [JsonIgnore] public string Result { get; set; } = "";
    }

    public sealed class C2CMessage : IBotMessage
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("author")] public Author Author { get; set; } = new();
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
        [JsonIgnore] public long FunGameUID { get; set; }
        [JsonIgnore] public bool SendProactive { get; set; }
        [JsonIgnore] public string? EventId { get; set; }
        [JsonIgnore] public ClaimsPrincipal? Principal { get; set; }
        public string Detail { get => Content; set => Content = value; }
        public string OpenId => Author.UserOpenId;
        public string AuthorOpenId => Author.UserOpenId;
        public bool IsGroup => false;
    }

    public sealed class GroupAtMessage : IBotMessage
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("author")] public Author Author { get; set; } = new();
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
        [JsonPropertyName("group_openid")] public string GroupOpenId { get; set; } = "";
        [JsonIgnore] public long FunGameUID { get; set; }
        [JsonIgnore] public bool SendProactive { get; set; }
        [JsonIgnore] public string? EventId { get; set; }
        [JsonIgnore] public ClaimsPrincipal? Principal { get; set; }
        public string Detail { get => Content; set => Content = value; }
        public string OpenId => GroupOpenId;
        public string AuthorOpenId => Author.MemberOpenId;
        public bool IsGroup => true;
    }

    public sealed class InteractionEvent : IBotMessage
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("chat_type")] public int ChatType { get; set; }
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
        [JsonPropertyName("group_openid")] public string GroupOpenId { get; set; } = "";
        [JsonPropertyName("user_openid")] public string UserOpenId { get; set; } = "";
        [JsonPropertyName("data")] public InteractionData Data { get; set; } = new();
        [JsonIgnore] public long FunGameUID { get; set; }
        [JsonIgnore] public bool SendProactive { get; set; }
        [JsonIgnore] public string? EventId { get; set; }
        [JsonIgnore] public ClaimsPrincipal? Principal { get; set; }
        public string Detail { get => Data.Resolved?.ButtonData ?? ""; set { } }
        public string OpenId => IsGroup ? GroupOpenId : UserOpenId;
        public string AuthorOpenId => UserOpenId;
        public bool IsGroup => ChatType == 1;
    }

    public sealed class InteractionData
    {
        [JsonPropertyName("resolved")] public InteractionResolved? Resolved { get; set; }
    }

    public sealed class InteractionResolved
    {
        [JsonPropertyName("button_id")] public string ButtonId { get; set; } = "";
        [JsonPropertyName("button_data")] public string ButtonData { get; set; } = "";
        [JsonPropertyName("message_id")] public string MessageId { get; set; } = "";
    }

    public sealed class OtherData
    {
        public string RequestUrl { get; set; } = "";
    }

    public sealed class BotReply
    {
        public string Text { get; set; } = "";
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MarkdownMessage? Markdown { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KeyboardMessage? Keyboard { get; set; }

        public static BotReply FromText(string text) => new() { Text = text };
        public static BotReply FromMarkdown(string markdown, KeyboardMessage? keyboard = null) => new()
        {
            Markdown = new MarkdownMessage { Content = markdown },
            Keyboard = keyboard
        };
        public static implicit operator BotReply(string text) => FromText(text);
    }

    public sealed class MarkdownMessage
    {
        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Content { get; set; }

        [JsonPropertyName("custom_template_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CustomTemplateId { get; set; }

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<MarkdownParam>? Params { get; set; }
    }

    public sealed class MarkdownParam
    {
        [JsonPropertyName("key")] public string Key { get; set; } = "";
        [JsonPropertyName("values")] public List<string> Values { get; set; } = [];
    }

    public sealed class KeyboardMessage
    {
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }

        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public KeyboardContent? Content { get; set; }
    }

    public sealed class KeyboardContent
    {
        [JsonPropertyName("rows")] public List<KeyboardRow> Rows { get; set; } = [];
    }

    public sealed class KeyboardRow
    {
        [JsonPropertyName("buttons")] public List<KeyboardButton> Buttons { get; set; } = [];
    }

    public sealed class KeyboardButton
    {
        [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
        [JsonPropertyName("render_data")] public KeyboardRenderData RenderData { get; set; } = new();
        [JsonPropertyName("action")] public KeyboardAction Action { get; set; } = new();

        [JsonPropertyName("group_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? GroupId { get; set; }
    }

    /// <summary>style：0 灰色线框 / 1 蓝色线框 / 3 白底红字 / 4 蓝底白字；label 最多 10 字符。</summary>
    public sealed class KeyboardRenderData
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("visited_label")] public string VisitedLabel { get; set; } = "";
        [JsonPropertyName("style")] public int Style { get; set; } = 1;
    }

    public sealed class KeyboardAction
    {
        /// <summary>0 跳转 / 1 回调（触发 INTERACTION_CREATE，必须回执） / 2 指令。</summary>
        [JsonPropertyName("type")] public int Type { get; set; } = 2;
        [JsonPropertyName("permission")] public KeyboardPermission Permission { get; set; } = new();
        [JsonPropertyName("data")] public string Data { get; set; } = "";

        [JsonPropertyName("enter")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Enter { get; set; } = true;

        [JsonPropertyName("reply")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Reply { get; set; }

        [JsonPropertyName("anchor")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Anchor { get; set; }

        [JsonPropertyName("modal")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public KeyboardModal? Modal { get; set; }

        /// <summary>官方 msg-btn 页列为必填：客户端不支持该 action 时的 toast 文案。</summary>
        [JsonPropertyName("unsupport_tips")]
        public string UnsupportTips { get; set; } = QQCmdTag.DefaultUnsupportedTips;
    }

    public sealed class KeyboardModal
    {
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        [JsonPropertyName("confirm_text")] public string ConfirmText { get; set; } = "确认";
        [JsonPropertyName("cancel_text")] public string CancelText { get; set; } = "取消";
    }

    public sealed class KeyboardPermission
    {
        /// <summary>0 指定用户 / 1 仅管理者 / 2 所有人 / 3 指定身份组（仅频道）。</summary>
        [JsonPropertyName("type")] public int Type { get; set; } = 2;
        [JsonPropertyName("specify_user_ids")] public List<string> SpecifyUserIds { get; set; } = [];
    }

    public sealed class MediaResponse
    {
        [JsonPropertyName("file_uuid")] public string FileUuid { get; set; } = "";
        [JsonPropertyName("file_info")] public string FileInfo { get; set; } = "";
        [JsonPropertyName("ttl")] public int Ttl { get; set; }
    }

    public sealed class UploadMediaResult
    {
        public string? FileUuid { get; set; }
        public string? FileInfo { get; set; }
        public int Ttl { get; set; }
        public string? Error { get; set; }
        [JsonIgnore] public bool IsSuccess => string.IsNullOrEmpty(Error);
    }

    public sealed class AccessTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public string ExpiresIn { get; set; } = "";
    }
}
