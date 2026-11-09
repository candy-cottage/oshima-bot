using System.Text.Json.Serialization;

namespace OshimaBot.Models;

/// <summary>第三方/管理端凭一次性绑定码把 QQ OpenID 绑到内部用户。</summary>
public sealed class BindRequest
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("openId")] public string OpenId { get; set; } = "";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "qq";
    [JsonPropertyName("scene")] public string Scene { get; set; } = "c2c";
}
