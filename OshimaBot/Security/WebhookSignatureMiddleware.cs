using System.Text.Json;
using Microsoft.Extensions.Options;
using OshimaBot.Models;

namespace OshimaBot.Security
{
    /// <summary>
    /// 仅在 QQBot:EnableWebhook=true 时对 POST /QQBot 做入站验签。
    /// op=13 回调地址验证请求按官方示例不携带签名头，仅校验 X-Bot-Appid。
    /// </summary>
    public sealed class WebhookSignatureMiddleware(
        RequestDelegate next,
        IOptions<QQBotOptions> options,
        ILogger<WebhookSignatureMiddleware> logger)
    {
        private readonly QQBotOptions _options = options.Value;

        public async Task InvokeAsync(HttpContext context, WebhookSignatureVerifier verifier)
        {
            if (!_options.EnableWebhook || !IsPlatformCallback(context.Request))
            {
                await next(context);
                return;
            }

            context.Request.EnableBuffering();
            using MemoryStream buffer = new();
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
            byte[] body = buffer.ToArray();
            context.Request.Body.Position = 0;

            string appId = context.Request.Headers[WebhookSignatureVerifier.AppIdHeader].ToString();
            if (!string.IsNullOrWhiteSpace(_options.AppId) && !string.Equals(appId, _options.AppId, StringComparison.Ordinal))
            {
                await RejectAsync(context, "X-Bot-Appid 与配置不匹配。");
                return;
            }

            // 官方 webhook.html 的 op=13 示例 headers 只有 User-Agent / X-Bot-Appid，
            // 验证请求由 plain_token + signature 机制自身完成，不做入站签名校验。
            if (TryGetOpCode(body, out int op) && op == 13)
            {
                await next(context);
                return;
            }

            WebhookVerificationResult result = verifier.Verify(
                context.Request.Headers[WebhookSignatureVerifier.SignatureHeader].ToString(),
                context.Request.Headers[WebhookSignatureVerifier.TimestampHeader].ToString(),
                appId,
                body,
                DateTimeOffset.UtcNow);

            if (!result.IsValid)
            {
                logger.LogWarning("拒绝 QQ Webhook 回调：{Failure}（{Reason}）", result.Failure, result.Reason);
                await RejectAsync(context, result.Reason);
                return;
            }

            await next(context);
        }

        private static bool IsPlatformCallback(HttpRequest request)
            => HttpMethods.IsPost(request.Method)
               && string.Equals(request.Path.Value?.TrimEnd('/'), "/QQBot", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetOpCode(byte[] body, out int op)
        {
            op = 0;
            if (body.Length == 0) return false;
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                return document.RootElement.TryGetProperty("op", out JsonElement element) && element.TryGetInt32(out op);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static Task RejectAsync(HttpContext context, string reason)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return context.Response.WriteAsJsonAsync(new { error = reason }, context.RequestAborted);
        }
    }
}
