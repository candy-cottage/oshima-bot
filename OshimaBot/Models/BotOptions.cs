namespace OshimaBot.Models
{
    public sealed class QQBotOptions
    {
        public const string SectionName = "QQBot";

        public bool Enabled { get; set; }
        public string AppId { get; set; } = "";
        public string Secret { get; set; } = "";
        public string ApiBaseAddress { get; set; } = "https://api.sgroup.qq.com";
        public string TokenAddress { get; set; } = "https://bots.qq.com/app/getAppAccessToken";
        public int RequestTimeoutSeconds { get; set; } = 30;

        public bool IsConfigured => Enabled
            && !string.IsNullOrWhiteSpace(AppId)
            && !string.IsNullOrWhiteSpace(Secret);
    }

    public sealed class ServerOptions
    {
        public const string SectionName = "Server";

        public string ApiKey { get; set; } = "";
        public int MaximumRespawnTimes { get; set; } = 10;
    }
}
