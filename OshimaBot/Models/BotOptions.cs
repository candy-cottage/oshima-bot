namespace OshimaBot.Models
{
    /// <summary>QQ 官方事件接入方式。</summary>
    public enum QQBotMode
    {
        WebSocket,
        Webhook
    }

    /// <summary>QQ 开放平台运行环境（决定 API 域名）。</summary>
    public enum QQBotEnvironment
    {
        Sandbox,
        Production
    }

    public sealed class QQBotOptions
    {
        public const string SectionName = "QQBot";

        /// <summary>GROUP_AND_C2C_EVENT (1&lt;&lt;25) | INTERACTION (1&lt;&lt;26)。官方示例的 513 属于频道事件，不适用于本项目。</summary>
        public const int DefaultIntents = (1 << 25) | (1 << 26);

        public bool Enabled { get; set; }

        /// <summary>开发与部署均使用 WebSocket；Webhook 仅作可选兜底。</summary>
        public QQBotMode Mode { get; set; } = QQBotMode.WebSocket;

        public QQBotEnvironment Environment { get; set; } = QQBotEnvironment.Sandbox;

        /// <summary>默认关闭。开启后 POST /QQBot 才接受平台回调，并执行入站验签。</summary>
        public bool EnableWebhook { get; set; }

        public string AppId { get; set; } = "";
        public string Secret { get; set; } = "";

        /// <summary>留空时按 Environment 自动选择正式/沙箱域名。</summary>
        public string ApiBaseAddress { get; set; } = "";

        public string TokenAddress { get; set; } = "https://bots.qq.com/app/getAppAccessToken";
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int Intents { get; set; } = DefaultIntents;

        /// <summary>回调时间戳允许的最大偏移（秒），用于防重放。</summary>
        public int SignatureMaxAgeSeconds { get; set; } = 300;

        /// <summary>心跳周期由 op 10 Hello 下发，此处仅作为网关请求超时兜底。</summary>
        public int GatewayRequestTimeoutSeconds { get; set; } = 15;

        public string ResolvedApiBaseAddress => string.IsNullOrWhiteSpace(ApiBaseAddress)
            ? Environment == QQBotEnvironment.Sandbox
                ? "https://sandbox.api.sgroup.qq.com"
                : "https://api.sgroup.qq.com"
            : ApiBaseAddress.TrimEnd('/');

        public bool IsConfigured => Enabled
            && !string.IsNullOrWhiteSpace(AppId)
            && !string.IsNullOrWhiteSpace(Secret);
    }

    public sealed class DatabaseOptions
    {
        public const string SectionName = "Database";

        /// <summary>SQLite 数据库文件不纳入版本控制。</summary>
        public string ConnectionString { get; set; } = "Data Source=oshimobot.db";

        /// <summary>启动时自动应用 EF Core Migration。</summary>
        public bool ApplyMigrationsOnStartup { get; set; } = true;
    }

    public sealed class ServerOptions
    {
        public const string SectionName = "Server";

        public string ApiKey { get; set; } = "";
        public int MaximumRespawnTimes { get; set; } = 10;
    }
}
