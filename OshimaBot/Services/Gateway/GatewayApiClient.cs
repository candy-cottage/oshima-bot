using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OshimaBot.Models;

namespace OshimaBot.Services.Gateway;

public interface IGatewayApiClient
{
    Task<GatewayInfo> GetGatewayAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 获取 WSS 地址与建议分片数；结果缓存，避免每次重连都消耗接口配额。
/// <para>当前文档是 <c>GET /gateway</c>（api-v2 通用接入点）；旧的 <c>/gateway/bot</c> 作为兜底保留——
/// 域名统一那次改动之后，路径也可能被一起收掉，两条都试比猜一个更安全。</para>
/// </summary>
public sealed class GatewayApiClient(
    IHttpClientFactory httpClientFactory,
    IQQBotService qqBot,
    IOptions<QQBotOptions> options,
    ILogger<GatewayApiClient> logger) : IGatewayApiClient
{
    private readonly QQBotOptions _options = options.Value;
    private GatewayInfo? _cached;

    public async Task<GatewayInfo> GetGatewayAsync(CancellationToken cancellationToken = default)
    {
        // 允许显式指定网关地址（沙箱调试、内网代理），此时不发 REST 请求。
        if (!string.IsNullOrWhiteSpace(_options.GatewayUrl))
        {
            return new GatewayInfo(_options.GatewayUrl.Trim(), 1, 1, int.MaxValue);
        }
        if (_cached is not null) return _cached;

        string token = await qqBot.GetAccessTokenAsync(cancellationToken);
        (string url, int shards, int maxConcurrency, int remaining) = await RequestGatewayAsync(token, cancellationToken);

        _cached = new GatewayInfo(url, shards, maxConcurrency, remaining);
        logger.LogInformation("QQ WSS 网关：Url={Url}, Shards={Shards}, MaxConcurrency={MaxConcurrency}, Remaining={Remaining}",
            url, shards, maxConcurrency, remaining);
        return _cached;
    }

    /// <summary>依次尝试 <c>/gateway</c> 与 <c>/gateway/bot</c>，取第一个真的给出 url 的响应。</summary>
    private async Task<(string Url, int Shards, int MaxConcurrency, int Remaining)> RequestGatewayAsync(
        string token, CancellationToken cancellationToken)
    {
        List<string> failures = [];
        foreach (string path in (string[])["/gateway", "/gateway/bot"])
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"{_options.ResolvedApiBaseAddress}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("QQBot", token);
            using HttpResponseMessage response = await httpClientFactory
                .CreateClient(QQBotService.HttpClientName).SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                failures.Add($"{path} → HTTP {(int)response.StatusCode}，{Trim(body)}");
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string url = root.TryGetProperty("url", out JsonElement urlElement)
                ? urlElement.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(url))
            {
                failures.Add($"{path} → 响应里没有 url，{Trim(body)}");
                continue;
            }

            int shards = Math.Max(1, root.TryGetProperty("shards", out JsonElement s) ? s.GetInt32() : 1);
            int maxConcurrency = 1;
            int remaining = int.MaxValue;
            if (root.TryGetProperty("session_start_limit", out JsonElement limit))
            {
                if (limit.TryGetProperty("max_concurrency", out JsonElement mc)) maxConcurrency = Math.Max(1, mc.GetInt32());
                if (limit.TryGetProperty("remaining", out JsonElement rem)) remaining = rem.GetInt32();
            }
            if (path != "/gateway")
            {
                logger.LogWarning("网关接口 {Path} 已回退到旧路径，官方可能已改结构，确认一下文档。", path);
            }
            return (url, shards, maxConcurrency, remaining);
        }

        throw new HttpRequestException($"获取 QQ 网关地址失败：{string.Join("；", failures)}");
    }

    private static string Trim(string body) => body.Length <= 200 ? body : body[..200] + "…";
}
