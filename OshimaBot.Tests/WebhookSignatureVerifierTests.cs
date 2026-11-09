using System.Text;
using Microsoft.Extensions.Options;
using OshimaBot.Models;
using OshimaBot.Security;
using Rebex.Security.Cryptography;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 测试数据取自 QQ 机器人官方文档：
/// 安全和授权 https://bot.qq.com/wiki/develop/api-v2/dev-prepare/interface-framework/sign.html
/// Webhook 方式 https://bot.qq.com/wiki/develop/api-v2/dev-prepare/event-emit/webhook.html
///
/// 注意：sign.html DEMO 中的 sig（865ad13a…）与其展示的 secret/body 并不自洽，
/// 用同一 secret 对 "timestamp + body" 实签得到的是 2eb9983e…。
/// 因此验签正例改用「自签往返」，另有 DocumentedDemoSignature_IsNotReproducible 记录该差异。
/// 密钥派生、op=13 验证签名仍以官方 DEMO 为准（均可精确复现）。
/// </summary>
public sealed class WebhookSignatureVerifierTests
{
    private const string Secret = "naOC0ocQE3shWLAfffVLB1rhYPG7";
    private const string OfficialSeed = "naOC0ocQE3shWLAfffVLB1rhYPG7naOC";
    private const string Body = "{ \"op\": 0,\"d\": {}, \"t\": \"GATEWAY_EVENT_NAME\"}";
    private const string Timestamp = "1725442341";
    private const string DocumentedDemoSignature =
        "865ad13a61752ca65e26bde6676459cd36cf1be609375b37bd62af366e1dc25a8dc789ba7f14e017ada3d554c671a911bfdf075ba54835b23391d509579ed002";
    private const string AppId = "11111111";

    private static readonly byte[] OfficialPublicKey =
    [
        215, 195, 98, 254, 120, 174, 248, 31, 242, 50, 135, 180, 147, 98,
        139, 93, 176, 42, 60, 79, 227, 11, 33, 94, 77, 25, 96, 155, 93,
        118, 103, 58
    ];

    private static readonly DateTimeOffset EventTime = DateTimeOffset.FromUnixTimeSeconds(1725442341);

    private static WebhookSignatureVerifier Create(string secret = Secret, string appId = AppId, int maxAge = 300)
        => new(Options.Create(new QQBotOptions
        {
            Secret = secret,
            AppId = appId,
            SignatureMaxAgeSeconds = maxAge
        }));

    /// <summary>用官方 DEMO 的 secret 对 "timestamp + body" 生成合法签名，供往返测试使用。</summary>
    private static string SignBody(string body = Body)
    {
        Ed25519 ed25519 = new();
        ed25519.FromSeed(WebhookSignatureVerifier.DeriveSeed(Secret));
        byte[] signature = ed25519.SignMessage(Encoding.UTF8.GetBytes(Timestamp + body));
        return Convert.ToHexString(signature).ToLowerInvariant();
    }

    [Fact]
    public void DeriveSeed_MatchesOfficialDemo()
    {
        byte[] seed = WebhookSignatureVerifier.DeriveSeed(Secret);
        Assert.Equal(32, seed.Length);
        Assert.Equal(OfficialSeed, Encoding.UTF8.GetString(seed));
    }

    [Fact]
    public void DerivePublicKey_MatchesOfficialDemo()
        => Assert.Equal(OfficialPublicKey, WebhookSignatureVerifier.DerivePublicKey(Secret));

    [Fact]
    public void DocumentedDemoSignature_IsNotReproducible()
        => Assert.NotEqual(DocumentedDemoSignature, SignBody());

    [Fact]
    public void RoundTrip_SignatureIsAccepted()
    {
        WebhookVerificationResult result = Create().Verify(
            SignBody(), Timestamp, AppId, Encoding.UTF8.GetBytes(Body), EventTime);
        Assert.True(result.IsValid, $"{result.Failure}: {result.Reason}");
    }

    [Fact]
    public void TamperedBody_IsRejected()
    {
        byte[] body = Encoding.UTF8.GetBytes(Body.Replace("GATEWAY_EVENT_NAME", "GATEWAY_EVENT_OTHER"));
        WebhookVerificationResult result = Create().Verify(SignBody(), Timestamp, AppId, body, EventTime);
        Assert.False(result.IsValid);
        Assert.Equal(WebhookVerificationFailure.InvalidSignature, result.Failure);
    }

    [Fact]
    public void TamperedSignature_IsRejected()
    {
        string signature = SignBody();
        string tampered = (signature[0] == '8' ? "7" : "8") + signature[1..];
        WebhookVerificationResult result = Create().Verify(
            tampered, Timestamp, AppId, Encoding.UTF8.GetBytes(Body), EventTime);
        Assert.False(result.IsValid);
        Assert.Equal(WebhookVerificationFailure.InvalidSignature, result.Failure);
    }

    [Fact]
    public void MissingHeaders_AreRejected()
    {
        WebhookVerificationResult result = Create().Verify(
            null, null, AppId, Encoding.UTF8.GetBytes(Body), EventTime);
        Assert.Equal(WebhookVerificationFailure.MissingHeaders, result.Failure);
    }

    [Fact]
    public void MalformedSignature_IsRejected()
    {
        WebhookVerificationResult result = Create().Verify(
            "not-hex", Timestamp, AppId, Encoding.UTF8.GetBytes(Body), EventTime);
        Assert.Equal(WebhookVerificationFailure.MalformedSignature, result.Failure);
    }

    [Fact]
    public void ExpiredTimestamp_IsRejected()
    {
        WebhookVerificationResult result = Create().Verify(
            SignBody(), Timestamp, AppId, Encoding.UTF8.GetBytes(Body), EventTime.AddMinutes(30));
        Assert.Equal(WebhookVerificationFailure.TimestampExpired, result.Failure);
    }

    [Fact]
    public void AppIdMismatch_IsRejected()
    {
        WebhookVerificationResult result = Create().Verify(
            SignBody(), Timestamp, "99999999", Encoding.UTF8.GetBytes(Body), EventTime);
        Assert.Equal(WebhookVerificationFailure.AppIdMismatch, result.Failure);
    }

    [Fact]
    public void MissingSecret_IsRejected()
    {
        WebhookVerificationResult result = Create(secret: "").Verify(
            SignBody(), Timestamp, AppId, Encoding.UTF8.GetBytes(Body), EventTime);
        Assert.Equal(WebhookVerificationFailure.MissingSecret, result.Failure);
    }

    [Fact]
    public void ValidationSignature_MatchesOfficialWebhookDemo()
    {
        // 官方 webhook.html：appid 11111111 / secret DG5g3B4j9X2KOErG
        const string secret = "DG5g3B4j9X2KOErG";
        const string plainToken = "Arq0D5A61EgUu4OxUvOp";
        const string eventTs = "1725442341";
        const string expected =
            "87befc99c42c651b3aac0278e71ada338433ae26fcb24307bdc5ad38c1adc2d01bcfcadc0842edac85e85205028a1132afe09280305f13aa6909ffc2d652c706";

        byte[] signature = WebhookSignatureVerifier.SignValidation(secret, eventTs, plainToken);
        Assert.Equal(expected, Convert.ToHexString(signature).ToLowerInvariant());
    }
}

public sealed class QQBotOptionsTests
{
    [Fact]
    public void Defaults_UseWebSocketSingleMode()
    {
        QQBotOptions options = new();
        Assert.Equal(QQBotMode.WebSocket, options.Mode);
        Assert.False(options.EnableWebhook);
        Assert.Equal(100663296, options.Intents);
    }

    [Fact]
    public void ApiBaseAddress_ResolvesByEnvironment()
    {
        Assert.Equal("https://sandbox.api.sgroup.qq.com", new QQBotOptions().ResolvedApiBaseAddress);
        Assert.Equal("https://api.sgroup.qq.com", new QQBotOptions
        {
            Environment = QQBotEnvironment.Production
        }.ResolvedApiBaseAddress);
        Assert.Equal("https://example.test", new QQBotOptions
        {
            ApiBaseAddress = "https://example.test/"
        }.ResolvedApiBaseAddress);
    }
}
