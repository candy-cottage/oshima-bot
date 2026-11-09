using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OshimaBot.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace OshimaBot.Services
{
    public interface IQQBotService
    {
        Task SendC2CMessageAsync(string openid, string content, int msgType = 0, object? media = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null);
        Task SendGroupMessageAsync(string groupOpenid, string content, int msgType = 0, object? media = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null);
        Task SendC2CMarkdownAsync(string openid, MarkdownMessage markdown, KeyboardMessage? keyboard = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null);
        Task SendGroupMarkdownAsync(string groupOpenid, MarkdownMessage markdown, KeyboardMessage? keyboard = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null);
        Task<UploadMediaResult> UploadC2CMediaAsync(string openid, int fileType, string url, CancellationToken cancellationToken = default);
        Task<UploadMediaResult> UploadGroupMediaAsync(string groupOpenid, int fileType, string url, CancellationToken cancellationToken = default);
        Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
        Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default);

        /// <summary>回应 INTERACTION_CREATE，否则客户端会一直 loading。同一 interaction_id 只能回一次。</summary>
        Task<bool> AckInteractionAsync(string interactionId, int code = 0, CancellationToken cancellationToken = default);
    }

    public sealed class QQBotService(
        IOptions<QQBotOptions> options,
        ILogger<QQBotService> logger,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache) : IQQBotService
    {
        public const string HttpClientName = "QQBot";
        private const string AccessTokenCacheKey = "OshimaBot:QQBot:AccessToken";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly QQBotOptions _options = options.Value;
        private readonly HttpClient _httpClient = httpClientFactory.CreateClient(HttpClientName);
        private readonly SemaphoreSlim _tokenLock = new(1, 1);

        public Task SendC2CMessageAsync(string openid, string content, int msgType = 0, object? media = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null)
            => SendMessageAsync($"/v2/users/{Uri.EscapeDataString(openid)}/messages", content, msgType, media, msgId, msgSeq, eventId, cancellationToken);

        public Task SendGroupMessageAsync(string groupOpenid, string content, int msgType = 0, object? media = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null)
            => SendMessageAsync($"/v2/groups/{Uri.EscapeDataString(groupOpenid)}/messages", content, msgType, media, msgId, msgSeq, eventId, cancellationToken);

        public Task SendC2CMarkdownAsync(string openid, MarkdownMessage markdown, KeyboardMessage? keyboard = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null)
            => SendMarkdownMessageAsync($"/v2/users/{Uri.EscapeDataString(openid)}/messages", markdown, keyboard, msgId, msgSeq, eventId, cancellationToken);

        public Task SendGroupMarkdownAsync(string groupOpenid, MarkdownMessage markdown, KeyboardMessage? keyboard = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null)
            => SendMarkdownMessageAsync($"/v2/groups/{Uri.EscapeDataString(groupOpenid)}/messages", markdown, keyboard, msgId, msgSeq, eventId, cancellationToken);

        private async Task SendMessageAsync(string path, string content, int msgType, object? media, string? msgId, int? msgSeq, string? eventId, CancellationToken cancellationToken)
        {
            Dictionary<string, object?> body = new()
            {
                ["content"] = content,
                ["msg_type"] = msgType
            };
            AddMessageContext(body, media, msgId, msgSeq, eventId);
            await PostAsync(path, body, cancellationToken);
        }

        private async Task SendMarkdownMessageAsync(string path, MarkdownMessage markdown, KeyboardMessage? keyboard, string? msgId, int? msgSeq, string? eventId, CancellationToken cancellationToken)
        {
            Dictionary<string, object?> body = new()
            {
                ["msg_type"] = 2,
                ["markdown"] = markdown
            };
            if (keyboard is not null) body["keyboard"] = keyboard;
            AddMessageContext(body, null, msgId, msgSeq, eventId);
            await PostAsync(path, body, cancellationToken);
        }

        /// <summary>event_id 与 msg_id 二选一：互动事件用 event_id，普通消息事件用 msg_id + 递增 msg_seq。</summary>
        private static void AddMessageContext(Dictionary<string, object?> body, object? media, string? msgId, int? msgSeq, string? eventId)
        {
            if (media is not null) body["media"] = media;
            if (!string.IsNullOrWhiteSpace(eventId))
            {
                body["event_id"] = eventId;
                return;
            }
            if (!string.IsNullOrWhiteSpace(msgId)) body["msg_id"] = msgId;
            if (msgSeq.HasValue) body["msg_seq"] = msgSeq.Value;
        }

        public async Task<bool> AckInteractionAsync(string interactionId, int code = 0, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(interactionId)) return false;

            EnsureConfigured();
            string accessToken = await GetAccessTokenAsync(cancellationToken);
            using HttpRequestMessage request = new(HttpMethod.Put,
                $"{_options.ResolvedApiBaseAddress}/interactions/{Uri.EscapeDataString(interactionId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("QQBot", accessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(new { code }, JsonOptions), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return true;

            string error = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("互动事件回执失败：InteractionId={InteractionId}, Status={StatusCode}, {Response}",
                interactionId, (int)response.StatusCode, error);
            return false;
        }

        public Task<UploadMediaResult> UploadC2CMediaAsync(string openid, int fileType, string url, CancellationToken cancellationToken = default)
            => UploadMediaAsync($"/v2/users/{Uri.EscapeDataString(openid)}/files", fileType, url, cancellationToken);

        public Task<UploadMediaResult> UploadGroupMediaAsync(string groupOpenid, int fileType, string url, CancellationToken cancellationToken = default)
            => UploadMediaAsync($"/v2/groups/{Uri.EscapeDataString(groupOpenid)}/files", fileType, url, cancellationToken);

        private async Task<UploadMediaResult> UploadMediaAsync(string path, int fileType, string fileUrl, CancellationToken cancellationToken)
        {
            Dictionary<string, object> body = new()
            {
                ["file_type"] = fileType,
                ["url"] = fileUrl,
                ["srv_send_msg"] = false
            };

            using HttpResponseMessage response = await PostAsync(path, body, cancellationToken, throwOnError: false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("QQ 富媒体上传失败：{StatusCode}，{Response}", (int)response.StatusCode, responseBody);
                return new UploadMediaResult { Error = $"QQ 富媒体上传失败（HTTP {(int)response.StatusCode}）。" };
            }

            MediaResponse? media = JsonSerializer.Deserialize<MediaResponse>(responseBody, JsonOptions);
            if (media is null || string.IsNullOrWhiteSpace(media.FileInfo))
            {
                return new UploadMediaResult { Error = "QQ 富媒体响应格式无效。" };
            }

            return new UploadMediaResult
            {
                FileUuid = media.FileUuid,
                FileInfo = media.FileInfo,
                Ttl = media.Ttl
            };
        }

        private async Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken cancellationToken, bool throwOnError = true)
        {
            EnsureConfigured();
            string accessToken = await GetAccessTokenAsync(cancellationToken);
            using HttpRequestMessage request = new(HttpMethod.Post, $"{_options.ResolvedApiBaseAddress}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("QQBot", accessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            if (throwOnError && !response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Dispose();
                logger.LogError("QQ 消息接口调用失败：{StatusCode}，{Response}", (int)response.StatusCode, error);
                throw new HttpRequestException($"QQ 消息接口返回 HTTP {(int)response.StatusCode}。");
            }
            return response;
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            EnsureConfigured();
            if (memoryCache.TryGetValue(AccessTokenCacheKey, out string? token) && !string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                if (memoryCache.TryGetValue(AccessTokenCacheKey, out token) && !string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
                return await RequestAccessTokenAsync(cancellationToken);
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        public async Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default)
        {
            EnsureConfigured();
            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                return await RequestAccessTokenAsync(cancellationToken);
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private async Task<string> RequestAccessTokenAsync(CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, _options.TokenAddress)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    appId = _options.AppId,
                    clientSecret = _options.Secret
                }), Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("QQ AccessToken 获取失败：{StatusCode}", (int)response.StatusCode);
                throw new HttpRequestException($"QQ AccessToken 接口返回 HTTP {(int)response.StatusCode}。");
            }

            AccessTokenResponse? token = JsonSerializer.Deserialize<AccessTokenResponse>(responseBody, JsonOptions);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken) || !int.TryParse(token.ExpiresIn, out int expiresIn) || expiresIn <= 0)
            {
                throw new InvalidOperationException("QQ AccessToken 响应格式无效。");
            }

            int cacheSeconds = Math.Max(1, expiresIn - Math.Min(60, expiresIn / 10));
            memoryCache.Set(AccessTokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(cacheSeconds));
            logger.LogInformation("QQ AccessToken 已刷新，有效期约 {ExpiresIn} 秒。", expiresIn);
            return token.AccessToken;
        }

        private void EnsureConfigured()
        {
            if (!_options.IsConfigured)
            {
                throw new InvalidOperationException("QQBot 未启用或凭据不完整，请配置 QQBot:Enabled、AppId 和 Secret。");
            }
        }
    }
}
