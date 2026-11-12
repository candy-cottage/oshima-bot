using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 私信（旧版 <c>user_mails</c> 表一直在、但没有收发入口）。
/// <para>重点是「内容过校验」与「频次上限」——私信是最容易被用来引流和骚扰的入口。</para>
/// </summary>
public sealed class MailServiceTests
{
    private const long Sender = 42;
    private const long Receiver = 77;

    [Fact]
    public async Task Send_ThenInboxAndRead()
    {
        using MailDatabase db = new();
        IMailService mail = db.Get<IMailService>();
        await db.CreatePlayerAsync(Sender, "寄信人");
        await db.CreatePlayerAsync(Receiver, "收信人");

        ActionResult sent = await mail.SendAsync(Sender, "收信人", "周末一起打共斗吗");
        Assert.True(sent.Ok, sent.Title);

        IReadOnlyList<MailEntry> inbox = await mail.InboxAsync(Receiver);
        MailEntry entry = Assert.Single(inbox);
        Assert.Equal("寄信人", entry.From);
        Assert.False(entry.Read);
        Assert.Contains("共斗", entry.Preview);

        // 未读筛选
        Assert.Single(await mail.InboxAsync(Receiver, unreadOnly: true));

        Assert.True((await mail.ReadAsync(Receiver, 1)).Ok);
        Assert.Empty(await mail.InboxAsync(Receiver, unreadOnly: true));
        Assert.True((await mail.InboxAsync(Receiver))[0].Read);
    }

    [Fact]
    public async Task Send_RejectsBadTargetsAndContent()
    {
        using MailDatabase db = new();
        IMailService mail = db.Get<IMailService>();
        await db.CreatePlayerAsync(Sender, "寄信人");

        Assert.Contains("找不到", (await mail.SendAsync(Sender, "查无此人", "在吗")).Title);
        Assert.Contains("自己", (await mail.SendAsync(Sender, Sender.ToString(), "给自己")).Title);
        Assert.False((await mail.SendAsync(Sender, Sender.ToString(), "加群 12345678 领福利")).Ok);
        Assert.False((await mail.SendAsync(Sender, Sender.ToString(), "https://example.com")).Ok);
        Assert.Empty(await mail.InboxAsync(Sender));
    }

    [Fact]
    public async Task Send_StopsAtDailyLimit()
    {
        using MailDatabase db = new();
        IMailService mail = db.Get<IMailService>();
        await db.CreatePlayerAsync(Sender, "寄信人");
        await db.CreatePlayerAsync(Receiver, "收信人");

        for (int i = 0; i < MailService.DailySendLimit; i++)
        {
            Assert.True((await mail.SendAsync(Sender, "收信人", $"第 {i + 1} 封")).Ok);
        }
        ActionResult blocked = await mail.SendAsync(Sender, "收信人", "超额");
        Assert.False(blocked.Ok);
        Assert.Contains("上限", blocked.Title);
        Assert.Equal(MailService.DailySendLimit, (await mail.InboxAsync(Receiver)).Count);

        // 跨日（商店日 4:00 之后）重置
        db.Clock.Advance(TimeSpan.FromHours(25));
        Assert.True((await mail.SendAsync(Sender, "收信人", "第二天")).Ok);
    }

    [Fact]
    public async Task Read_RejectsOutOfRangeIndex()
    {
        using MailDatabase db = new();
        IMailService mail = db.Get<IMailService>();
        await db.CreatePlayerAsync(Receiver, "收信人");

        ActionResult empty = await mail.ReadAsync(Receiver, 1);
        Assert.False(empty.Ok);
        Assert.Contains("收件箱没有", empty.Title);
    }

    private sealed class MailDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }
        public FakeClock Clock { get; }

        public MailDatabase()
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            // 固定在中午，避开商店日 4:00 的边界。
            Clock = new FakeClock(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<ApiKeyAuthenticator>();
            services.AddGameplayRuntime(Clock);
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IMailService, MailService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task CreatePlayerAsync(long userId, string nickname)
        {
            await Get<IPlayerService>().GetSnapshotAsync(userId);
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            context.Users.Add(new User { Id = userId, Handle = $"handle{userId}", Nickname = nickname });
            await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
