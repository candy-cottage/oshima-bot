using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Models;
using OshimaBot.Services;
using OshimaBot.Services.Gateway;
using Xunit;

namespace OshimaBot.Tests;

public sealed class GatewayProtocolTests
{
    [Fact]
    public void Identify_UsesOfficialTokenIntentsAndSingleShard()
    {
        using JsonDocument document = JsonDocument.Parse(
            GatewayProtocol.BuildIdentify("ACCESS", QQBotOptions.DefaultIntents));
        JsonElement payload = document.RootElement;
        Assert.Equal(2, payload.GetProperty("op").GetInt32());
        JsonElement data = payload.GetProperty("d");
        Assert.Equal("QQBot ACCESS", data.GetProperty("token").GetString());
        Assert.Equal(100663296, data.GetProperty("intents").GetInt32());
        Assert.Equal(new[] { 0, 1 }, data.GetProperty("shard").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.True(data.GetProperty("properties").TryGetProperty("$os", out _));
    }

    [Fact]
    public void Resume_CarriesSessionIdAndLastSeq()
    {
        using JsonDocument document = JsonDocument.Parse(
            GatewayProtocol.BuildResume("ACCESS", "session-1", 1337));
        JsonElement payload = document.RootElement;
        Assert.Equal(6, payload.GetProperty("op").GetInt32());
        JsonElement data = payload.GetProperty("d");
        Assert.Equal("QQBot ACCESS", data.GetProperty("token").GetString());
        Assert.Equal("session-1", data.GetProperty("session_id").GetString());
        Assert.Equal(1337, data.GetProperty("seq").GetInt64());
    }

    [Fact]
    public void Heartbeat_UsesNullBeforeFirstDispatch()
    {
        using JsonDocument first = JsonDocument.Parse(GatewayProtocol.BuildHeartbeat(null));
        Assert.Equal(JsonValueKind.Null, first.RootElement.GetProperty("d").ValueKind);

        using JsonDocument later = JsonDocument.Parse(GatewayProtocol.BuildHeartbeat(251));
        Assert.Equal(251, later.RootElement.GetProperty("d").GetInt32());
    }

    [Fact]
    public void Hello_ParsesIntervalWithSafeFallback()
    {
        using JsonDocument hello = JsonDocument.Parse("""{"heartbeat_interval":41250}""");
        Assert.Equal(41250, GatewayProtocol.ParseHeartbeatInterval(hello.RootElement));

        using JsonDocument broken = JsonDocument.Parse("""{}""");
        Assert.Equal(45000, GatewayProtocol.ParseHeartbeatInterval(broken.RootElement));

        using JsonDocument tooSmall = JsonDocument.Parse("""{"heartbeat_interval":1}""");
        Assert.Equal(1000, GatewayProtocol.ParseHeartbeatInterval(tooSmall.RootElement));
    }

    [Fact]
    public void Ready_ParsesSessionId()
    {
        using JsonDocument ready = JsonDocument.Parse(
            """{"version":1,"session_id":"082ee18c-0be3-491b-9d8b-fbd95c51673a","user":{"id":"1"}}""");
        Assert.Equal("082ee18c-0be3-491b-9d8b-fbd95c51673a", GatewayProtocol.ParseSessionId(ready.RootElement));
        Assert.Null(GatewayProtocol.ParseSessionId(JsonDocument.Parse("{}").RootElement));
    }

    [Theory]
    [InlineData(4009, GatewayCloseAction.Resume)]
    [InlineData(4008, GatewayCloseAction.Resume)]
    [InlineData(4006, GatewayCloseAction.Identify)]
    [InlineData(4007, GatewayCloseAction.Identify)]
    [InlineData(4900, GatewayCloseAction.Identify)]
    [InlineData(4913, GatewayCloseAction.Identify)]
    [InlineData(4004, GatewayCloseAction.RefreshToken)]
    [InlineData(4013, GatewayCloseAction.Stop)]
    [InlineData(4014, GatewayCloseAction.Stop)]
    [InlineData(4914, GatewayCloseAction.Stop)]
    [InlineData(4915, GatewayCloseAction.Stop)]
    [InlineData(4001, GatewayCloseAction.Stop)]
    public void ClosePolicy_FollowsOfficialErrorTable(int code, GatewayCloseAction expected)
        => Assert.Equal(expected, GatewayProtocol.DecideCloseAction(code));

    [Fact]
    public void ClosePolicy_NormalClosureAllowsResume()
    {
        Assert.Equal(GatewayCloseAction.Resume, GatewayProtocol.DecideCloseAction(null));
        Assert.Equal(GatewayCloseAction.Resume, GatewayProtocol.DecideCloseAction(1000));
    }
}

public sealed class GatewayInfrastructureTests
{
    private const string GatewayJson = """
        {"url":"wss://api.bot.qq.com/websocket/","shards":9,
         "session_start_limit":{"total":1000,"remaining":999,"reset_after":14400000,"max_concurrency":1}}
        """;

    [Fact]
    public async Task GatewayApiClient_ParsesShardInfoAndCachesResult()
    {
        StubHttpHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(GatewayJson, Encoding.UTF8, "application/json")
        });
        GatewayApiClient api = CreateApiClient(handler, new QQBotOptions { Enabled = true, AppId = "1", Secret = "s" });

        GatewayInfo info = await api.GetGatewayAsync();
        Assert.Equal("wss://api.bot.qq.com/websocket/", info.Url);
        Assert.Equal(9, info.Shards);
        Assert.Equal(1, info.MaxConcurrency);
        Assert.Equal(999, info.Remaining);
        Assert.Equal("/gateway/bot", Assert.Single(handler.Requests).RequestUri!.AbsolutePath);
        Assert.Equal("QQBot", Assert.Single(handler.Requests).Headers.Authorization!.Scheme);

        await api.GetGatewayAsync();
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GatewayApiClient_ConfiguredUrlSkipsRestCall()
    {
        StubHttpHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(GatewayJson, Encoding.UTF8, "application/json")
        });
        GatewayApiClient api = CreateApiClient(handler, new QQBotOptions
        {
            Enabled = true, AppId = "1", Secret = "s", GatewayUrl = "wss://custom.example/ws"
        });

        GatewayInfo info = await api.GetGatewayAsync();
        Assert.Equal("wss://custom.example/ws", info.Url);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SessionStore_RoundTripsAndClears()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        ServiceCollection services = new();
        services.AddDbContext<BotDbContext>(options => options.UseSqlite(connection));
        await using ServiceProvider provider = services.BuildServiceProvider();
        using (IServiceScope scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreatedAsync();
        }

        GatewaySessionStore store = new(provider.GetRequiredService<IServiceScopeFactory>());
        Assert.Null(await store.LoadAsync("APP1"));
        await store.SaveAsync("APP1", "session-1", 42);
        GatewaySessionState? loaded = await store.LoadAsync("APP1");
        Assert.Equal("session-1", loaded!.SessionId);
        Assert.Equal(42, loaded.LastSeq);

        await store.SaveAsync("APP1", "session-1", 99);
        Assert.Equal(99, (await store.LoadAsync("APP1"))!.LastSeq);
        await store.ClearAsync("APP1");
        Assert.Null(await store.LoadAsync("APP1"));
    }

    [Fact]
    public void Status_TracksConnectionLifecycle()
    {
        GatewayStatus status = new();
        Assert.False(status.Snapshot.Connected);
        status.MarkConnected("session-1");
        status.MarkSeq(7);
        Assert.True(status.Snapshot.Connected);
        Assert.Equal("session-1", status.Snapshot.SessionId);
        Assert.Equal(7, status.Snapshot.LastSeq);
        Assert.NotNull(status.Snapshot.ConnectedAtUtc);

        status.MarkDisconnected("closed");
        Assert.False(status.Snapshot.Connected);
        Assert.Equal("closed", status.Snapshot.LastError);
    }

    [Fact]
    public async Task GatewayClient_StaysIdleWhenBotIsDisabled()
    {
        StubHttpHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(GatewayJson, Encoding.UTF8, "application/json")
        });
        GatewayApiClient api = CreateApiClient(handler, new QQBotOptions { Enabled = false });
        QQBotGatewayClient client = new(
            Options.Create(new QQBotOptions { Enabled = false, Mode = QQBotMode.WebSocket }),
            api, new StubQQBotService(), new NoopDispatcher(), new InMemorySessionStore(),
            new GatewayStatus(), NullLogger<QQBotGatewayClient>.Instance);

        await client.StartAsync(CancellationToken.None);
        await client.StopAsync(CancellationToken.None);
        Assert.Empty(handler.Requests);
    }

    private static GatewayApiClient CreateApiClient(StubHttpHandler handler, QQBotOptions options)
        => new(new StubHttpClientFactory(handler), new StubQQBotService(), Options.Create(options),
            NullLogger<GatewayApiClient>.Instance);

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubQQBotService : IQQBotService
    {
        public Task SendC2CMessageAsync(string openid, string content, int msgType = 0, object? media = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null) => Task.CompletedTask;
        public Task SendGroupMessageAsync(string groupOpenid, string content, int msgType = 0, object? media = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null) => Task.CompletedTask;
        public Task SendC2CMarkdownAsync(string openid, MarkdownMessage markdown, KeyboardMessage? keyboard = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null) => Task.CompletedTask;
        public Task SendGroupMarkdownAsync(string groupOpenid, MarkdownMessage markdown, KeyboardMessage? keyboard = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null) => Task.CompletedTask;
        public Task<UploadMediaResult> UploadC2CMediaAsync(string openid, int fileType, string url, CancellationToken cancellationToken = default) => Task.FromResult(new UploadMediaResult());
        public Task<UploadMediaResult> UploadGroupMediaAsync(string groupOpenid, int fileType, string url, CancellationToken cancellationToken = default) => Task.FromResult(new UploadMediaResult());
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("ACCESS");
        public Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("ACCESS");
        public Task<bool> AckInteractionAsync(string interactionId, int code = 0, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class NoopDispatcher : IQQEventDispatcher
    {
        public Task<EventDispatchResult> DispatchAsync(Payload payload, QQEventSource source, CancellationToken cancellationToken = default)
            => Task.FromResult(EventDispatchResult.Accepted());
    }

    private sealed class InMemorySessionStore : IGatewaySessionStore
    {
        private GatewaySessionState? _state;
        public Task<GatewaySessionState?> LoadAsync(string appId, CancellationToken cancellationToken = default) => Task.FromResult(_state);
        public Task SaveAsync(string appId, string sessionId, long lastSeq, CancellationToken cancellationToken = default)
        {
            _state = new GatewaySessionState(sessionId, lastSeq);
            return Task.CompletedTask;
        }
        public Task ClearAsync(string appId, CancellationToken cancellationToken = default)
        {
            _state = null;
            return Task.CompletedTask;
        }
    }
}
