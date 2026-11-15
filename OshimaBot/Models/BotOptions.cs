namespace OshimaBot.Models
{
    /// <summary>QQ 官方事件接入方式。</summary>
    public enum QQBotMode
    {
        WebSocket,
        Webhook
    }

    /// <summary>
    /// QQ 机器人接口配置。
    /// <para>2026-08-10 起官方把接口域名统一为 <see cref="DefaultApiBaseAddress"/>：
    /// 旧的 <c>api.sgroup.qq.com</c> 与沙箱域名 <c>sandbox.api.sgroup.qq.com</c> 均已下线，
    /// **沙箱应用也走同一个域名**（靠应用自身的沙箱身份区分，不再有独立入口）。
    /// 所以这里取消了 Environment 开关——留着它只会让人以为还有两套地址。</para>
    /// <para>旧配置文件里的 <c>QQBot:Environment</c> 会被配置绑定器忽略（未知键不报错），
    /// 不必先清理也能启动，但建议顺手删掉。</para>
    /// </summary>
    public sealed class QQBotOptions
    {
        public const string SectionName = "QQBot";

        /// <summary>统一接口域名（2026-08-10 起）。旧域名已下线，不要再用。</summary>
        public const string DefaultApiBaseAddress = "https://api.bot.qq.com";

        /// <summary>取 access_token 的地址；统一域名下路径与旧域名一致。</summary>
        public const string DefaultTokenAddress = DefaultApiBaseAddress + "/app/getAppAccessToken";

        /// <summary>GROUP_AND_C2C_EVENT (1&lt;&lt;25) | INTERACTION (1&lt;&lt;26)。官方示例的 513 属于频道事件，不适用于本项目。</summary>
        public const int DefaultIntents = (1 << 25) | (1 << 26);

        public bool Enabled { get; set; }

        /// <summary>开发与部署均使用 WebSocket；Webhook 仅作可选兜底。</summary>
        public QQBotMode Mode { get; set; } = QQBotMode.WebSocket;

        /// <summary>默认关闭。开启后 POST /QQBot 才接受平台回调，并执行入站验签。</summary>
        public bool EnableWebhook { get; set; }

        public string AppId { get; set; } = "";
        public string Secret { get; set; } = "";

        /// <summary>留空时用 <see cref="DefaultApiBaseAddress"/>；只在走代理/自建网关时才需要填。</summary>
        public string ApiBaseAddress { get; set; } = "";

        /// <summary>取 access_token 的地址；留空用官方统一地址。</summary>
        public string TokenAddress { get; set; } = DefaultTokenAddress;
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int Intents { get; set; } = DefaultIntents;

        /// <summary>回调时间戳允许的最大偏移（秒），用于防重放。</summary>
        public int SignatureMaxAgeSeconds { get; set; } = 300;

        /// <summary>心跳周期由 op 10 Hello 下发，此处仅作为网关请求超时兜底。</summary>
        public int GatewayRequestTimeoutSeconds { get; set; } = 15;

        /// <summary>留空时通过 <c>GET /gateway</c> 自动获取；只在要走固定网关（代理/自建）时才填。</summary>
        public string GatewayUrl { get; set; } = "";

        /// <summary>单连接起步，分片编号固定 0；分片总数取自网关接口的 shards（缺省按 1）。</summary>
        public int ShardId { get; set; }

        /// <summary>断线重连的指数退避上限（秒）。</summary>
        public int GatewayMaxReconnectDelaySeconds { get; set; } = 30;

        public string ResolvedApiBaseAddress => string.IsNullOrWhiteSpace(ApiBaseAddress)
            ? DefaultApiBaseAddress
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

        /// <summary>数据库备份目录（相对路径按程序工作目录解析）。不存在时首次备份自动创建。</summary>
        public string BackupDirectory { get; set; } = "backups";

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
