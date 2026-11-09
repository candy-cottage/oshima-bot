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

        /// <summary>留空时通过 GET /gateway/bot 自动获取；可用于沙箱代理或显式指定网关。</summary>
        public string GatewayUrl { get; set; } = "";

        /// <summary>单连接起步，分片编号固定 0；分片总数取自 /gateway/bot 的 shards。</summary>
        public int ShardId { get; set; }

        /// <summary>断线重连的指数退避上限（秒）。</summary>
        public int GatewayMaxReconnectDelaySeconds { get; set; } = 30;

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

        /// <summary>processed_events 幂等记录保留时长（小时），过期由后台任务每小时清理。</summary>
        public int ProcessedEventRetentionHours { get; set; } = 24;
    }

    public sealed class ServerOptions
    {
        public const string SectionName = "Server";

        /// <summary>仅作首次启动引导密钥：启动时把其 SHA-256 写入 api_keys，认证阶段不再读取明文。</summary>
        public string ApiKey { get; set; } = "";
        public int MaximumRespawnTimes { get; set; } = 10;

        /// <summary>超管白名单 QQ OpenID，与 user_roles 双轨；数据库清空后仍可恢复管理权限。</summary>
        public List<string> SuperAdminOpenIds { get; set; } = [];

        /// <summary>一次性绑定码有效期（秒）。</summary>
        public int BindCodeTtlSeconds { get; set; } = 300;

        /// <summary>S9：是否额外把回合记录写入 rounds_archive.zip。默认关闭，结果统一入 simulation_runs 表。</summary>
        public bool ArchiveSimulationRounds { get; set; }

        /// <summary>S9：是否把完整模拟日志写入 simulation_runs.rounds_json（供 /上次的完整日志 重启后恢复）。</summary>
        public bool StoreFullSimulationLog { get; set; } = true;
    }
}
