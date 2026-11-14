using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Messaging;
using OshimaBot.Models;
using OshimaBot.Security;
using OshimaBot.Services;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 事件幂等（`processed_events`）。
/// <para>功能早在 S3 就实现了（<see cref="EventDeduplicationStore"/>，接在
/// <see cref="QQEventDispatcher"/> 的入口），但一直没有测试——这轮补上。
/// 要证明的是：同一个事件重复投递**只处理一次**，处理失败要把占位撤掉让平台重推能重来。</para>
/// </summary>
public sealed class IdempotencyTests
{
    [Fact]
    public async Task TryMark_AcceptsOnlyTheFirstArrival()
    {
        using IdempotencyDatabase db = new();
        IEventDeduplicationStore store = db.Get<IEventDeduplicationStore>();

        Assert.True(await store.TryMarkAsync("evt-1"));
        Assert.False(await store.TryMarkAsync("evt-1"));       // 平台重投：第二次拒绝
        Assert.True(await store.TryMarkAsync("evt-2"));        // 不同事件互不影响

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(2, await context.ProcessedEvents.CountAsync());
    }

    [Fact]
    public async Task Forget_AllowsThePlatformToRetry()
    {
        using IdempotencyDatabase db = new();
        IEventDeduplicationStore store = db.Get<IEventDeduplicationStore>();

        Assert.True(await store.TryMarkAsync("evt-retry"));
        Assert.False(await store.TryMarkAsync("evt-retry"));

        // 分发异常时占用被撤销，平台重推必须能重新处理
        await store.ForgetAsync("evt-retry");
        Assert.True(await store.TryMarkAsync("evt-retry"));
    }

    [Fact]
    public async Task Purge_OnlyDropsExpiredRows()
    {
        using IdempotencyDatabase db = new();
        IEventDeduplicationStore store = db.Get<IEventDeduplicationStore>();

        Assert.True(await store.TryMarkAsync("evt-old"));
        Assert.True(await store.TryMarkAsync("evt-new"));

        // 把「old」改成两天前写入，模拟历史记录
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            ProcessedEvent row = await context.ProcessedEvents.SingleAsync(x => x.EventId == "evt-old");
            row.ReceivedAtUtc = DateTime.UtcNow.AddHours(-48);
            await context.SaveChangesAsync();
        }

        int removed = await store.PurgeAsync(TimeSpan.FromHours(24));
        Assert.Equal(1, removed);

        using IServiceScope verify = db.Provider.CreateScope();
        BotDbContext after = verify.ServiceProvider.GetRequiredService<BotDbContext>();
        List<string> left = await after.ProcessedEvents.Select(x => x.EventId).ToListAsync();
        Assert.Equal(["evt-new"], left);
    }

    /// <summary>
    /// 端到端：同一份 QQ Payload 投两次，第二次必须是 Duplicate，且消息只进队一次。
    /// 这条才是「重投不会抽两次卡」的真正保证。
    /// </summary>
    [Fact]
    public async Task Dispatcher_DeduplicatesTheSamePayload()
    {
        using IdempotencyDatabase db = new();
        CountingSink sink = new();
        QQEventDispatcher dispatcher = new(
            db.Get<IEventDeduplicationStore>(), new FakeAck(), sink,
            new FakeDirectory(), NullLogger<QQEventDispatcher>.Instance);

        Payload payload = GroupMessagePayload("evt-qq-1");

        EventDispatchResult first = await dispatcher.DispatchAsync(payload, QQEventSource.WebSocket);
        Assert.Equal(EventDispatchStatus.Accepted, first.Status);
        Assert.Equal(1, sink.Count);

        EventDispatchResult second = await dispatcher.DispatchAsync(payload, QQEventSource.WebSocket);
        Assert.Equal(EventDispatchStatus.Duplicate, second.Status);
        Assert.Equal(1, sink.Count);                            // 没有第二次入队

        // 换一条新消息（新 event id）必须正常处理
        EventDispatchResult third = await dispatcher.DispatchAsync(GroupMessagePayload("evt-qq-2"), QQEventSource.WebSocket);
        Assert.Equal(EventDispatchStatus.Accepted, third.Status);
        Assert.Equal(2, sink.Count);
    }

    /// <summary>Ping 之类的非 Dispatch payload 不进幂等表，也不该被当成消息。</summary>
    [Fact]
    public async Task Dispatcher_IgnoresNonDispatchPayloads()
    {
        using IdempotencyDatabase db = new();
        CountingSink sink = new();
        QQEventDispatcher dispatcher = new(
            db.Get<IEventDeduplicationStore>(), new FakeAck(), sink,
            new FakeDirectory(), NullLogger<QQEventDispatcher>.Instance);

        Payload ping = GroupMessagePayload("evt-ping");
        ping.Op = 10;
        EventDispatchResult result = await dispatcher.DispatchAsync(ping, QQEventSource.WebSocket);

        Assert.Equal(EventDispatchStatus.Ignored, result.Status);
        Assert.Equal(0, sink.Count);

        using IServiceScope scope = db.Provider.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<BotDbContext>()
            .ProcessedEvents.CountAsync());
    }

    // ---------------- 构造 ----------------

    private static Payload GroupMessagePayload(string eventId) => new()
    {
        Id = eventId,
        Op = 0,
        EventType = "GROUP_AT_MESSAGE_CREATE",
        Data = JsonSerializer.SerializeToElement(new
        {
            id = $"msg-{eventId}",
            content = " /状态",
            timestamp = "2026-11-14T22:00:00+08:00",
            group_openid = "GROUP-1",
            author = new { member_openid = "MEMBER-1" }
        })
    };

    private sealed class CountingSink : IBotMessageSink
    {
        public int Count { get; private set; }
        public ValueTask QueueAsync(IBotMessage message, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAck : IInteractionAckService
    {
        public Task<bool> AckAsync(string interactionId, int code = 0, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FakeDirectory : IUserDirectory
    {
        public Task<BotUserPrincipal> ResolveAsync(ExternalIdentity identity, CancellationToken cancellationToken = default)
            => Task.FromResult(new BotUserPrincipal(9001, "handle", "昵称", [BotRole.Player], false));

        public Task<IdentityLinkStatus> LinkAsync(long userId, ExternalIdentity identity,
            CancellationToken cancellationToken = default)
            => Task.FromResult(IdentityLinkStatus.Linked);
    }

    private sealed class IdempotencyDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public IdempotencyDatabase()
        {
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();

            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IEventDeduplicationStore, EventDeduplicationStore>();

            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
