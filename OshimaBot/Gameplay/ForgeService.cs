using System.Globalization;
using System.Text.Json;
using FunGame.Core.Library.Constant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OshimaBot.Content.Items;
using OshimaBot.Content.Regions;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;

namespace OshimaBot.Gameplay;

/// <summary>锻造信息（进行中的任务 + 配方 + 大师目标）。</summary>
public sealed record ForgeInfo(
    bool Running, string Recipe, string Master, int RemainingSeconds, bool Ready, string Estimate);

/// <summary>
/// 锻造（旧版 <c>ForgeItem_Create / _Master / _Info / _Cancel / _Simulate / _Complete</c>）。
/// <para>材料必须是**野外地区的材料**（地区作物），产地决定系数与品质门槛；
/// 结果在收取时结算，期间可以设「大师锻造」目标（结果不符时消耗一张大师锻造券改成目标）。</para>
/// </summary>
public interface IForgeService
{
    /// <param name="submissions">背包序号 + 件数，可给多组以混合产地。</param>
    Task<ActionResult> StartAsync(long userId, IReadOnlyList<(int Index, int Quantity)> submissions,
        int minutes, CancellationToken cancellationToken = default);

    Task<ForgeInfo> InfoAsync(long userId, CancellationToken cancellationToken = default);

    Task<ActionResult> CancelAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>模拟锻造：不扣材料、不占任务，只按当前提交推演一次结果。</summary>
    Task<ActionResult> SimulateAsync(long userId, IReadOnlyList<(int Index, int Quantity)> submissions,
        CancellationToken cancellationToken = default);

    /// <summary>设定大师锻造目标（地区 + 品质），在结果不符时消耗大师锻造券改判。</summary>
    Task<ActionResult> SetMasterAsync(long userId, int regionId, string qualityText,
        CancellationToken cancellationToken = default);

    Task<ActionResult> ClaimAsync(long userId, CancellationToken cancellationToken = default);
}

public sealed class ForgeService(
    IServiceScopeFactory scopeFactory,
    ILogger<ForgeService> logger,
    IGameClock clock,
    IRandomSource random) : IForgeService
{
    public const string MasterTicketName = "大师锻造券";

    public Task<ActionResult> StartAsync(long userId, IReadOnlyList<(int Index, int Quantity)> submissions,
        int minutes, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            if (await db.ForgingJobs.AnyAsync(x => x.UserId == userId && x.SettledAtUtc == null, ct))
            {
                return ActionResult.Fail("已有锻造进行中", "没有开始新的锻造",
                    "等结束后 /收取锻造，或先 /锻造 取消", "/锻造 信息");
            }

            (List<ForgeMaterial> materials, Dictionary<string, (int Count, RegionItem Crop)> taken, ActionResult? denied) =
                await TakeAsync(db, userId, submissions, ct);
            if (denied is not null) return denied;

            ForgePlan plan = ForgeRules.Plan(materials, random.NextDouble());
            if (!plan.Ok)
            {
                return ActionResult.Fail("配方不满足锻造条件", "没有扣除任何材料",
                    plan.Message, "/锻造 模拟");
            }

            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            if (account.Credits < ForgeRules.CostCredits)
            {
                return ActionResult.Fail("金币不足", "没有扣除任何材料",
                    $"需要 {ForgeRules.CostCredits}，当前 {account.Credits:0.##}", "/状态");
            }
            PlayerService.ApplyCurrency(db, account, -ForgeRules.CostCredits, "credits", "forge_start", "forge",
                userId.ToString(CultureInfo.InvariantCulture));
            account.UpdatedAtUtc = clock.UtcNow;

            int totalMinutes = Math.Clamp(minutes <= 0 ? ForgeRules.DefaultMinutes : minutes,
                1, ForgeRules.MaxMinutes);
            db.ForgingJobs.Add(new ForgingJob
            {
                UserId = userId,
                RecipeKey = "region-forge",
                StateJson = JsonSerializer.Serialize(new ForgeState
                {
                    Minutes = totalMinutes,
                    Materials = [.. materials.Select(x => new ForgeStateLine
                    {
                        Name = x.Name,
                        Count = x.Count,
                        RegionId = x.RegionId,
                        Difficulty = x.Difficulty
                    })]
                }),
                StartedAtUtc = clock.UtcNow,
                EndsAtUtc = clock.UtcNow.AddMinutes(totalMinutes)
            });

            return ActionResult.Success("⚒️ 开始锻造", $"预计 {totalMinutes} 分钟",
                [
                    $"- {plan.Message}",
                    $"- 预计品质：{ForgeRules.QualityName(plan.Quality)}（产地 {plan.RegionName}）",
                    $"- 消耗 {ForgeRules.CostCredits} 金币，剩余 {account.Credits:0.##}",
                    "- 结束后 /收取锻造；期间可用 /锻造 大师 指定目标"
                ], ["/锻造 信息", "/收取锻造", "/状态"]);
        }, cancellationToken);

    public Task<ForgeInfo> InfoAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<ForgeInfo>(async (db, ct) =>
        {
            ForgingJob? job = await db.ForgingJobs.AsNoTracking()
                .Where(x => x.UserId == userId && x.SettledAtUtc == null)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (job is null)
            {
                return new ForgeInfo(false, "没有进行中的锻造", "未指定", 0, false, "");
            }

            ForgeState? state = ReadState(job.StateJson);
            if (state is null)
            {
                return new ForgeInfo(true, "配方数据缺失", "未指定", Remaining(job), false, "");
            }

            List<ForgeMaterial> materials = [.. state.Materials.Select(x =>
                new ForgeMaterial(x.Name, x.Count, x.RegionId, RegionName(x.RegionId), x.Difficulty))];
            ForgePlan plan = ForgeRules.Plan(materials, 0.5);
            string recipe = string.Join("、", state.Materials.Select(x => $"{x.Name} ×{x.Count}"));
            string master = state.Master is null
                ? "未指定"
                : $"{RegionName(state.Master.RegionId)} · {ForgeRules.QualityName(state.Master.Quality)}";
            string estimate = plan.Ok
                ? $"{plan.Message}；预计品质 {ForgeRules.QualityName(plan.Quality)}，产地 {plan.RegionName}"
                : plan.Message;

            return new ForgeInfo(true, recipe, master, Remaining(job), Remaining(job) == 0, estimate);
        }, cancellationToken);

    public Task<ActionResult> CancelAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            ForgingJob? job = await db.ForgingJobs
                .Where(x => x.UserId == userId && x.SettledAtUtc == null)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (job is null)
            {
                return ActionResult.Fail("没有进行中的锻造", "没有取消任何配方",
                    "用 /锻造 @材料 开始一次锻造", "/锻造");
            }

            ForgeState? state = ReadState(job.StateJson);
            int refunded = 0;
            if (state is not null)
            {
                foreach (ForgeStateLine line in state.Materials)
                {
                    RegionItem? crop = FindCrop(line.Name);
                    if (crop is null) continue;
                    await PlayerService.AddItemAsync(db, userId, crop.Name,
                        GameplayRules.QualityTierOf(crop.QualityType), line.Count, ct,
                        crop.Id.ToString(CultureInfo.InvariantCulture));
                    refunded += line.Count;
                }
            }

            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            PlayerService.ApplyCurrency(db, account, ForgeRules.CostCredits, "credits", "forge_cancel", "forge",
                userId.ToString(CultureInfo.InvariantCulture));
            account.UpdatedAtUtc = clock.UtcNow;

            job.SettledAtUtc = clock.UtcNow;
            return ActionResult.Success("↩️ 已取消锻造", $"退回 {refunded} 件材料与 {ForgeRules.CostCredits} 金币",
                ["- 配方已清空，可以重新配"], ["/背包", "/锻造"]);
        }, cancellationToken);

    public Task<ActionResult> SimulateAsync(long userId, IReadOnlyList<(int Index, int Quantity)> submissions,
        CancellationToken cancellationToken = default)
        => ReadAsync<ActionResult>(async (db, ct) =>
        {
            if (submissions.Count == 0)
            {
                return ActionResult.Fail("没有给材料", "没有模拟任何配方",
                    "用法：/锻造 模拟 @1 x40（用 /背包 查编号）", "/背包");
            }

            List<ForgeMaterial> materials = [];
            foreach ((int index, int quantity) in submissions)
            {
                PlayerItem? item = await PlayerService.ItemByIndexAsync(db, userId, index, ct);
                if (item is null)
                {
                    return ActionResult.Fail($"背包里没有 @{index}", "没有模拟任何配方",
                        "用 /背包 查看编号", "/背包");
                }
                (long regionId, RarityType difficulty) = Locate(PlayerService.ReadName(item));
                materials.Add(new ForgeMaterial(PlayerService.ReadName(item), quantity,
                    regionId, RegionName(regionId), difficulty));
            }

            ForgePlan plan = ForgeRules.Plan(materials, random.NextDouble());
            if (!plan.Ok)
            {
                return ActionResult.Fail("这个配方不行", "没有消耗任何材料", plan.Message, "/锻造");
            }
            FunGame.Core.Entity.Item? product = ForgeRules.Product(plan.RegionId, plan.Quality, GameplayRules.NewRandom(random));
            List<string> lines =
            [
                $"- {plan.Message}",
                $"- 品质：{ForgeRules.QualityName(plan.Quality)}　产地：{plan.RegionName}",
                product is null
                    ? "- 该地区没有这个品质的产物：真实锻造会失败（只给失败积分）"
                    : $"- 产物：{product.Name}",
                $"- 锻造积分：{plan.GeneralPoints + (product is null ? plan.FailPoints : plan.SuccessPoints):0.##}"
                    + $"（通用 {plan.GeneralPoints:0.##} + {(product is null ? "失败" : "成功")} "
                    + $"{(product is null ? plan.FailPoints : plan.SuccessPoints):0.##}）",
                "- 模拟不消耗材料，也不影响进行中的锻造"
            ];
            return ActionResult.Success("🔬 模拟锻造", $"{ForgeRules.QualityName(plan.Quality)} · {plan.RegionName}",
                lines, ["/锻造", "/世界地图"]);
        }, cancellationToken);

    public Task<ActionResult> SetMasterAsync(long userId, int regionId, string qualityText,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            ForgingJob? job = await db.ForgingJobs
                .Where(x => x.UserId == userId && x.SettledAtUtc == null)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (job is null)
            {
                return ActionResult.Fail("没有进行中的锻造", "没有设定大师目标",
                    "先用 /锻造 @材料 开始锻造", "/锻造");
            }
            OshimaRegion? region = FunGameService.Regions.OfType<OshimaRegion>().FirstOrDefault(r => r.Id == regionId);
            if (region is null || region.Id == 0)
            {
                return ActionResult.Fail($"没有编号 {regionId} 的野外地区", "没有设定大师目标",
                    "用 /世界地图 查看编号（主城不能当目标）", "/世界地图");
            }
            QualityType? quality = ParseQuality(qualityText);
            if (quality is null)
            {
                return ActionResult.Fail("品质写法不对", "没有设定大师目标",
                    "可用：普通 / 优秀 / 稀有 / 史诗 / 传说 / 神话（或 0–5）", "/锻造 信息");
            }

            ForgeState? state = ReadState(job.StateJson);
            if (state is null) return ActionResult.Fail("配方数据缺失", "没有设定大师目标", "取消后重来", "/锻造");

            state.Master = new ForgeStateMaster { RegionId = regionId, Quality = quality.Value };
            job.StateJson = JsonSerializer.Serialize(state);

            return ActionResult.Success("🎯 已指定大师锻造目标", $"{region.Name} · {ForgeRules.QualityName(quality.Value)}",
                [
                    "- 结果若与目标不符，会尝试消耗 1 张「大师锻造券」把产物改成目标",
                    "- 没有券就只能照常结算",
                    "- 用 /收取锻造 结束"
                ], ["/锻造 信息", "/收取锻造"]);
        }, cancellationToken);

    public Task<ActionResult> ClaimAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            ForgingJob? job = await db.ForgingJobs
                .Where(x => x.UserId == userId && x.SettledAtUtc == null)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            if (job is null)
            {
                return ActionResult.Fail("没有进行中的锻造", "没有产出任何物品",
                    "用 /锻造 @材料 开始一次锻造", "/锻造");
            }
            if (job.EndsAtUtc > clock.UtcNow)
            {
                return ActionResult.Fail("锻造还没结束", "没有产出任何物品",
                    $"还需约 {Remaining(job)} 秒", "/锻造 信息");
            }

            ForgeState? state = ReadState(job.StateJson);
            if (state is null)
            {
                job.SettledAtUtc = clock.UtcNow;
                return ActionResult.Fail("配方数据缺失", "本次锻造作废",
                    "材料已消耗，联系管理员核查", "/锻造");
            }

            List<ForgeMaterial> materials = [.. state.Materials.Select(x =>
                new ForgeMaterial(x.Name, x.Count, x.RegionId, RegionName(x.RegionId), x.Difficulty))];
            ForgePlan plan = ForgeRules.Plan(materials, random.NextDouble());
            if (!plan.Ok)
            {
                job.SettledAtUtc = clock.UtcNow;
                return ActionResult.Fail("配方不成立", "本次锻造作废", plan.Message, "/锻造");
            }

            long regionId = plan.RegionId;
            QualityType quality = plan.Quality;
            string masterNote = "";
            if (state.Master is { } master && (master.RegionId != regionId || master.Quality != quality))
            {
                PlayerItem? ticket = (await ItemStore.ListAsync(db, userId, ct))
                    .FirstOrDefault(x => x.Name == MasterTicketName && !x.Locked && !x.Bound);
                if (ticket is not null)
                {
                    PlayerService.Consume(db, ticket, 1);
                    regionId = master.RegionId;
                    quality = master.Quality;
                    masterNote = $"- 大师锻造券生效：产物改判为 {RegionName(regionId)} · {ForgeRules.QualityName(quality)}";
                }
                else
                {
                    masterNote = "- 结果与大师目标不符，但背包里没有「大师锻造券」，按原结果结算";
                }
            }

            FunGame.Core.Entity.Item? product = ForgeRules.Product(regionId, quality, GameplayRules.NewRandom(random));
            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            double points = plan.GeneralPoints + (product is null ? plan.FailPoints : plan.SuccessPoints);
            if (points > 0)
            {
                PlayerService.ApplyCurrency(db, account, points, "forge_points", "forge_result", "forge",
                    userId.ToString(CultureInfo.InvariantCulture));
            }
            account.UpdatedAtUtc = clock.UtcNow;

            List<string> lines =
            [
                $"- {plan.Message}",
                $"- 品质：{ForgeRules.QualityName(quality)}　产地：{RegionName(regionId)}",
                product is null
                    ? "- 该地区没有这个品质的产物，本次锻造失败"
                    : $"- 产物：{product.Name}",
                $"- 锻造积分 +{points:0.##}（通用 {plan.GeneralPoints:0.##} + "
                    + $"{(product is null ? $"失败 {plan.FailPoints:0.##}" : $"成功 {plan.SuccessPoints:0.##}")}）",
                $"- 当前锻造积分 {account.ForgePoints:0.##}"
            ];
            if (masterNote.Length > 0) lines.Add(masterNote);
            if (product is not null)
            {
                await PlayerService.AddItemAsync(db, userId, product.Name,
                    GameplayRules.QualityTierOf(product.QualityType), 1, ct,
                    product.Id.ToString(CultureInfo.InvariantCulture));
            }

            job.SettledAtUtc = clock.UtcNow;
            return ActionResult.Success(product is null ? "💥 锻造失败" : "⚒️ 锻造成功",
                product is null
                    ? $"{ForgeRules.QualityName(quality)} · {RegionName(regionId)}"
                    : $"{product.Name}（{ForgeRules.QualityName(quality)}）",
                lines, ["/背包", "/锻造", "/锻造排行榜"]);
        }, cancellationToken);

    // ---------------- 内部 ----------------

    /// <summary>从背包取材料：必须是野外地区的作物（旧版靠名字反查地区，这里同样按名字反查）。</summary>
    private async Task<(List<ForgeMaterial> Materials, Dictionary<string, (int Count, RegionItem Crop)> Taken, ActionResult? Denied)>
        TakeAsync(BotDbContext db, long userId, IReadOnlyList<(int Index, int Quantity)> submissions, CancellationToken ct)
    {
        List<ForgeMaterial> materials = [];
        Dictionary<string, (int Count, RegionItem Crop)> taken = [];
        if (submissions.Count == 0)
        {
            return ([], taken, ActionResult.Fail("没有给材料", "没有扣除任何材料",
                "用法：/锻造 @1 x40 或 /锻造 @1 x20 @2 x20（用 /背包 查编号）", "/背包"));
        }

        HashSet<int> seen = [];
        foreach ((int index, int quantity) in submissions)
        {
            if (!seen.Add(index))
            {
                return ([], taken, ActionResult.Fail($"@{index} 重复提交", "没有扣除任何材料",
                    "同一个背包格只写一次，用 x数量 表示件数", "/锻造"));
            }
            PlayerItem? item = await PlayerService.ItemByIndexAsync(db, userId, index, ct);
            if (item is null)
            {
                return ([], taken, ActionResult.Fail($"背包里没有 @{index}", "没有扣除任何材料",
                    "用 /背包 查看编号", "/背包"));
            }
            if (item.Locked || item.Bound)
            {
                return ([], taken, ActionResult.Fail("该物品已锁定或绑定", "没有扣除任何材料",
                    "先 /解锁 @编号", "/背包"));
            }
            if (quantity > item.Quantity)
            {
                return ([], taken, ActionResult.Fail("数量超过持有量", "没有扣除任何材料",
                    $"@{index} 只有 {item.Quantity} 件，你要 {quantity} 件", "/背包"));
            }

            string name = PlayerService.ReadName(item);
            (long regionId, RarityType difficulty) = Locate(name);
            if (regionId <= 0)
            {
                return ([], taken, ActionResult.Fail($"{name} 不是锻造材料", "没有扣除任何材料",
                    "锻造只收野外地区的材料：先用 /探索 采一些", "/世界地图"));
            }
            materials.Add(new ForgeMaterial(name, quantity, regionId, RegionName(regionId), difficulty));
            if (taken.TryGetValue(name, out var existing))
            {
                taken[name] = (existing.Count + quantity, existing.Crop);
            }
            else
            {
                taken[name] = (quantity, FindCrop(name)!);
            }
        }

        // 全部校验通过后再扣，避免扣一半失败。
        foreach ((int index, int quantity) in submissions)
        {
            PlayerItem? item = await PlayerService.ItemByIndexAsync(db, userId, index, ct);
            if (item is not null) PlayerService.Consume(db, item, quantity);
        }
        return (materials, taken, null);
    }

    /// <summary>按名字找地区作物，返回 (地区 Id, 地区难度)；找不到返回 (0, OneStar)。</summary>
    private static (long RegionId, RarityType Difficulty) Locate(string name)
    {
        foreach (OshimaRegion region in FunGameService.Regions.OfType<OshimaRegion>())
        {
            if (region.Id == 0) continue;
            if (region.RegionCrops.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
            {
                return (region.Id, region.Difficulty);
            }
        }
        return (0, RarityType.OneStar);
    }

    private static RegionItem? FindCrop(string name)
        => FunGameService.Regions.OfType<OshimaRegion>()
            .SelectMany(region => region.RegionCrops)
            .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    private static string RegionName(long regionId)
        => FunGameService.Regions.OfType<OshimaRegion>()
            .FirstOrDefault(r => r.Id == regionId)?.Name ?? $"地区#{regionId}";

    private static QualityType? ParseQuality(string text)
    {
        string value = (text ?? "").Trim();
        if (int.TryParse(value, out int tier)) return GameplayRules.QualityTypeOf(tier);
        return value switch
        {
            "普通" or "白" => QualityType.White,
            "优秀" or "绿" => QualityType.Green,
            "稀有" or "蓝" => QualityType.Blue,
            "史诗" or "紫" => QualityType.Purple,
            "传说" or "橙" => QualityType.Orange,
            "神话" or "红" => QualityType.Red,
            _ => null
        };
    }

    private int Remaining(ForgingJob job)
    {
        double left = (job.EndsAtUtc - clock.UtcNow).TotalSeconds;
        return left <= 0 ? 0 : (int)Math.Ceiling(left);
    }

    private static ForgeState? ReadState(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ForgeState>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ForgeState
    {
        public int Minutes { get; set; }
        public List<ForgeStateLine> Materials { get; set; } = [];
        public ForgeStateMaster? Master { get; set; }
    }

    private sealed class ForgeStateLine
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public long RegionId { get; set; }
        public RarityType Difficulty { get; set; }
    }

    private sealed class ForgeStateMaster
    {
        public long RegionId { get; set; }
        public QualityType Quality { get; set; }
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
            logger.LogError(exception, "锻造操作失败：UserId={UserId}", userId);
            return ActionResult.Fail("锻造操作出现异常", "本次操作已回滚", "稍后重试", "/锻造");
        }
    }
}
