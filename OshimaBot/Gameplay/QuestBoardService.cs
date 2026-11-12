using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>任务类型（旧版 <c>QuestType</c> 的三类里，v3 先做这三支）。</summary>
public enum QuestBoardKind
{
    /// <summary>持续任务：接取后等时长，到点自动完成，可结算。</summary>
    Continuous,
    /// <summary>即时任务：接取当场执行并发奖（旧版 <c>ExecuteImmediateQuest</c>）。</summary>
    Immediate,
    /// <summary>渐进任务：提交指定物品到目标件数后完成。</summary>
    Progressive
}

/// <param name="CostCredits">接取/执行时要先付的金币（即时任务用）。</param>
/// <param name="ItemName">渐进任务要提交的物品名；空串表示任意物品。</param>
public sealed record QuestBoardSpec(
    string Key, string Name, string Description, QuestBoardKind Kind,
    int DurationMinutes, int Target, long CostCredits,
    long RewardCredits, int RewardDiamonds, string ItemName);

/// <summary>任务板上的一条（含玩家自己的状态）。</summary>
public sealed record QuestBoardEntry(
    int Index, string Name, string TypeName, string Requirement, string Reward,
    string State, int RemainingSeconds, int Progress, int Target)
{
    public bool Running => State == QuestBoardRules.Running;
    public bool Completed => State == QuestBoardRules.Completed;
}

public static class QuestBoardRules
{
    public const string NotStarted = "可接取";
    public const string Running = "进行中";
    public const string Completed = "已完成";
    public const string Settled = "已结算";

    /// <summary>任务板定义（旧版从 quests 配置按用户生成；v3 先给一套常驻任务）。</summary>
    public static readonly IReadOnlyList<QuestBoardSpec> Quests =
    [
        new("patrol", "巡逻铎京", "在铎京街头走一圈，看看有没有异常。",
            QuestBoardKind.Continuous, 30, 0, 0, 800, 1, ""),
        new("escort", "护送商队", "护送商队穿过城外官道，路上不太平。",
            QuestBoardKind.Continuous, 60, 0, 0, 2_000, 3, ""),
        new("clean", "清理下水道", "老城区的下水道堵了，先垫点工钱请人。",
            QuestBoardKind.Immediate, 0, 0, 200, 500, 1, ""),
        new("gather", "物资征调", "协会在收物资，交三件东西就行。",
            QuestBoardKind.Progressive, 0, 3, 0, 1_500, 0, "")
    ];

    public static QuestBoardSpec? ByIndex(int index)
        => index <= 0 || index > Quests.Count ? null : Quests[index - 1];

    public static QuestBoardSpec? Find(string key)
        => Quests.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string KindName(QuestBoardKind kind) => kind switch
    {
        QuestBoardKind.Continuous => "持续",
        QuestBoardKind.Immediate => "即时",
        _ => "渐进"
    };

    public static string Requirement(QuestBoardSpec spec) => spec.Kind switch
    {
        QuestBoardKind.Continuous => $"等待 {spec.DurationMinutes} 分钟",
        QuestBoardKind.Immediate => spec.CostCredits > 0 ? $"先付 {spec.CostCredits} 金币" : "立即执行",
        _ => $"提交 {spec.Target} 件{(spec.ItemName.Length == 0 ? "任意物品" : spec.ItemName)}"
    };

    public static string Reward(QuestBoardSpec spec)
    {
        List<string> parts = [];
        if (spec.RewardCredits > 0) parts.Add($"{spec.RewardCredits} 金币");
        if (spec.RewardDiamonds > 0) parts.Add($"{spec.RewardDiamonds} 钻石");
        return parts.Count == 0 ? "无" : string.Join(" + ", parts);
    }
}

/// <summary>
/// 任务板（旧版 任务列表 / 做任务 / 任务信息 / 任务结算）。
/// <para>状态落在 <c>ActivityProgress</c>（key = <c>quest-board</c>）：一张 JSON 记所有任务的开始时间、
/// 进度与是否已结算，不新建表；非渐进任务同时只能进行一个（同旧版）。</para>
/// </summary>
public interface IQuestBoardService
{
    Task<IReadOnlyList<QuestBoardEntry>> ListAsync(long userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuestBoardEntry>> WorkingAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> AcceptAsync(long userId, int index, int itemIndex, CancellationToken cancellationToken = default);
    Task<ActionResult> SettleAsync(long userId, CancellationToken cancellationToken = default);
}

public sealed class QuestBoardService(
    IServiceScopeFactory scopeFactory,
    ILogger<QuestBoardService> logger,
    IGameClock clock) : IQuestBoardService
{
    private const string StateKey = "quest-board";

    public Task<IReadOnlyList<QuestBoardEntry>> ListAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<QuestBoardEntry>>(async (db, ct) =>
        {
            Dictionary<string, QuestRun> runs = await LoadRunsAsync(db, userId, ct);
            return [.. QuestBoardRules.Quests.Select((spec, i) => View(spec, i + 1, runs))];
        }, cancellationToken);

    public Task<IReadOnlyList<QuestBoardEntry>> WorkingAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<QuestBoardEntry>>(async (db, ct) =>
        {
            Dictionary<string, QuestRun> runs = await LoadRunsAsync(db, userId, ct);
            return [.. QuestBoardRules.Quests
                .Select((spec, i) => View(spec, i + 1, runs))
                .Where(view => view.Running || view.Completed)];
        }, cancellationToken);

    public Task<ActionResult> AcceptAsync(long userId, int index, int itemIndex,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            QuestBoardSpec? spec = QuestBoardRules.ByIndex(index);
            if (spec is null)
            {
                return ActionResult.Fail($"任务 @{index} 不存在", "没有开始任何任务",
                    $"任务板共 {QuestBoardRules.Quests.Count} 个任务，用 /任务列表 查看", "/任务列表");
            }

            Dictionary<string, QuestRun> runs = await LoadRunsAsync(db, userId, ct);
            if (runs.TryGetValue(spec.Key, out QuestRun? existing))
            {
                if (existing.Settled)
                {
                    return ActionResult.Fail($"任务【{spec.Name}】已经结算过了", "没有重复接取",
                        "任务板每个任务只能领一次奖励", "/任务列表");
                }
                if (IsCompleted(spec, existing))
                {
                    return ActionResult.Fail($"任务【{spec.Name}】已经完成", "没有重复接取",
                        "用 /任务结算 领取奖励", "/任务结算");
                }
                if (spec.Kind == QuestBoardKind.Progressive)
                {
                    return await ContributeAsync(db, userId, spec, existing, itemIndex, ct);
                }
                return ActionResult.Fail($"任务【{spec.Name}】正在进行中", "没有重复接取",
                    $"还需 {Remaining(spec, existing)} 秒，完成后 /任务结算", "/任务信息");
            }

            // 非渐进任务同时只能进行一个（旧版 workingQuests 检查）。
            string? busy = null;
            if (spec.Kind != QuestBoardKind.Progressive)
            {
                foreach ((string key, QuestRun other) in runs)
                {
                    QuestBoardSpec? running = QuestBoardRules.Find(key);
                    if (running is null || running.Kind == QuestBoardKind.Progressive) continue;
                    if (other.Settled || IsCompleted(running, other)) continue;
                    busy = running.Name;
                    break;
                }
                if (busy is not null)
                {
                    return ActionResult.Fail($"你正在进行任务【{busy}】", "没有开始新任务",
                        "非渐进任务同时只能进行一个，先用 /任务信息 看看进度", "/任务信息");
                }
            }

            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);

            if (spec.Kind == QuestBoardKind.Immediate)
            {
                // 即时任务：当场执行，直接发奖并标记已结算（旧版 ExecuteImmediateQuest + SettleActivityQuest）。
                if (account.Credits < spec.CostCredits)
                {
                    return ActionResult.Fail("金币不足", "没有执行这个任务",
                        $"需要先付 {spec.CostCredits}，当前 {account.Credits:0.##}", "/状态");
                }
                PlayerService.ApplyCurrency(db, account, -spec.CostCredits, "credits", "quest_cost", "quest", spec.Key);
                PlayerService.ApplyCurrency(db, account, spec.RewardCredits, "credits", "quest_reward", "quest", spec.Key);
                if (spec.RewardDiamonds > 0)
                {
                    PlayerService.ApplyCurrency(db, account, spec.RewardDiamonds, "diamonds", "quest_reward", "quest", spec.Key);
                }
                account.UpdatedAtUtc = clock.UtcNow;
                runs[spec.Key] = new QuestRun(clock.UtcNow, 0, Settled: true);
                await SaveRunsAsync(db, userId, runs, ct);

                return ActionResult.Success("✅ 任务完成", $"【{spec.Name}】",
                    [
                        $"- 净收益：{(spec.RewardCredits - spec.CostCredits):0.##} 金币"
                            + (spec.RewardDiamonds > 0 ? $" + {spec.RewardDiamonds} 钻石" : ""),
                        $"- 当前金币 {account.Credits:0.##}　钻石 {account.Materials:0.##}"
                    ], ["/任务列表", "/状态"]);
            }

            if (spec.Kind == QuestBoardKind.Progressive)
            {
                return await ContributeAsync(db, userId, spec, new QuestRun(clock.UtcNow, 0, Settled: false), itemIndex, ct,
                    persistStart: true);
            }

            runs[spec.Key] = new QuestRun(clock.UtcNow, 0, Settled: false);
            await SaveRunsAsync(db, userId, runs, ct);
            return ActionResult.Success("📋 已接取任务", $"【{spec.Name}】",
                [
                    $"- 要求：{QuestBoardRules.Requirement(spec)}",
                    $"- 奖励：{QuestBoardRules.Reward(spec)}",
                    $"- 预计 {(clock.UtcNow.AddMinutes(spec.DurationMinutes) - clock.UtcNow).TotalMinutes:0} 分钟后完成，完成后 /任务结算"
                ], ["/任务信息", "/任务结算", "/任务列表"]);
        }, cancellationToken);

    public Task<ActionResult> SettleAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            Dictionary<string, QuestRun> runs = await LoadRunsAsync(db, userId, ct);
            List<QuestBoardSpec> ready = [];
            List<string> pending = [];
            foreach ((string key, QuestRun run) in runs)
            {
                QuestBoardSpec? spec = QuestBoardRules.Find(key);
                if (spec is null || run.Settled) continue;
                if (IsCompleted(spec, run)) ready.Add(spec);
                else pending.Add($"【{spec.Name}】{(spec.Kind == QuestBoardKind.Progressive ? $"{run.Progress}/{spec.Target} 件" : $"还需 {Remaining(spec, run)} 秒")}");
            }

            if (ready.Count == 0)
            {
                return ActionResult.Fail(pending.Count == 0 ? "没有进行中的任务" : "任务还没完成",
                    "没有结算任何奖励",
                    pending.Count == 0 ? "用 /任务列表 接一个任务" : string.Join("；", pending),
                    pending.Count == 0 ? "/任务列表" : "/任务信息");
            }

            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            List<string> lines = [];
            foreach (QuestBoardSpec spec in ready)
            {
                if (spec.RewardCredits > 0)
                {
                    PlayerService.ApplyCurrency(db, account, spec.RewardCredits, "credits", "quest_reward", "quest", spec.Key);
                }
                if (spec.RewardDiamonds > 0)
                {
                    PlayerService.ApplyCurrency(db, account, spec.RewardDiamonds, "diamonds", "quest_reward", "quest", spec.Key);
                }
                QuestRun run = runs[spec.Key];
                runs[spec.Key] = run with { Settled = true };
                lines.Add($"- 【{spec.Name}】→ {QuestBoardRules.Reward(spec)}");
            }
            account.UpdatedAtUtc = clock.UtcNow;
            await SaveRunsAsync(db, userId, runs, ct);

            if (pending.Count > 0) lines.Add($"- 还有未完成：{string.Join("；", pending)}");
            lines.Add($"- 当前金币 {account.Credits:0.##}　钻石 {account.Materials:0.##}");

            return ActionResult.Success("💰 任务结算", $"{ready.Count} 个任务已结算", lines,
                ["/任务列表", "/任务信息", "/状态"]);
        }, cancellationToken);

    // ---------------- 内部 ----------------

    /// <summary>渐进任务：提交物品推进进度（一次提交一件，够数即完成）。</summary>
    private async Task<ActionResult> ContributeAsync(
        BotDbContext db, long userId, QuestBoardSpec spec, QuestRun run, int itemIndex, CancellationToken ct,
        bool persistStart = false)
    {
        if (itemIndex <= 0)
        {
            return ActionResult.Fail("没有指定要提交的物品", "没有提交任何物品",
                $"用法：/做任务 {Index(spec)} @物品编号（用 /背包 查编号）", "/背包");
        }
        PlayerItem? item = await PlayerService.ItemByIndexAsync(db, userId, itemIndex, ct);
        if (item is null)
        {
            return ActionResult.Fail($"背包里没有 @{itemIndex}", "没有提交任何物品",
                "用 /背包 查看编号", "/背包");
        }
        if (item.Locked || item.Bound)
        {
            return ActionResult.Fail("该物品已锁定或绑定", "没有提交任何物品",
                "先 /解锁 @编号", "/背包");
        }
        if (spec.ItemName.Length > 0 && !string.Equals(item.Name, spec.ItemName, StringComparison.Ordinal))
        {
            return ActionResult.Fail($"这个任务要的是【{spec.ItemName}】", "没有提交任何物品",
                $"你交的是【{PlayerService.ReadName(item)}】", "/任务信息");
        }

        string name = PlayerService.ReadName(item);
        PlayerService.Consume(db, item, 1);
        int progress = Math.Min(spec.Target, run.Progress + 1);
        Dictionary<string, QuestRun> runs = await LoadRunsAsync(db, userId, ct);
        runs[spec.Key] = new QuestRun(run.StartedUtc, progress, Settled: false);
        await SaveRunsAsync(db, userId, runs, ct);

        bool done = progress >= spec.Target;
        return ActionResult.Success(done ? "✅ 任务已达成" : "📦 已提交物资",
            $"【{spec.Name}】{progress}/{spec.Target}",
            [
                $"- 本次提交：{name}",
                done ? "- 够数了，用 /任务结算 领取奖励" : $"- 还差 {spec.Target - progress} 件",
                $"- 奖励：{QuestBoardRules.Reward(spec)}"
            ], done ? ["/任务结算", "/任务列表"] : ["/任务信息", "/背包"]);
    }

    private QuestBoardEntry View(QuestBoardSpec spec, int index, Dictionary<string, QuestRun> runs)
    {
        if (!runs.TryGetValue(spec.Key, out QuestRun? run))
        {
            return new QuestBoardEntry(index, spec.Name, QuestBoardRules.KindName(spec.Kind),
                QuestBoardRules.Requirement(spec), QuestBoardRules.Reward(spec),
                QuestBoardRules.NotStarted, 0, 0, spec.Target);
        }
        string state = run.Settled ? QuestBoardRules.Settled
            : IsCompleted(spec, run) ? QuestBoardRules.Completed
            : QuestBoardRules.Running;
        return new QuestBoardEntry(index, spec.Name, QuestBoardRules.KindName(spec.Kind),
            QuestBoardRules.Requirement(spec), QuestBoardRules.Reward(spec),
            state, run.Settled || IsCompleted(spec, run) ? 0 : Remaining(spec, run),
            run.Progress, spec.Target);
    }

    /// <summary>任务在任务板上的 1 基序号（指令里用）。</summary>
    private static int Index(QuestBoardSpec spec)
        => QuestBoardRules.Quests.Select((quest, i) => (quest, i))
            .First(pair => pair.quest.Key == spec.Key).i + 1;

    private bool IsCompleted(QuestBoardSpec spec, QuestRun run)
        => spec.Kind switch
        {
            QuestBoardKind.Progressive => run.Progress >= spec.Target,
            QuestBoardKind.Continuous => (clock.UtcNow - run.StartedUtc).TotalMinutes >= spec.DurationMinutes,
            _ => true
        };

    private int Remaining(QuestBoardSpec spec, QuestRun run)
    {
        double left = spec.DurationMinutes - (clock.UtcNow - run.StartedUtc).TotalMinutes;
        return left <= 0 ? 0 : (int)Math.Ceiling(left * 60);
    }

    private sealed record QuestRun(DateTime StartedUtc, int Progress, bool Settled);

    private async Task<Dictionary<string, QuestRun>> LoadRunsAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        ActivityProgress? row = await db.ActivityProgress.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityKey == StateKey, ct);
        if (row is null) return [];
        try
        {
            Dictionary<string, RunDto>? dto = JsonSerializer.Deserialize<Dictionary<string, RunDto>>(row.ProgressJson);
            return dto is null
                ? []
                : dto.ToDictionary(pair => pair.Key,
                    pair => new QuestRun(pair.Value.StartedUtc, pair.Value.Progress, pair.Value.Settled));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task SaveRunsAsync(
        BotDbContext db, long userId, Dictionary<string, QuestRun> runs, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(runs.ToDictionary(
            pair => pair.Key,
            pair => new RunDto { StartedUtc = pair.Value.StartedUtc, Progress = pair.Value.Progress, Settled = pair.Value.Settled }));
        ActivityProgress? row = await db.ActivityProgress
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityKey == StateKey, ct);
        if (row is null)
        {
            db.ActivityProgress.Add(new ActivityProgress
            {
                UserId = userId, ActivityKey = StateKey, ProgressJson = json,
                ClaimedJson = "[]", UpdatedAtUtc = clock.UtcNow
            });
            return;
        }
        row.ProgressJson = json;
        row.UpdatedAtUtc = clock.UtcNow;
    }

    private sealed class RunDto
    {
        public DateTime StartedUtc { get; set; }
        public int Progress { get; set; }
        public bool Settled { get; set; }
    }

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
            logger.LogError(exception, "任务板操作失败：UserId={UserId}", userId);
            return ActionResult.Fail("任务操作出现异常", "本次操作已回滚", "稍后重试", "/任务列表");
        }
    }
}
