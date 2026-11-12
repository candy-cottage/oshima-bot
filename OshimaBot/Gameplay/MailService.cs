using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>收件箱的一行。</summary>
public sealed record MailEntry(
    int Index, long Id, string From, string Subject, string Preview, bool Read, string SentOn);

/// <summary>
/// 玩家私信（旧版 <c>user_mails</c> 表一直在、但没有收发入口）。
/// <para>内容要过 <see cref="NamingPolicy"/> 的内容校验：私信是最容易被用来站外引流与骚扰的入口，
/// 上线后还需接举报/拉黑——这里先把「能判的部分」和频次上限做掉。</para>
/// </summary>
public interface IMailService
{
    Task<ActionResult> SendAsync(long userId, string target, string content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MailEntry>> InboxAsync(long userId, bool unreadOnly = false, CancellationToken cancellationToken = default);
    Task<ActionResult> ReadAsync(long userId, int index, CancellationToken cancellationToken = default);
}

public sealed class MailService(
    IServiceScopeFactory scopeFactory,
    ILogger<MailService> logger,
    IGameClock clock) : IMailService
{
    /// <summary>每人每天最多发多少条，防止刷屏骚扰。</summary>
    public const int DailySendLimit = 20;

    public const int SubjectLength = 20;

    public Task<ActionResult> SendAsync(long userId, string target, string content,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (bool contentOk, string contentWhy) = NamingPolicy.CheckMessage(content);
            if (!contentOk)
            {
                return ActionResult.Fail("私信内容未通过校验", "没有发送任何私信", contentWhy, "/私信");
            }
            if (string.Equals(target?.Trim(), userId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return ActionResult.Fail("不能给自己发私信", "没有发送任何私信", "换一个收件人", "/收件箱");
            }

            (long ReceiverId, string Display)? found = await FindUserAsync(db, target, ct);
            if (found is null)
            {
                return ActionResult.Fail("找不到这个玩家", "没有发送任何私信",
                    $"用 UID / Handle / 昵称指定；你输入的是「{target}」", "/收件箱");
            }
            (long receiverId, string display) = found.Value;

            int sentToday = await db.MailMessages.CountAsync(x =>
                x.SenderUserId == userId && x.CreatedAtUtc >= GameplayRules.StoreDayStartUtc(clock.UtcNow), ct);
            if (sentToday >= DailySendLimit)
            {
                return ActionResult.Fail("今日私信已达上限", "没有发送任何私信",
                    $"每天最多 {DailySendLimit} 条，明天再来", "/收件箱");
            }

            string body = content.Trim();
            db.MailMessages.Add(new MailMessage
            {
                SenderUserId = userId,
                ReceiverUserId = receiverId,
                Subject = body.Length <= SubjectLength ? body : body[..SubjectLength] + "…",
                Content = body,
                CreatedAtUtc = clock.UtcNow
            });

            return ActionResult.Success("✉️ 私信已送出", $"→ {display}",
                [
                    $"- 内容：{body}",
                    $"- 今日已发 {sentToday + 1}/{DailySendLimit} 条",
                    "- 对方用 /收件箱 可以看到"
                ], ["/收件箱", "/我的社团"]);
        }, cancellationToken);

    public Task<IReadOnlyList<MailEntry>> InboxAsync(long userId, bool unreadOnly = false,
        CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<MailEntry>>(async (db, ct) =>
        {
            List<MailMessage> rows = await db.MailMessages.AsNoTracking()
                .Where(x => x.ReceiverUserId == userId && (!unreadOnly || x.ReadAtUtc == null))
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
                .Take(50).ToListAsync(ct);
            if (rows.Count == 0) return [];

            List<long> senders = [.. rows.Where(x => x.SenderUserId is not null)
                .Select(x => x.SenderUserId!.Value).Distinct()];
            Dictionary<long, string> names = senders.Count == 0
                ? []
                : await db.Users.AsNoTracking().Where(x => senders.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => Display(x), ct);

            return [.. rows.Select((row, i) => new MailEntry(
                i + 1, row.Id,
                row.SenderUserId is null
                    ? "系统"
                    : names.GetValueOrDefault(row.SenderUserId.Value, $"旅人{row.SenderUserId}"),
                row.Subject, Preview(row.Content), row.ReadAtUtc is not null,
                row.CreatedAtUtc.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)))];
        }, cancellationToken);

    public Task<ActionResult> ReadAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            List<MailMessage> rows = await db.MailMessages
                .Where(x => x.ReceiverUserId == userId)
                .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
                .Take(50).ToListAsync(ct);
            if (index <= 0 || index > rows.Count)
            {
                return ActionResult.Fail($"收件箱没有第 {index} 封", "没有读出任何私信",
                    rows.Count == 0 ? "收件箱是空的" : $"当前 {rows.Count} 封，用 /收件箱 查看", "/收件箱");
            }

            MailMessage mail = rows[index - 1];
            string from = mail.SenderUserId is null
                ? "系统"
                : await DisplayOfAsync(db, mail.SenderUserId.Value, ct);
            mail.ReadAtUtc ??= clock.UtcNow;

            return ActionResult.Success($"📨 {mail.Subject}", $"来自 {from}",
                [mail.Content, "", $"- 收到于 {mail.CreatedAtUtc:yyyy-MM-dd HH:mm}"],
                ["/收件箱", "/私信"]);
        }, cancellationToken);

    // ---------------- 内部 ----------------

    private async Task<T> ReadAsync<T>(Func<BotDbContext, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return await body(db, ct);
    }

    private async Task<ActionResult> MutateAsync(
        long userId, Func<BotDbContext, CancellationToken, Task<ActionResult>> body, CancellationToken ct)
    {
        if (userId <= 0) return PlayerService.NoIdentity;
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(ct);

            ActionResult result = await body(db, ct);
            if (!result.Ok)
            {
                await transaction.RollbackAsync(ct);
                return result;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "私信操作失败：UserId={UserId}", userId);
            return ActionResult.Fail("私信操作出现异常", "本次操作已回滚", "稍后重试", "/收件箱");
        }
    }

    private static string Preview(string content)
        => content.Length <= 24 ? content : content[..24] + "…";

    private static string Display(User user)
        => !string.IsNullOrWhiteSpace(user.Nickname) ? user.Nickname
            : !string.IsNullOrWhiteSpace(user.Handle) ? user.Handle
            : $"旅人{user.Id}";

    private static async Task<string> DisplayOfAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        User? user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        return user is null ? $"旅人{userId}" : Display(user);
    }

    private static async Task<(long UserId, string Display)?> FindUserAsync(
        BotDbContext db, string? target, CancellationToken ct)
    {
        string key = (target ?? "").Trim().TrimStart('@').Trim();
        if (key.Length == 0) return null;
        if (long.TryParse(key, out long id) && id > 0)
        {
            User? byId = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (byId is not null) return (byId.Id, Display(byId));
            if (await db.CorePlayers.AsNoTracking().AnyAsync(x => x.UserId == id, ct)) return (id, $"旅人{id}");
        }
        User? byName = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Handle == key || x.Nickname == key, ct);
        return byName is null ? null : (byName.Id, Display(byName));
    }
}
