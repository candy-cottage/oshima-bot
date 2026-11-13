using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>社团周常任务：进度由社团行为累计，满目标后由社长/管理结算进社团基金。</summary>
public sealed partial class ClubService
{
    public Task<IReadOnlyList<ClubTaskEntry>> TasksAsync(long userId, CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<ClubTaskEntry>>(async (db, ct) =>
        {
            ClubMember? member = await db.ClubMembers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (member is null) return [];

            string period = GameplayRules.ClubWeek(clock.UtcNow);
            List<ClubTaskProgress> rows = await db.ClubTaskProgresses.AsNoTracking()
                .Where(x => x.ClubId == member.ClubId && x.Period == period).ToListAsync(ct);

            return [.. ClubRules.Tasks.Select((spec, i) =>
            {
                ClubTaskProgress? row = rows.FirstOrDefault(x => x.Key == spec.Key);
                int progress = Math.Min(row?.Progress ?? 0, spec.Target);
                return new ClubTaskEntry(i + 1, spec.Key, spec.Name, spec.Unit, spec.Target, progress,
                    spec.RewardFunds, row?.ClaimedAtUtc is not null, spec.Hint);
            })];
        }, cancellationToken);

    /// <summary>提交物资：把背包里的物品交进社团，推进「物资征集」并折算进社团基金。</summary>
    public Task<ActionResult> ContributeTaskAsync(long userId, int itemIndex, int quantity,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember member, ActionResult? denied) = await RequireMemberAsync(db, userId, ct);
            if (denied is not null) return denied;

            string period = GameplayRules.ClubWeek(clock.UtcNow);
            ClubTaskSpec spec = ClubRules.Tasks.First(x => x.Kind == ClubTaskKind.Collect);
            ClubTaskProgress? row = await db.ClubTaskProgresses.FirstOrDefaultAsync(
                x => x.ClubId == club.Id && x.Period == period && x.Key == spec.Key, ct);
            if (row?.ClaimedAtUtc is not null)
            {
                return ActionResult.Fail("本周期物资征集已结算", "没有提交任何物品",
                    "下周会重新开始，先看看别的任务", "/社团任务");
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
                    "先 /解锁 @编号 再提交", "/背包");
            }

            int amount = Math.Clamp(quantity, 1, item.Quantity);
            long funds = ClubRules.CollectFunds(item.Quality) * amount;
            string name = PlayerService.ReadName(item);
            int quality = item.Quality;

            PlayerService.Consume(db, item, amount);
            club.Funds += funds;
            member.Contribution += funds;
            int progress = await AdvanceTaskAsync(db, club.Id, ClubTaskKind.Collect, amount, ct);

            return ActionResult.Success("📦 已提交物资",
                $"{name} ×{amount} → [ {club.Name} ]",
                [
                    $"- 折算社团基金 +{GameplayRules.Money(funds)}（按{GameplayRules.QualityName(quality)}分解价的一半）",
                    $"- 物资征集进度：{Math.Min(progress, spec.Target)}/{spec.Target} {spec.Unit}",
                    $"- 社团基金：{GameplayRules.Money(club.Funds)}"
                ], ["/社团任务", "/我的社团", "/背包"]);
        }, cancellationToken);

    /// <summary>结算：把已完成的任务奖励一次性打进社团基金，并按人头记个人贡献。</summary>
    public Task<ActionResult> ClaimTaskAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, ct) =>
        {
            (Club club, ClubMember mine, ActionResult? denied) = await RequireAdminAsync(db, userId, ct);
            if (denied is not null) return denied;

            if (index <= 0 || index > ClubRules.Tasks.Count)
            {
                return ActionResult.Fail($"任务 @{index} 不存在", "没有结算任何任务",
                    $"本社团每周有 {ClubRules.Tasks.Count} 个任务，用 /社团任务 查看", "/社团任务");
            }
            ClubTaskSpec spec = ClubRules.Tasks[index - 1];
            string period = GameplayRules.ClubWeek(clock.UtcNow);
            ClubTaskProgress? row = await db.ClubTaskProgresses.FirstOrDefaultAsync(
                x => x.ClubId == club.Id && x.Period == period && x.Key == spec.Key, ct);
            if (row is null || row.Progress < spec.Target)
            {
                return ActionResult.Fail("任务还没完成", "没有结算任何任务",
                    $"{spec.Name}：{(row?.Progress ?? 0)}/{spec.Target} {spec.Unit}，" + spec.Hint, "/社团任务");
            }
            if (row.ClaimedAtUtc is not null)
            {
                return ActionResult.Fail("这个任务本周期已经结算过了", "没有重复发放奖励",
                    "下周会重新开始", "/社团任务");
            }

            int members = await db.ClubMembers.CountAsync(x => x.ClubId == club.Id, ct);
            double share = members <= 0 ? 0 : (double)spec.RewardFunds / members;
            List<ClubMember> roster = await db.ClubMembers
                .Where(x => x.ClubId == club.Id).ToListAsync(ct);
            foreach (ClubMember member in roster) member.Contribution += share;

            club.Funds += spec.RewardFunds;
            row.ClaimedAtUtc = clock.UtcNow;
            row.UpdatedAtUtc = clock.UtcNow;

            return ActionResult.Success("🏁 任务已结算",
                $"{spec.Name} · 社团基金 +{GameplayRules.Money(spec.RewardFunds)}",
                [
                    $"- 全员各记 {GameplayRules.Money(share)} 点社团贡献（共 {members} 人）",
                    $"- 社团基金：{GameplayRules.Money(club.Funds)}",
                    "- 基金可以在 /社团商店 换成钻石与突破材料"
                ], ["/社团任务", "/社团商店", "/我的社团"]);
        }, cancellationToken);

    /// <summary>
    /// 推进某一类任务进度（同一社团同一周期同一任务只有一行）。
    /// <para>在捐献 / 加入 / 提交物资的写路径里调用，返回值是**截断后**的进度。</para>
    /// </summary>
    private async Task<int> AdvanceTaskAsync(BotDbContext db, long clubId, ClubTaskKind kind, int amount, CancellationToken ct)
    {
        ClubTaskSpec? spec = ClubRules.Tasks.FirstOrDefault(x => x.Kind == kind);
        if (spec is null || amount <= 0) return 0;

        string period = GameplayRules.ClubWeek(clock.UtcNow);
        ClubTaskProgress? row = await db.ClubTaskProgresses.FirstOrDefaultAsync(
            x => x.ClubId == clubId && x.Period == period && x.Key == spec.Key, ct);
        if (row is null)
        {
            row = new ClubTaskProgress { ClubId = clubId, Key = spec.Key, Period = period };
            db.ClubTaskProgresses.Add(row);
        }
        if (row.ClaimedAtUtc is not null) return row.Progress;   // 已结算过：不再累计，避免「领了还涨」

        row.Progress = Math.Min(spec.Target, row.Progress + amount);
        row.UpdatedAtUtc = clock.UtcNow;
        return row.Progress;
    }

    /// <summary>任意社员即可的操作（任务列表、商店浏览、市场交易）；返回社团与成员记录。</summary>
    private static async Task<(Club Club, ClubMember Mine, ActionResult? Denied)> RequireMemberAsync(
        BotDbContext db, long userId, CancellationToken ct)
    {
        ClubMember? member = await db.ClubMembers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (member is null)
        {
            return (null!, null!, ActionResult.Fail("你当前没有加入任何社团", "没有执行任何操作",
                "先 /加入社团 或 /创建社团", "/社团列表"));
        }
        Club? club = await db.Clubs.FirstOrDefaultAsync(x => x.Id == member.ClubId, ct);
        if (club is null)
        {
            return (null!, null!, ActionResult.Fail("社团数据缺失", "没有执行任何操作", "稍后重试", "/社团列表"));
        }
        return (club, member, null);
    }
}
