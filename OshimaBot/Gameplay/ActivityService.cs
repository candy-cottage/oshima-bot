using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>活动列表的一行。</summary>
public sealed record ActivityEntry(
    int Index, string Name, string Phase, string Window, int DoneQuests, int TotalQuests, bool AllClaimed);

/// <summary>活动详情里的一个任务。</summary>
public sealed record ActivityQuestView(
    int Index, string Name, string Description, int Progress, int Target,
    string Cost, string Reward, bool Claimed)
{
    public bool Ready => Progress >= Target && !Claimed;
}

public sealed record ActivityDetail(
    int Index, string Name, string Description, string Phase, string Window,
    IReadOnlyList<ActivityQuestView> Quests)
{
    public bool AnyReady => Quests.Any(x => x.Ready);
}

/// <summary>
/// 活动（旧版 <c>GetEvents</c> / <c>PerformEvent</c> / <c>ClaimEventPrize</c>）与运营公告。
/// <para>进度落在既有的 <c>ActivityProgress</c> 表里（<c>ActivityKey = activity:{key}</c>），
/// 不再新建表；<c>ProgressJson</c> 存各任务做过的次数，<c>ClaimedJson</c> 存已领奖的任务 key。</para>
/// </summary>
public interface IActivityService
{
    Task<IReadOnlyList<ActivityEntry>> ListAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActivityDetail?> InfoAsync(long userId, int index, CancellationToken cancellationToken = default);

    /// <param name="itemIndex">成本是物品时的背包编号；其它成本类型忽略。</param>
    Task<ActionResult> PerformAsync(
        long userId, int index, int questIndex, int itemIndex, CancellationToken cancellationToken = default);

    Task<ActionResult> ClaimAsync(long userId, int index, int questIndex, CancellationToken cancellationToken = default);

    /// <summary>写入运营公告（管理员指令调用，内容先过 NamingPolicy）。</summary>
    Task<ActionResult> SetNoticeAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class ActivityService(
    IServiceScopeFactory scopeFactory,
    ILogger<ActivityService> logger,
    IGameClock clock) : IActivityService
{
    private const string NoticeKey = "notice";

    public Task<IReadOnlyList<ActivityEntry>> ListAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<ActivityEntry>>(async (db, ct) =>
        {
            Dictionary<string, ActivityState> states = await LoadStatesAsync(db, userId, ct);
            return [.. ActivityCatalog.Activities.Select((spec, i) =>
            {
                (DateTime? start, DateTime? end) = ActivityCatalog.Window(spec.Window, clock.UtcNow);
                ActivityPhase phase = ActivityCatalog.Phase((start, end), clock.UtcNow);
                (Dictionary<string, int> progress, HashSet<string> claimed) =
                    states.TryGetValue(spec.Key, out ActivityState? state)
                        ? (state.Progress, state.Claimed)
                        : ([], []);
                int done = spec.Quests.Count(q => progress.GetValueOrDefault(q.Key) >= q.Target);
                return new ActivityEntry(i + 1, spec.Name, ActivityCatalog.PhaseName(phase),
                    WindowText(start, end), done, spec.Quests.Count,
                    spec.Quests.All(q => claimed.Contains(q.Key)));
            })];
        }, cancellationToken);

    public Task<ActivityDetail?> InfoAsync(long userId, int index, CancellationToken cancellationToken = default)
        => ReadAsync<ActivityDetail?>(async (db, ct) =>
        {
            ActivitySpec? spec = ActivityCatalog.ByIndex(index);
            if (spec is null) return null;

            (DateTime? start, DateTime? end) = ActivityCatalog.Window(spec.Window, clock.UtcNow);
            ActivityPhase phase = ActivityCatalog.Phase((start, end), clock.UtcNow);
            Dictionary<string, ActivityState> states = await LoadStatesAsync(db, userId, ct);
            (Dictionary<string, int> progress, HashSet<string> claimed) =
                states.TryGetValue(spec.Key, out ActivityState? state) ? (state.Progress, state.Claimed) : ([], []);

            return new ActivityDetail(index, spec.Name, spec.Description,
                ActivityCatalog.PhaseName(phase), WindowText(start, end),
                [.. spec.Quests.Select((quest, i) => new ActivityQuestView(
                    i + 1, quest.Name, quest.Description,
                    Math.Min(progress.GetValueOrDefault(quest.Key), quest.Target), quest.Target,
                    ActivityCatalog.CostText(quest), ActivityCatalog.RewardText(quest),
                    claimed.Contains(quest.Key)))]);
        }, cancellationToken);

    public Task<ActionResult> PerformAsync(long userId, int index, int questIndex, int itemIndex,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            ActivitySpec? spec = ActivityCatalog.ByIndex(index);
            if (spec is null) return NoActivity(index);
            if (questIndex <= 0 || questIndex > spec.Quests.Count)
            {
                return ActionResult.Fail($"任务 @{questIndex} 不存在", "没有扣除任何资源",
                    $"[ {spec.Name} ] 有 {spec.Quests.Count} 个任务，用 /查活动 {index} 查看", "/活动");
            }
            ActivityQuestSpec quest = spec.Quests[questIndex - 1];

            (DateTime? start, DateTime? end) = ActivityCatalog.Window(spec.Window, clock.UtcNow);
            ActivityPhase phase = ActivityCatalog.Phase((start, end), clock.UtcNow);
            if (phase != ActivityPhase.InProgress)
            {
                return ActionResult.Fail($"[ {spec.Name} ] 现在不能做任务", "没有扣除任何资源",
                    $"{ActivityCatalog.PhaseName(phase)}　{WindowText(start, end)}", "/活动");
            }

            (Dictionary<string, int> progress, HashSet<string> claimed) = await LoadStateAsync(db, spec.Key, userId, ct);
            if (claimed.Contains(quest.Key))
            {
                return ActionResult.Fail("这个任务已经领过奖了", "没有扣除任何资源",
                    "同一个任务只能领一次", $"/查活动 {index}");
            }
            int done = progress.GetValueOrDefault(quest.Key);
            if (done >= quest.Target)
            {
                return ActionResult.Fail("这个任务已经做满", "没有扣除任何资源",
                    $"用 /领取奖励 {index} {questIndex} 领取", $"/查活动 {index}");
            }

            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            string reference = spec.Key;
            string? itemName = null;
            switch (quest.CostKind)
            {
                case ActivityCostKind.Credits:
                    if (account.Credits < quest.CostAmount)
                    {
                        return ActionResult.Fail("金币不足", "没有扣除任何资源",
                            $"需要 {quest.CostAmount}，当前 {account.Credits:0.##}", "/状态");
                    }
                    PlayerService.ApplyCurrency(db, account, -quest.CostAmount, "credits", "activity_cost", "activity", reference);
                    break;
                case ActivityCostKind.Diamonds:
                    if (account.Materials < quest.CostAmount)
                    {
                        return ActionResult.Fail("钻石不足", "没有扣除任何资源",
                            $"需要 {quest.CostAmount}，当前 {account.Materials:0.##}", "/状态");
                    }
                    PlayerService.ApplyCurrency(db, account, -quest.CostAmount, "diamonds", "activity_cost", "activity", reference);
                    break;
                default:
                {
                    PlayerItem? item = await PlayerService.ItemByIndexAsync(db, userId, itemIndex, ct);
                    if (item is null)
                    {
                        return ActionResult.Fail("没有指定要上交的物品", "没有扣除任何资源",
                            $"用法：/做活动任务 {index} {questIndex} @物品编号（用 /背包 查编号）", "/背包");
                    }
                    if (item.Locked || item.Bound)
                    {
                        return ActionResult.Fail("该物品已锁定或绑定", "没有扣除任何资源",
                            "先 /解锁 @编号", "/背包");
                    }
                    if (item.Quantity < quest.CostAmount)
                    {
                        return ActionResult.Fail("物品数量不足", "没有扣除任何资源",
                            $"需要 {quest.CostAmount} 件，这一堆只有 {item.Quantity} 件", "/背包");
                    }
                    itemName = PlayerService.ReadName(item);
                    PlayerService.Consume(db, item, (int)quest.CostAmount);
                    break;
                }
            }

            account.UpdatedAtUtc = clock.UtcNow;
            progress[quest.Key] = done + 1;
            await SaveStateAsync(db, spec.Key, userId, progress, claimed, ct);

            int now = progress[quest.Key];
            return ActionResult.Success("📌 已提交", $"{quest.Name}　{now}/{quest.Target}",
                [
                    $"- 本次消耗：{(itemName is null ? ActivityCatalog.CostText(quest) : $"{itemName} ×{quest.CostAmount}")}",
                    now >= quest.Target
                        ? $"- 任务已做满，用 /领取奖励 {index} {questIndex} 领取：{ActivityCatalog.RewardText(quest)}"
                        : $"- 进度 {now}/{quest.Target}，还能再做 {quest.Target - now} 次",
                    $"- 当前金币 {account.Credits:0.##}　钻石 {account.Materials:0.##}"
                ], [$"/查活动 {index}", $"/领取奖励 {index} {questIndex}", "/活动"]);
        }, cancellationToken);

    public Task<ActionResult> ClaimAsync(long userId, int index, int questIndex, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            ActivitySpec? spec = ActivityCatalog.ByIndex(index);
            if (spec is null) return NoActivity(index);
            if (questIndex <= 0 || questIndex > spec.Quests.Count)
            {
                return ActionResult.Fail($"任务 @{questIndex} 不存在", "没有发放任何奖励",
                    $"用 /查活动 {index} 查看任务编号", "/活动");
            }
            ActivityQuestSpec quest = spec.Quests[questIndex - 1];

            (Dictionary<string, int> progress, HashSet<string> claimed) = await LoadStateAsync(db, spec.Key, userId, ct);
            if (claimed.Contains(quest.Key))
            {
                return ActionResult.Fail("这个任务的奖励已经领过了", "没有重复发放",
                    "同一个任务只能领一次", $"/查活动 {index}");
            }
            int done = progress.GetValueOrDefault(quest.Key);
            if (done < quest.Target)
            {
                return ActionResult.Fail("任务还没做满", "没有发放任何奖励",
                    $"{quest.Name}：{done}/{quest.Target}，每次 {ActivityCatalog.CostText(quest)}", $"/查活动 {index}");
            }

            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            string reference = spec.Key;
            if (quest.RewardCredits > 0)
            {
                PlayerService.ApplyCurrency(db, account, quest.RewardCredits, "credits", "activity_reward", "activity", reference);
            }
            if (quest.RewardDiamonds > 0)
            {
                PlayerService.ApplyCurrency(db, account, quest.RewardDiamonds, "diamonds", "activity_reward", "activity", reference);
            }
            foreach (ActivityRewardItem reward in quest.RewardItems)
            {
                await PlayerService.AddItemAsync(db, userId, reward.Name, 1, reward.Count, ct,
                    string.IsNullOrWhiteSpace(reward.CatalogId) ? "activity" : reward.CatalogId);
            }
            account.UpdatedAtUtc = clock.UtcNow;

            claimed.Add(quest.Key);
            await SaveStateAsync(db, spec.Key, userId, progress, claimed, ct);

            return ActionResult.Success("🎁 奖励已领取", $"{spec.Name} · {quest.Name}",
                [
                    $"- 获得：{ActivityCatalog.RewardText(quest)}",
                    $"- 当前金币 {account.Credits:0.##}　钻石 {account.Materials:0.##}",
                    "- 背包里能看到新物品"
                ], ["/背包", "/活动", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> SetNoticeAsync(string text, CancellationToken cancellationToken = default)
        => MutateAsync(0, async (db, ct) =>
        {
            string notice = (text ?? "").Trim();
            (bool ok, string why) = NamingPolicy.CheckNotice(notice);
            if (!ok)
            {
                return ActionResult.Fail("公告未通过内容校验", "没有修改公告", why, "/公告");
            }
            if (notice.Length == 0)
            {
                return ActionResult.Fail("公告不能为空", "没有修改公告", "写点内容再提交", "/公告");
            }

            string json = JsonSerializer.Serialize(new { text = notice });
            AppConfig? config = await db.AppConfigs.FirstOrDefaultAsync(x => x.Key == NoticeKey, ct);
            if (config is null)
            {
                db.AppConfigs.Add(new AppConfig { Key = NoticeKey, ValueJson = json, UpdatedAtUtc = clock.UtcNow });
            }
            else
            {
                config.ValueJson = json;
                config.UpdatedAtUtc = clock.UtcNow;
            }

            return ActionResult.Success("📜 公告已更新", notice.Length > 40 ? notice[..40] + "…" : notice,
                [$"- 共 {notice.Length} 字，全服 /公告 立即生效"], ["/公告", "/活动"]);
        }, cancellationToken);

    // ---------------- 内部 ----------------

    private sealed record ActivityState(Dictionary<string, int> Progress, HashSet<string> Claimed);

    private async Task<T> ReadAsync<T>(Func<BotDbContext, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return await body(db, ct);
    }

    private async Task<ActionResult> MutateAsync(
        long userId, Func<BotDbContext, CancellationToken, Task<ActionResult>> body, CancellationToken ct)
    {
        // 公告写入没有玩家身份（userId = 0），只有需要存档的操作才校验身份。
        if (userId < 0) return PlayerService.NoIdentity;
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
            logger.LogError(exception, "活动操作失败：UserId={UserId}", userId);
            return ActionResult.Fail("活动操作出现异常", "本次操作已回滚", "稍后重试", "/活动");
        }
    }

    private async Task<Dictionary<string, ActivityState>> LoadStatesAsync(
        BotDbContext db, long userId, CancellationToken ct)
    {
        string[] keys = [.. ActivityCatalog.Activities.Select(Prefix)];
        List<ActivityProgress> rows = await db.ActivityProgress.AsNoTracking()
            .Where(x => x.UserId == userId && keys.Contains(x.ActivityKey)).ToListAsync(ct);

        Dictionary<string, ActivityState> states = [];
        foreach (ActivitySpec spec in ActivityCatalog.Activities)
        {
            ActivityProgress? row = rows.FirstOrDefault(x => x.ActivityKey == Prefix(spec));
            states[spec.Key] = row is null
                ? new ActivityState([], [])
                : new ActivityState(ReadProgress(row.ProgressJson), ReadClaimed(row.ClaimedJson));
        }
        return states;
    }

    private async Task<(Dictionary<string, int> Progress, HashSet<string> Claimed)> LoadStateAsync(
        BotDbContext db, string activityKey, long userId, CancellationToken ct)
    {
        string key = Prefix(activityKey);
        ActivityProgress? row = await db.ActivityProgress
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityKey == key, ct);
        return row is null
            ? ([], [])
            : (ReadProgress(row.ProgressJson), ReadClaimed(row.ClaimedJson));
    }

    private async Task SaveStateAsync(
        BotDbContext db, string activityKey, long userId,
        Dictionary<string, int> progress, HashSet<string> claimed, CancellationToken ct)
    {
        string key = Prefix(activityKey);
        string progressJson = JsonSerializer.Serialize(progress);
        string claimedJson = JsonSerializer.Serialize(claimed);
        ActivityProgress? row = await db.ActivityProgress
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityKey == key, ct);
        if (row is null)
        {
            db.ActivityProgress.Add(new ActivityProgress
            {
                UserId = userId, ActivityKey = key, ProgressJson = progressJson,
                ClaimedJson = claimedJson, UpdatedAtUtc = clock.UtcNow
            });
            return;
        }
        row.ProgressJson = progressJson;
        row.ClaimedJson = claimedJson;
        row.UpdatedAtUtc = clock.UtcNow;
    }

    private static string Prefix(ActivitySpec spec) => Prefix(spec.Key);

    private static string Prefix(string activityKey) => $"activity:{activityKey}";

    private static Dictionary<string, int> ReadProgress(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static HashSet<string> ReadClaimed(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string WindowText(DateTime? startUtc, DateTime? endUtc)
    {
        if (startUtc is null && endUtc is null) return "长期开放";
        if (startUtc is { } start && endUtc is { } end)
        {
            return $"{GameplayRules.ToLocal(start):yyyy-MM-dd HH:mm} ~ {GameplayRules.ToLocal(end):yyyy-MM-dd HH:mm}";
        }
        return startUtc is { } only
            ? $"{GameplayRules.ToLocal(only):yyyy-MM-dd HH:mm} 起"
            : $"截止 {GameplayRules.ToLocal(endUtc!.Value):yyyy-MM-dd HH:mm}";
    }

    private static ActionResult NoActivity(int index)
        => ActionResult.Fail($"活动 @{index} 不存在", "没有做任何操作",
            $"当前有 {ActivityCatalog.Activities.Count} 个活动，用 /活动 查看", "/活动");
}
