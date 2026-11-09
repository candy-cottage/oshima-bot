using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OshimaBot.Models;

namespace OshimaBot.Services.Gateway;

public interface IGatewayApiClient
{
    Task<GatewayInfo> GetGatewayAsync(CancellationToken cancellationToken = default);
}

/// <summary>GET /gateway/bot 获取 WSS 地址与建议分片数；结果缓存，避免每次重连都消耗接口配额。</summary>
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
        using HttpRequestMessage request = new(HttpMethod.Get, $"{_options.ResolvedApiBaseAddress}/gateway/bot");
        request.Headers.Authorization = new AuthenticationHeaderValue("QQBot", token);
        using HttpResponseMessage response = await httpClientFactory
            .CreateClient(QQBotService.HttpClientName).SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"获取 QQ 网关地址失败：HTTP {(int)response.StatusCode}，{body}");
        }

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string url = root.GetProperty("url").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("QQ 网关响应缺少 url。");
        int shards = Math.Max(1, root.TryGetProperty("shards", out JsonElement s) ? s.GetInt32() : 1);
        int maxConcurrency = 1;
        int remaining = int.MaxValue;
        if (root.TryGetProperty("session_start_limit", out JsonElement limit))
        {
            if (limit.TryGetProperty("max_concurrency", out JsonElement mc)) maxConcurrency = Math.Max(1, mc.GetInt32());
            if (limit.TryGetProperty("remaining", out JsonElement rem)) remaining = rem.GetInt32();
        }

        _cached = new GatewayInfo(url, shards, maxConcurrency, remaining);
        logger.LogInformation("QQ WSS 网关：Url={Url}, Shards={Shards}, MaxConcurrency={MaxConcurrency}, Remaining={Remaining}",
            url, shards, maxConcurrency, remaining);
        return _cached;
    }
}
