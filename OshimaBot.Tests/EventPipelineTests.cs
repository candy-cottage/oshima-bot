using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OshimaBot.Data;
using OshimaBot.Messaging;
using OshimaBot.Models;
using OshimaBot.Security;
using OshimaBot.Services;
using Xunit;

namespace OshimaBot.Tests;

public sealed class EventPipelineTests
{
    private const string C2CJson = """
        {"id":"EVT1","op":0,"s":7,"t":"C2C_MESSAGE_CREATE",
         "d":{"id":"MSG1","author":{"user_openid":"U1","member_openid":""},
              "content":"查角色 1","timestamp":"2026-11-09 12:00:00"}}
        """;

    private const string InteractionJson = """
        {"id":"INTERACTION_CREATE:int-1","op":0,"s":8,"t":"INTERACTION_CREATE",
         "d":{"id":"int-1","chat_type":0,"timestamp":"2026-11-09 12:00:01","user_openid":"U1",
              "data":{"resolved":{"button_id":"b1","button_data":"/签到","message_id":"M1"}}}}
        """;

    private static Payload Parse(string json) => JsonSerializer.Deserialize<Payload>(json)!;

    private static QQEventDispatcher CreateDispatcher(
        FakeDedup dedup, FakeAck ack, FakeSink sink) => new(
            dedup, ack, sink, new FakeUserDirectory(), NullLogger<QQEventDispatcher>.Instance);

    [Fact]
    public async Task DedupStore_SecondMarkIsRejected()
    {
        using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<BotDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<IEventDeduplicationStore, EventDeduplicationStore>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using (IServiceScope scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreatedAsync();
        }

        IEventDeduplicationStore store = provider.GetRequiredService<IEventDeduplicationStore>();
        Assert.True(await store.TryMarkAsync("EVT1"));
        Assert.False(await store.TryMarkAsync("EVT1"));
        Assert.True(await store.TryMarkAsync("EVT2"));

        await store.MarkProcessedAsync("EVT1");
        Assert.Equal(2, await store.PurgeAsync(TimeSpan.Zero));
        Assert.True(await store.TryMarkAsync("EVT1"));
    }

    [Fact]
    public async Task Dispatcher_AcceptsC2CEventWithMsgId()
    {
        FakeDedup dedup = new();
        FakeAck ack = new();
        FakeSink sink = new();
        QQEventDispatcher dispatcher = CreateDispatcher(dedup, ack, sink);

        EventDispatchResult result = await dispatcher.DispatchAsync(Parse(C2CJson), QQEventSource.WebSocket);

        Assert.Equal(EventDispatchStatus.Accepted, result.Status);
        IBotMessage message = Assert.Single(sink.Messages);
        Assert.IsType<C2CMessage>(message);
        Assert.Equal("MSG1", message.Id);
        Assert.Equal("U1", message.OpenId);
        Assert.Null(message.EventId);
        Assert.Empty(ack.InteractionIds);
        Assert.Equal(1, dedup.MarkedCount);
    }

    [Fact]
    public async Task Dispatcher_AttachesResolvedIdentityBeforeQueue()
    {
        FakeDedup dedup = new();
        FakeSink sink = new();
        FakeUserDirectory users = new();
        QQEventDispatcher dispatcher = new(dedup, new FakeAck(), sink, users, NullLogger<QQEventDispatcher>.Instance);

        EventDispatchResult result = await dispatcher.DispatchAsync(Parse(C2CJson), QQEventSource.WebSocket);

        Assert.Equal(EventDispatchStatus.Accepted, result.Status);
        IBotMessage message = Assert.Single(sink.Messages);
        Assert.Equal(42, message.FunGameUID);
        Assert.NotNull(message.Principal);
        Assert.Equal("42", message.Principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        Assert.Equal(1, users.Calls);
    }

    [Fact]
    public async Task Dispatcher_RejectsDuplicateAcrossBothSources()
    {
        FakeDedup dedup = new();
        FakeSink sink = new();
        QQEventDispatcher dispatcher = CreateDispatcher(dedup, new FakeAck(), sink);

        Assert.Equal(EventDispatchStatus.Accepted,
            (await dispatcher.DispatchAsync(Parse(C2CJson), QQEventSource.WebSocket)).Status);
        Assert.Equal(EventDispatchStatus.Duplicate,
            (await dispatcher.DispatchAsync(Parse(C2CJson), QQEventSource.Webhook)).Status);
        Assert.Single(sink.Messages);
    }

    [Fact]
    public async Task Dispatcher_IgnoresUnknownEventButStillMarksIt()
    {
        FakeDedup dedup = new();
        FakeSink sink = new();
        QQEventDispatcher dispatcher = CreateDispatcher(dedup, new FakeAck(), sink);
        Payload payload = Parse("""{"id":"EVT9","op":0,"s":9,"t":"FRIEND_ADD","d":{}}""");

        EventDispatchResult result = await dispatcher.DispatchAsync(payload, QQEventSource.WebSocket);

        Assert.Equal(EventDispatchStatus.Ignored, result.Status);
        Assert.Empty(sink.Messages);
        Assert.Equal(1, dedup.MarkedCount);
    }

    [Fact]
    public async Task Dispatcher_NonDispatchOpcodeIsIgnoredWithoutDedup()
    {
        FakeDedup dedup = new();
        FakeSink sink = new();
        QQEventDispatcher dispatcher = CreateDispatcher(dedup, new FakeAck(), sink);
        Payload payload = Parse("""{"id":"EVT10","op":10,"d":{"heartbeat_interval":45000}}""");

        EventDispatchResult result = await dispatcher.DispatchAsync(payload, QQEventSource.WebSocket);

        Assert.Equal(EventDispatchStatus.Ignored, result.Status);
        Assert.Empty(dedup.Seen);
        Assert.Empty(sink.Messages);
    }

    [Fact]
    public async Task Dispatcher_InteractionAcksWithDIdAndRepliesByEventId()
    {
        FakeDedup dedup = new();
        FakeAck ack = new();
        FakeSink sink = new();
        QQEventDispatcher dispatcher = CreateDispatcher(dedup, ack, sink);

        EventDispatchResult result = await dispatcher.DispatchAsync(Parse(InteractionJson), QQEventSource.WebSocket);

        Assert.Equal(EventDispatchStatus.Accepted, result.Status);
        Assert.Equal("int-1", Assert.Single(ack.InteractionIds)); // 不带 INTERACTION_CREATE: 前缀
        IBotMessage message = Assert.Single(sink.Messages);
        Assert.Equal("INTERACTION_CREATE:int-1", message.EventId);
        Assert.Equal("/签到", message.Detail);
    }

    [Fact]
    public async Task Dispatcher_InvalidPayloadReturnsInvalid()
    {
        FakeDedup dedup = new();
        FakeSink sink = new();
        QQEventDispatcher dispatcher = CreateDispatcher(dedup, new FakeAck(), sink);
        Payload payload = Parse("""{"id":"EVT11","op":0,"s":11,"t":"C2C_MESSAGE_CREATE","d":{}}""");

        EventDispatchResult result = await dispatcher.DispatchAsync(payload, QQEventSource.Webhook);

        Assert.Equal(EventDispatchStatus.Invalid, result.Status);
        Assert.Empty(sink.Messages);
    }

    [Fact]
    public async Task Dispatcher_SinkFailureForgetsDedupMarkerForRetry()
    {
        FakeDedup dedup = new();
        FakeSink sink = new() { Throw = true };
        QQEventDispatcher dispatcher = CreateDispatcher(dedup, new FakeAck(), sink);

        EventDispatchResult result = await dispatcher.DispatchAsync(Parse(C2CJson), QQEventSource.WebSocket);

        Assert.Equal(EventDispatchStatus.Failed, result.Status);
        Assert.Equal("EVT1", Assert.Single(dedup.Forgotten));
        sink.Throw = false;
        Assert.Equal(EventDispatchStatus.Accepted,
            (await dispatcher.DispatchAsync(Parse(C2CJson), QQEventSource.WebSocket)).Status);
    }

    [Fact]
    public async Task MessageSender_UsesEventIdWithoutMsgIdOrSeq()
    {
        RecordingQQBotService qqBot = new();
        MessageSender sender = new(qqBot, new MemoryCache(new MemoryCacheOptions()), NullLogger<MessageSender>.Instance);
        C2CMessage message = new()
        {
            Id = "MSG1", Content = "查角色 1", Author = new Author { UserOpenId = "U1" },
            EventId = "INTERACTION_CREATE:int-1"
        };

        await sender.SendAsync(message, BotReply.FromText("结果"));

        SentCall call = Assert.Single(qqBot.Calls);
        Assert.Equal("c2c-text", call.Kind);
        Assert.Equal("INTERACTION_CREATE:int-1", call.EventId);
        Assert.Null(call.MsgId);
        Assert.Null(call.MsgSeq);
    }

    [Fact]
    public async Task MessageSender_IncrementsMsgSeqPerMsgId()
    {
        RecordingQQBotService qqBot = new();
        MessageSender sender = new(qqBot, new MemoryCache(new MemoryCacheOptions()), NullLogger<MessageSender>.Instance);
        C2CMessage message = new() { Id = "MSG1", Content = "查角色 1", Author = new Author { UserOpenId = "U1" } };

        await sender.SendAsync(message, BotReply.FromText("第一次"));
        await sender.SendAsync(message, BotReply.FromText("第二次"));
        await sender.SendAsync(new C2CMessage { Id = "MSG2", Content = "x", Author = new Author { UserOpenId = "U1" } },
            BotReply.FromText("另一条"));

        Assert.Equal(3, qqBot.Calls.Count);
        Assert.Equal(1, qqBot.Calls[0].MsgSeq);
        Assert.Equal(2, qqBot.Calls[1].MsgSeq);
        Assert.Equal(1, qqBot.Calls[2].MsgSeq);
        Assert.All(qqBot.Calls, call => Assert.Null(call.EventId));
    }

    [Fact]
    public async Task MessageSender_ProactiveMessageHasNoPassiveIdentifiers()
    {
        RecordingQQBotService qqBot = new();
        MessageSender sender = new(qqBot, new MemoryCache(new MemoryCacheOptions()), NullLogger<MessageSender>.Instance);
        GroupAtMessage message = new()
        {
            Id = "MSG3", GroupOpenId = "G1", Content = "查物品 1",
            Author = new Author { MemberOpenId = "M1" }, SendProactive = true
        };

        await sender.SendAsync(message, BotReply.FromText("主动推送"));

        SentCall call = Assert.Single(qqBot.Calls);
        Assert.Equal("group-text", call.Kind);
        Assert.Null(call.MsgId);
        Assert.Null(call.MsgSeq);
        Assert.Null(call.EventId);
    }

    private sealed class FakeDedup : IEventDeduplicationStore
    {
        public HashSet<string> Seen { get; } = [];
        public List<string> Forgotten { get; } = [];
        public int MarkedCount { get; private set; }

        public Task<bool> TryMarkAsync(string eventId, int msgSeq = 0, CancellationToken cancellationToken = default)
            => Task.FromResult(Seen.Add($"{eventId}:{msgSeq}"));

        public Task MarkProcessedAsync(string eventId, int msgSeq = 0, CancellationToken cancellationToken = default)
        {
            MarkedCount++;
            return Task.CompletedTask;
        }

        public Task ForgetAsync(string eventId, int msgSeq = 0, CancellationToken cancellationToken = default)
        {
            Seen.Remove($"{eventId}:{msgSeq}");
            Forgotten.Add(eventId);
            return Task.CompletedTask;
        }

        public Task<int> PurgeAsync(TimeSpan retention, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class FakeAck : IInteractionAckService
    {
        public List<string> InteractionIds { get; } = [];

        public Task<bool> AckAsync(string interactionId, int code = 0, CancellationToken cancellationToken = default)
        {
            InteractionIds.Add(interactionId);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeUserDirectory : IUserDirectory
    {
        public int Calls { get; private set; }

        public Task<BotUserPrincipal> ResolveAsync(ExternalIdentity identity, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new BotUserPrincipal(42, "U000042", "", [BotRole.Player], false));
        }

        public Task<IdentityLinkStatus> LinkAsync(long userId, ExternalIdentity identity, CancellationToken cancellationToken = default)
            => Task.FromResult(IdentityLinkStatus.Linked);
    }

    private sealed class FakeSink : IBotMessageSink
    {
        public List<IBotMessage> Messages { get; } = [];
        public bool Throw { get; set; }

        public ValueTask QueueAsync(IBotMessage message, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new InvalidOperationException("投递失败");
            Messages.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    internal readonly record struct SentCall(string Kind, string OpenId, string? MsgId, int? MsgSeq, string? EventId);

    private sealed class RecordingQQBotService : IQQBotService
    {
        public List<SentCall> Calls { get; } = [];

        public Task SendC2CMessageAsync(string openid, string content, int msgType = 0, object? media = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null)
        {
            Calls.Add(new SentCall("c2c-text", openid, msgId, msgSeq, eventId));
            return Task.CompletedTask;
        }

        public Task SendGroupMessageAsync(string groupOpenid, string content, int msgType = 0, object? media = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null)
        {
            Calls.Add(new SentCall("group-text", groupOpenid, msgId, msgSeq, eventId));
            return Task.CompletedTask;
        }

        public Task SendC2CMarkdownAsync(string openid, MarkdownMessage markdown, KeyboardMessage? keyboard = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null)
        {
            Calls.Add(new SentCall("c2c-md", openid, msgId, msgSeq, eventId));
            return Task.CompletedTask;
        }

        public Task SendGroupMarkdownAsync(string groupOpenid, MarkdownMessage markdown, KeyboardMessage? keyboard = null, string? msgId = null, int? msgSeq = null, CancellationToken cancellationToken = default, string? eventId = null)
        {
            Calls.Add(new SentCall("group-md", groupOpenid, msgId, msgSeq, eventId));
            return Task.CompletedTask;
        }

        public Task<UploadMediaResult> UploadC2CMediaAsync(string openid, int fileType, string url, CancellationToken cancellationToken = default)
            => Task.FromResult(new UploadMediaResult());

        public Task<UploadMediaResult> UploadGroupMediaAsync(string groupOpenid, int fileType, string url, CancellationToken cancellationToken = default)
            => Task.FromResult(new UploadMediaResult());

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("token");
        public Task<string> RefreshTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult("token");
        public Task<bool> AckInteractionAsync(string interactionId, int code = 0, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
