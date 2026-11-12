using System.Globalization;
using System.Text.Json;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Content.Items;
using OshimaBot.Content.Units;
using OshimaBot.Content.Regions;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Combat;
using OshimaBot.Game.Services;
using CoreCharacter = FunGame.Core.Entity.Character;
using CoreTeam = FunGame.Core.Entity.Team;

namespace OshimaBot.Gameplay;

/// <summary>探索许可（虚物品：不进背包，按日配额记账）。</summary>
public sealed record ExploreQuota(int Left, string Day, bool LunchClaimed, bool DinnerClaimed, int DailyMax);

/// <summary>一次进行中的探索。</summary>
public sealed record ExploreRunView(
    int Index, long RunId, long RegionId, string RegionName, string Characters,
    int Cost, int RemainingSeconds, bool Ready);

/// <summary>
/// 世界探索（旧版 <c>ExploreRegion</c> / <c>SettleExplore</c> / <c>GetExploreInfo</c>）。
/// <para>三个要点照旧版：探索许可按「角色数 × 星级」扣、同一角色不能并行探索、
/// 2 分钟后才可结算；结算时按难度掷结果，**战斗结果走真实战斗**（用户 2026-11-12 决定）。</para>
/// </summary>
public interface IExploreService
{
    /// <summary>领午餐/晚餐并滚到新的游戏日；任何探索相关操作前都会先调它。</summary>
    Task<ExploreQuota> RefreshQuotaAsync(long userId, CancellationToken cancellationToken = default);

    Task<(ExploreQuota Quota, IReadOnlyList<ExploreRunView> Runs)> InfoAsync(
        long userId, CancellationToken cancellationToken = default);

    Task<ActionResult> StartAsync(long userId, int regionId, IReadOnlyList<int> characterIndexes,
        bool useSquad, CancellationToken cancellationToken = default);

    Task<BattleReport> ClaimAsync(long userId, CancellationToken cancellationToken = default);
}

public sealed class ExploreService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExploreService> logger,
    IGameClock clock,
    IRandomSource random) : IExploreService
{
    private const string QuotaKey = "explore";

    public Task<ExploreQuota> RefreshQuotaAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (ExploreQuota quota, _) = await LoadQuotaAsync(db, userId, ct);
            return quota;
        }, cancellationToken);

    public Task<(ExploreQuota Quota, IReadOnlyList<ExploreRunView> Runs)> InfoAsync(
        long userId, CancellationToken cancellationToken = default)
        => MutateAsync<(ExploreQuota Quota, IReadOnlyList<ExploreRunView> Runs)>(userId, async (db, ct) =>
        {
            (ExploreQuota quota, _) = await LoadQuotaAsync(db, userId, ct);
            IReadOnlyList<ExploreRunView> runs = await RunsAsync(db, userId, ct);
            return (quota, runs);
        }, cancellationToken);

    public Task<ActionResult> StartAsync(long userId, int regionId, IReadOnlyList<int> characterIndexes,
        bool useSquad, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            OshimaRegion? region = FunGameService.Regions.OfType<OshimaRegion>()
                .FirstOrDefault(r => r.Id == regionId);
            if (region is null)
            {
                return ActionResult.Fail($"没有编号 {regionId} 的地区", "没有开始探索",
                    "用 /世界地图 查看编号（0 是主城，不能探索）", "/世界地图");
            }
            if (region.Id == 0)
            {
                return ActionResult.Fail("铎京城不能探索", "没有开始探索",
                    "主城是安全区，去 /世界地图 挑一个野外地区", "/世界地图");
            }

            List<PlayerCharacter> roster = await PlayerService.AllCharactersAsync(db, userId, ct);
            if (roster.Count == 0)
            {
                return ActionResult.Fail("没有可出战的角色", "没有开始探索",
                    "先 /注册 建立存档，或用 /抽卡 获取角色", "/状态");
            }

            List<PlayerCharacter> party = [];
            if (useSquad)
            {
                party = await PlayerService.SquadCharactersAsync(db, userId, ct);
                if (party.Count == 0)
                {
                    return ActionResult.Fail("没有小队编成", "没有开始探索",
                        "先用 /小队 加入 @编号 编成 1–3 人，或直接 /探索 地区 角色序号", "/小队");
                }
            }
            else
            {
                foreach (int index in characterIndexes)
                {
                    if (index <= 0 || index > roster.Count)
                    {
                        return ActionResult.Fail($"没有序号 {index} 的角色", "没有开始探索",
                            $"你当前有 {roster.Count} 名角色，用 /我的角色 查看序号", "/我的角色");
                    }
                    party.Add(roster[index - 1]);
                }
            }
            if (party.Count == 0)
            {
                return ActionResult.Fail("没有指定探索成员", "没有开始探索",
                    "用法：/探索 地区编号 角色序号…（如 /探索 1 1 2）或 /探索 1 小队", "/探索信息");
            }
            if (party.Count > ExploreRules.MaxCharacters)
            {
                return ActionResult.Fail("一次最多带 4 名角色", "没有开始探索",
                    "角色越多奖励越多，但探索许可消耗也按人数翻倍", "/探索信息");
            }
            if (party.Select(x => x.Guid).Distinct().Count() != party.Count)
            {
                return ActionResult.Fail("探索成员有重复", "没有开始探索",
                    "每个角色只能上一次，检查一下序号", "/我的角色");
            }

            // 角色锁定：已在别的探索里的不能再出发。
            List<ExplorationRun> active = await db.ExplorationRuns
                .Where(x => x.UserId == userId).ToListAsync(ct);
            HashSet<string> busy = [];
            foreach (ExplorationRun run in active)
            {
                if ((clock.UtcNow - run.StartedAtUtc) >= ExploreRules.Duration) continue;   // 已完成待结算，不锁人
                foreach (string guid in ReadGuids(run)) busy.Add(GuidFor(guid));
            }
            List<string> locked = [.. party.Where(x => busy.Contains(GuidFor(x.Guid.ToString()))).Select(x => x.Name)];
            if (locked.Count > 0)
            {
                return ActionResult.Fail("有角色正在探索中", "没有开始探索",
                    $"{string.Join("、", locked)} 还在别的探索里，等结算后再用", "/探索信息");
            }

            int stars = ExploreRules.Stars(region.Difficulty);
            int cost = ExploreRules.Cost(party.Count, stars);
            (ExploreQuota quota, ActivityProgress? quotaRow) = await LoadQuotaAsync(db, userId, ct);
            if (quota.Left < cost)
            {
                return ActionResult.Fail("探索许可不足", "没有开始探索",
                    $"本次需要 {cost}（{party.Count} 人 × {stars} 星），剩余 {quota.Left}；"
                    + "许可每天 4:00 恢复 60，11–13 点与 17–19 点上线各再给 30", "/探索信息");
            }

            WriteQuota(db, quotaRow, userId, quota with { Left = quota.Left - cost });
            db.ExplorationRuns.Add(new ExplorationRun
            {
                UserId = userId,
                RegionKey = region.Id.ToString(CultureInfo.InvariantCulture),
                StateJson = JsonSerializer.Serialize(new
                {
                    regionId = region.Id,
                    guids = party.Select(x => x.Guid.ToString()).ToArray()
                }),
                StartedAtUtc = clock.UtcNow
            });

            return ActionResult.Success("🧭 出发探索", $"{region.Name} · {party.Count} 人 · {stars} 星",
                [
                    $"- 成员：{string.Join("、", party.Select(x => $"{x.Name} Lv.{x.Level}"))}",
                    $"- 消耗探索许可 {cost}，剩余 {quota.Left - cost}",
                    $"- 预计 {ExploreRules.DurationMinutes} 分钟后可 /探索结算",
                    "- 探索期间这些角色不能再次出发"
                ], ["/探索信息", "/探索结算", "/世界地图"]);
        }, cancellationToken);

    public Task<BattleReport> ClaimAsync(long userId, CancellationToken cancellationToken = default)
        => ReportAsync(async () =>
        {
            if (userId <= 0) return BattleReport.Empty(PlayerService.NoIdentity);
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);

            List<ExplorationRun> runs = await db.ExplorationRuns
                .Where(x => x.UserId == userId).OrderBy(x => x.Id).ToListAsync(cancellationToken);
            if (runs.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BattleReport.Empty(ActionResult.Fail("没有进行中的探索", "没有结算任何探索",
                    "用 /探索 地区编号 角色序号 出发", "/世界地图"));
            }

            List<PlayerCharacter> roster = await PlayerService.AllCharactersAsync(db, userId, cancellationToken);
            Dictionary<string, PlayerCharacter> byGuid = roster
                .GroupBy(x => GuidFor(x.Guid.ToString())).ToDictionary(g => g.Key, g => g.First());

            List<string> lines = [];
            List<string> logs = [];
            List<ExplorationRun> finished = [];
            bool anyReady = false;
            foreach (ExplorationRun run in runs)
            {
                int remaining = RemainingSeconds(run);
                if (remaining > 0)
                {
                    lines.Add($"- 地区 #{run.RegionKey}：还需 {remaining} 秒");
                    continue;
                }
                anyReady = true;
                finished.Add(run);
                (List<string> runLines, List<string> runLog) = await ResolveAsync(db, userId, run, byGuid, cancellationToken);
                lines.AddRange(runLines);
                logs.AddRange(runLog);
            }

            if (!anyReady)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BattleReport.Empty(ActionResult.Fail("探索还没结束", "没有结算任何探索",
                    $"每次探索 {ExploreRules.DurationMinutes} 分钟，" + string.Join("；", lines), "/探索信息"));
            }

            foreach (ExplorationRun run in finished) db.ExplorationRuns.Remove(run);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new BattleReport(
                ActionResult.Success("🗺️ 探索结算", $"{finished.Count} 场探索完成",
                    lines, ["/探索信息", "/背包", "/世界地图"]),
                logs);
        });

    // ---------------- 结算 ----------------

    private async Task<(List<string> Lines, List<string> Log)> ResolveAsync(
        BotDbContext db, long userId, ExplorationRun run, Dictionary<string, PlayerCharacter> byGuid,
        CancellationToken ct)
    {
        List<string> lines = [];
        List<string> log = [];
        long regionId = long.TryParse(run.RegionKey, out long id) ? id : 0;
        OshimaRegion? region = FunGameService.Regions.OfType<OshimaRegion>().FirstOrDefault(r => r.Id == regionId);
        if (region is null)
        {
            lines.Add($"- 地区 #{run.RegionKey} 已不存在（内容包变更），本次探索作废");
            return (lines, log);
        }

        List<PlayerCharacter> party = [.. ReadGuids(run)
            .Select(guid => byGuid.GetValueOrDefault(GuidFor(guid)))
            .OfType<PlayerCharacter>()];
        if (party.Count == 0)
        {
            lines.Add($"- 【{region.Name}】的探索队员已不在存档里，本次作废");
            return (lines, log);
        }

        int stars = ExploreRules.Stars(region.Difficulty);
        ExploreResult result = ExploreRules.Roll(random, stars);
        PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
        string reference = region.Id.ToString(CultureInfo.InvariantCulture);
        string members = string.Join("、", party.Select(x => x.Name));
        lines.Add($"- 【{region.Name}】{members} → {ExploreRules.ResultName(result)}");

        switch (result)
        {
            case ExploreResult.Nothing:
                lines.Add("  什么也没找到。");
                break;

            case ExploreResult.General:
            {
                long credits = ExploreRules.Credits(stars, party.Count);
                PlayerService.ApplyCurrency(db, account, credits, "credits", "explore", "region", reference);
                lines.Add($"  发现了一些零散物资，换成 {credits} 金币。");
                break;
            }

            case ExploreResult.Earned:
            {
                List<RegionItem> crops = [.. region.RegionCrops];
                if (crops.Count == 0)
                {
                    long credits = ExploreRules.Credits(stars, party.Count);
                    PlayerService.ApplyCurrency(db, account, credits, "credits", "explore", "region", reference);
                    lines.Add($"  这里没有可采材料，捡到 {credits} 金币。");
                    break;
                }
                int count = ExploreRules.Materials(stars, party.Count);
                RegionItem crop = crops[random.Next(crops.Count)];
                await PlayerService.AddItemAsync(db, userId, crop.Name, GameplayRules.QualityTierOf(crop.QualityType),
                    count, ct, crop.Id.ToString(CultureInfo.InvariantCulture));
                lines.Add($"  采到了 {crop.Name} ×{count}。");
                break;
            }

            case ExploreResult.Fight:
            {
                List<CoreCharacter> enemies = [.. BuildEnemies(region, stars)];
                List<CoreCharacter> allies = [.. party.Select(CombatUnits.FromPlayer).OfType<CoreCharacter>()];
                if (allies.Count == 0 || enemies.Count == 0)
                {
                    lines.Add("  敌人或队伍数据缺失，本次作废。");
                    break;
                }

                BattleResult battle = FunGameActionQueue.RunTeam(
                    [new CoreTeam("探索队", allies), new CoreTeam(region.Name, enemies)],
                    new BattleOptions { MaxRound = 150, UseRoundRewards = false });
                log.AddRange(battle.Log);

                bool win = enemies.All(x => x.HP <= 0);
                if (win)
                {
                    List<RegionItem> crops = [.. region.RegionCrops];
                    if (crops.Count > 0)
                    {
                        int count = ExploreRules.BattleMaterials(stars, party.Count);
                        RegionItem crop = crops[random.Next(crops.Count)];
                        await PlayerService.AddItemAsync(db, userId, crop.Name,
                            GameplayRules.QualityTierOf(crop.QualityType), count, ct,
                            crop.Id.ToString(CultureInfo.InvariantCulture));
                        lines.Add($"  击败 {string.Join("、", enemies.Select(x => x.Name))}（{battle.Rounds} 回合），"
                            + $"获得 {crop.Name} ×{count}。");
                    }
                    else
                    {
                        long credits = ExploreRules.Credits(stars, party.Count);
                        PlayerService.ApplyCurrency(db, account, credits, "credits", "explore", "region", reference);
                        lines.Add($"  击败 {string.Join("、", enemies.Select(x => x.Name))}（{battle.Rounds} 回合），获得 {credits} 金币。");
                    }
                }
                else
                {
                    long consolation = ExploreRules.Credits(stars, party.Count) / 3;
                    PlayerService.ApplyCurrency(db, account, consolation, "credits", "explore_fail", "region", reference);
                    lines.Add($"  不敌 {string.Join("、", enemies.Select(x => x.Name))}（{battle.Rounds} 回合），"
                        + $"撤退时只带回 {consolation} 金币。");
                }
                break;
            }

            default:
            {
                // Event 概率为 0，正常走不到；留一个安全分支。
                long credits = ExploreRules.Credits(stars, party.Count);
                PlayerService.ApplyCurrency(db, account, credits, "credits", "explore", "region", reference);
                lines.Add($"  遇到了一场小插曲，收获 {credits} 金币。");
                break;
            }
        }

        account.UpdatedAtUtc = clock.UtcNow;
        return (lines, log);
    }

    /// <summary>
    /// 敌人：星级 1–2 一个生物、3–4 两个、5 星三个；有 50% 概率换成地区头目（生命 +50%，同旧版）。
    /// </summary>
    private List<CoreCharacter> BuildEnemies(OshimaRegion region, int stars)
    {
        List<CoreCharacter> enemies = [];
        List<RegionUnit> units = [.. region.RegionUnits];
        List<RegionCharacter> bosses = [.. region.RegionCharacters];

        if (bosses.Count > 0 && random.Next(2) == 1)
        {
            CoreCharacter boss = bosses[random.Next(bosses.Count)].Copy();
            boss.Name = $"{boss.Name}（{region.Name}）";
            boss.ExHPPercentage += 0.5;         // 旧版头目 +50% 生命
            boss.Recovery(EP: 200);
            enemies.Add(boss);
            return enemies;
        }
        if (units.Count == 0)
        {
            // 没有生物表就退化用头目（避免空战斗）。
            if (bosses.Count == 0) return enemies;
            CoreCharacter fallback = bosses[random.Next(bosses.Count)].Copy();
            fallback.Recovery(EP: 200);
            enemies.Add(fallback);
            return enemies;
        }

        for (int i = 0; i < ExploreRules.EnemyCount(stars); i++)
        {
            CoreCharacter unit = units[random.Next(units.Count)].Copy();
            int same = enemies.Count(x => x.Name.StartsWith(unit.Name, StringComparison.Ordinal));
            if (same > 0) unit.Name = $"{unit.Name}·{same + 1}";
            unit.Recovery(EP: 200);
            enemies.Add(unit);
        }
        return enemies;
    }

    // ---------------- 配额与运行态 ----------------

    /// <summary>
    /// 读取并**顺手推进**探索许可：跨日（4:00 边界）重置为 60，
    /// 11:00–13:59 / 17:00–19:59 首次访问各 +30（旧版「午餐 / 晚餐」）。
    /// <para>返回的 <c>Row</c> 是**被跟踪的实体**，扣配额时复用它，
    /// 否则同一个 (UserId, ActivityKey) 会被 Add 两次（EF 报 identity conflict）。</para>
    /// </summary>
    private async Task<(ExploreQuota Quota, ActivityProgress? Row)> LoadQuotaAsync(
        BotDbContext db, long userId, CancellationToken ct)
    {
        string today = GameplayRules.StoreDay(clock.UtcNow);
        DateTime local = GameplayRules.ToLocal(clock.UtcNow);
        ActivityProgress? row = await db.ActivityProgress
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityKey == QuotaKey, ct);

        ExploreQuota quota = row is null
            ? new ExploreQuota(ExploreRules.DailyQuota, today, false, false, ExploreRules.DailyQuota)
            : ReadQuota(row.ProgressJson, today);

        bool changed = row is null;
        if (local.Hour is >= 11 and <= 13 && !quota.LunchClaimed)
        {
            quota = quota with { Left = quota.Left + ExploreRules.MealBonus, LunchClaimed = true };
            changed = true;
        }
        if (local.Hour is >= 17 and <= 19 && !quota.DinnerClaimed)
        {
            quota = quota with { Left = quota.Left + ExploreRules.MealBonus, DinnerClaimed = true };
            changed = true;
        }
        if (changed) row = WriteQuota(db, row, userId, quota);
        return (quota, row);
    }

    /// <summary>把配额写进（或新建）那一行，返回被跟踪的实体。</summary>
    private ActivityProgress WriteQuota(
        BotDbContext db, ActivityProgress? row, long userId, ExploreQuota quota)
    {
        string json = JsonSerializer.Serialize(new
        {
            day = quota.Day,
            left = quota.Left,
            lunch = quota.LunchClaimed,
            dinner = quota.DinnerClaimed
        });
        if (row is null)
        {
            row = new ActivityProgress
            {
                UserId = userId, ActivityKey = QuotaKey, ProgressJson = json,
                ClaimedJson = "[]", UpdatedAtUtc = clock.UtcNow
            };
            db.ActivityProgress.Add(row);
            return row;
        }
        row.ProgressJson = json;
        row.UpdatedAtUtc = clock.UtcNow;
        return row;
    }

    private static ExploreQuota ReadQuota(string json, string today)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string day = root.TryGetProperty("day", out JsonElement d) ? d.GetString() ?? today : today;
            if (!string.Equals(day, today, StringComparison.Ordinal))
            {
                return new ExploreQuota(ExploreRules.DailyQuota, today, false, false, ExploreRules.DailyQuota);
            }
            return new ExploreQuota(
                root.TryGetProperty("left", out JsonElement left) ? left.GetInt32() : ExploreRules.DailyQuota,
                day,
                root.TryGetProperty("lunch", out JsonElement lunch) && lunch.GetBoolean(),
                root.TryGetProperty("dinner", out JsonElement dinner) && dinner.GetBoolean(),
                ExploreRules.DailyQuota);
        }
        catch (JsonException)
        {
            return new ExploreQuota(ExploreRules.DailyQuota, today, false, false, ExploreRules.DailyQuota);
        }
    }

    private async Task<List<ExploreRunView>> RunsAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        List<ExplorationRun> runs = await db.ExplorationRuns.AsNoTracking()
            .Where(x => x.UserId == userId).OrderBy(x => x.Id).ToListAsync(ct);
        if (runs.Count == 0) return [];

        List<PlayerCharacter> roster = await PlayerService.AllCharactersAsync(db, userId, ct);
        Dictionary<string, string> names = roster
            .GroupBy(x => GuidFor(x.Guid.ToString()))
            .ToDictionary(g => g.Key, g => g.First().Name);

        List<ExploreRunView> views = [];
        for (int i = 0; i < runs.Count; i++)
        {
            ExplorationRun run = runs[i];
            long regionId = long.TryParse(run.RegionKey, out long id) ? id : 0;
            OshimaRegion? region = FunGameService.Regions.OfType<OshimaRegion>().FirstOrDefault(r => r.Id == regionId);
            int stars = region is null ? 1 : ExploreRules.Stars(region.Difficulty);
            List<string> members = [.. ReadGuids(run)
                .Select(guid => names.GetValueOrDefault(GuidFor(guid), "已离队"))];
            int remaining = RemainingSeconds(run);
            views.Add(new ExploreRunView(i + 1, run.Id, regionId, region?.Name ?? $"地区#{regionId}",
                string.Join("、", members), ExploreRules.Cost(Math.Max(1, members.Count), stars),
                remaining, remaining <= 0));
        }
        return views;
    }

    private int RemainingSeconds(ExplorationRun run)
    {
        double remaining = (ExploreRules.Duration - (clock.UtcNow - run.StartedAtUtc)).TotalSeconds;
        return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining);
    }

    private static IEnumerable<string> ReadGuids(ExplorationRun run)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(run.StateJson);
            if (!document.RootElement.TryGetProperty("guids", out JsonElement guids)
                || guids.ValueKind != JsonValueKind.Array) return [];
            return [.. guids.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Guid 统一用小写比较（存档里可能是大写）。</summary>
    private static string GuidFor(string guid) => guid.Trim().ToLowerInvariant();

    // ---------------- plumbing ----------------

    private async Task<T> MutateAsync<T>(long userId, Func<BotDbContext, CancellationToken, Task<T>> body, CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (userId <= 0) throw new InvalidOperationException("identity");
            T result = await body(db, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<BattleReport> ReportAsync(Func<Task<BattleReport>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "探索结算失败");
            return BattleReport.Empty(ActionResult.Fail("探索结算出现异常", "本次结算已回滚",
                "稍后重试，或联系管理员", "/探索信息"));
        }
    }
}
