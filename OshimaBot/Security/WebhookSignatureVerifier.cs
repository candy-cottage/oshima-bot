using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using OshimaBot.Models;
using Rebex.Security.Cryptography;

namespace OshimaBot.Security
{
    public enum WebhookVerificationFailure
    {
        None,
        MissingSecret,
        MissingHeaders,
        AppIdMismatch,
        MalformedSignature,
        InvalidTimestamp,
        TimestampExpired,
        InvalidSignature
    }

    public readonly record struct WebhookVerificationResult(bool IsValid, WebhookVerificationFailure Failure, string Reason)
    {
        public static WebhookVerificationResult Success() => new(true, WebhookVerificationFailure.None, "");
        public static WebhookVerificationResult Fail(WebhookVerificationFailure failure, string reason) => new(false, failure, reason);
    }

    /// <summary>
    /// QQ 官方 Webhook 入站验签：Bot Secret repeat 补齐 32 字节作为 Ed25519 seed，
    /// 由 seed 派生公钥，校验 X-Signature-Ed25519 对 "timestamp + 原始 body" 的签名。
    /// 参考 https://bot.qq.com/wiki/develop/api-v2/dev-prepare/interface-framework/sign.html
    /// </summary>
    public sealed class WebhookSignatureVerifier
    {
        public const string SignatureHeader = "X-Signature-Ed25519";
        public const string TimestampHeader = "X-Signature-Timestamp";
        public const string AppIdHeader = "X-Bot-Appid";

        private readonly byte[]? _publicKey;
        private readonly string _appId;
        private readonly int _maxAgeSeconds;

        public WebhookSignatureVerifier(IOptions<QQBotOptions> options)
        {
            QQBotOptions value = options.Value;
            _publicKey = string.IsNullOrEmpty(value.Secret) ? null : DerivePublicKey(value.Secret);
            _appId = value.AppId;
            _maxAgeSeconds = Math.Max(1, value.SignatureMaxAgeSeconds);
        }

        public static byte[] DeriveSeed(string secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                throw new ArgumentException("Bot Secret 不能为空。", nameof(secret));
            }

            string seed = secret;
            while (seed.Length < 32) seed += seed;
            return Encoding.UTF8.GetBytes(seed[..32]);
        }

        public static byte[] DerivePublicKey(string secret)
        {
            Ed25519 ed25519 = new();
            ed25519.FromSeed(DeriveSeed(secret));
            return ed25519.GetPublicKey();
        }

        /// <summary>op=13 回调地址验证：对 "event_ts + plain_token" 签名。</summary>
        public static byte[] SignValidation(string secret, string eventTs, string plainToken)
        {
            Ed25519 ed25519 = new();
            ed25519.FromSeed(DeriveSeed(secret));
            return ed25519.SignMessage(Encoding.UTF8.GetBytes(eventTs + plainToken));
        }

        public WebhookVerificationResult Verify(
            string? signatureHex,
            string? timestamp,
            string? appId,
            ReadOnlySpan<byte> body,
            DateTimeOffset now)
        {
            if (_publicKey is null)
            {
                return WebhookVerificationResult.Fail(WebhookVerificationFailure.MissingSecret, "QQBot Secret 尚未配置。");
            }

            if (string.IsNullOrWhiteSpace(signatureHex) || string.IsNullOrWhiteSpace(timestamp))
            {
                return WebhookVerificationResult.Fail(WebhookVerificationFailure.MissingHeaders, "缺少签名或时间戳请求头。");
            }

            if (!string.IsNullOrWhiteSpace(_appId) && !string.Equals(appId, _appId, StringComparison.Ordinal))
            {
                return WebhookVerificationResult.Fail(WebhookVerificationFailure.AppIdMismatch, "X-Bot-Appid 与配置不匹配。");
            }

            byte[] signature;
            try
            {
                signature = Convert.FromHexString(signatureHex);
            }
            catch (FormatException)
            {
                return WebhookVerificationResult.Fail(WebhookVerificationFailure.MalformedSignature, "签名不是合法的十六进制字符串。");
            }

            if (signature.Length != 64)
            {
                return WebhookVerificationResult.Fail(WebhookVerificationFailure.MalformedSignature, "Ed25519 签名长度必须为 64 字节。");
            }

            if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
            {
                return WebhookVerificationResult.Fail(WebhookVerificationFailure.InvalidTimestamp, "时间戳格式无效。");
            }

            if (Math.Abs(now.ToUnixTimeSeconds() - seconds) > _maxAgeSeconds)
            {
                return WebhookVerificationResult.Fail(WebhookVerificationFailure.TimestampExpired, "回调时间戳超出允许范围。");
            }

            byte[] timestampBytes = Encoding.UTF8.GetBytes(timestamp);
            byte[] signed = new byte[timestampBytes.Length + body.Length];
            Buffer.BlockCopy(timestampBytes, 0, signed, 0, timestampBytes.Length);
            body.CopyTo(signed.AsSpan(timestampBytes.Length));

            Ed25519 ed25519 = new();
            ed25519.FromPublicKey(_publicKey);
            // Rebex 的 VerifyMessage 参数顺序是 (message, signature)。
            return ed25519.VerifyMessage(signed, signature)
                ? WebhookVerificationResult.Success()
                : WebhookVerificationResult.Fail(WebhookVerificationFailure.InvalidSignature, "Ed25519 签名校验失败。");
        }
    }
}
