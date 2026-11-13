using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>人物存档的备份与还原。</summary>
public interface ISnapshotService
{
    /// <summary>手动存一份快照。</summary>
    Task<ActionResult> SaveAsync(long userId, string note, CancellationToken cancellationToken = default);

    /// <summary>最近一份快照（含摘要），没有则为 null。</summary>
    Task<SnapshotSummary?> LatestAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>按时间倒序列出该用户的快照摘要。</summary>
    Task<IReadOnlyList<SnapshotSummary>> ListAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>还原到最近一份快照；还原前会自动留底一份 <c>pre-restore</c>。</summary>
    Task<ActionResult> RestoreLatestAsync(long userId, CancellationToken cancellationToken = default);
}

/// <summary>给玩家看的快照摘要（不含整块 payload）。</summary>
public sealed record SnapshotSummary(
    long Id, string Kind, string CreatedOn, string Note,
    double Credits, double Materials, int CharacterCount, int ItemCount)
{
    public bool IsManual => Kind == SnapshotRules.KindManual;
}

/// <summary>快照策略：留几份、每天能还原几次、什么情况下不许还原。</summary>
public static class SnapshotRules
{
    public const string KindManual = "manual";
    public const string KindPreRestore = "pre-restore";

    /// <summary>每人保留的快照上限，超出按时间淘汰最旧的。</summary>
    public const int MaxSnapshotsPerUser = 10;

    /// <summary>每天最多还原次数。还原能抹掉「打输了 / 分解错了」，不限次就等于没有代价。</summary>
    public const int MaxRestoresPerDay = 1;

    /// <summary>
    /// 这些账本原因代表「钱或物品在玩家之间移动过」。只要快照之后发生过，就不允许还原：
    /// 快照里的物品早已给了别人，回滚等于凭空复制一份。
    /// </summary>
    public static readonly IReadOnlyList<string> PlayerTradeReasons =
    [
        "market_sale", "market_buy",
        "offer_sale", "offer_buy",
        "club_market_sale", "club_market_buy",
        "mail_send", "mail_claim"
    ];
}

/// <summary>快照里存的钱包部分（CorePlayerRecord 的全部标量）。</summary>
public sealed record WalletSnapshot(
    double Credits, double Materials, string? MainCharacterGuid,
    int SignInStreak, string SignedOn,
    double ForgePoints, double HorseRacingPoints, double CooperativePoints);

/// <summary>快照里的角色行。<see cref="LocalId"/> 只是小队/训练的旧外键，还原时会重新映射。</summary>
public sealed record CharacterSnapshot(
    long LocalId, string Guid, string CatalogId, int Level, double Exp, int ExLevel,
    int MaxLevel, int LevelBreak, int Promotion, string SnapshotJson);

public sealed record ItemSnapshot(
    string Guid, long EscrowUserId, string CatalogId, int Quality, double Price,
    bool IsLock, bool IsTradable, bool IsBound, int ItemType, int EquipSlotType, int Quantity,
    string SnapshotJson);

/// <summary>小队成员用「角色 Guid」记录，避免依赖自增键。</summary>
public sealed record SquadSnapshot(string Name, bool IsActive, IReadOnlyList<SquadMemberSnapshot> Members);

public sealed record SquadMemberSnapshot(string CharacterGuid, int Slot);

public sealed record QuestSnapshot(string QuestKey, string QuestDate, int Status, string ProgressJson, DateTime? ClaimedAtUtc);

public sealed record SignInSnapshot(string SignInDate, string RewardJson, DateTime CreatedAtUtc);

public sealed record ActivitySnapshot(string ActivityKey, string ProgressJson, string ClaimedJson, DateTime UpdatedAtUtc);

/// <summary>一份快照的完整内容。刻意不含交易/社团/邮件——它们不属于「一个人」。</summary>
public sealed record SnapshotPayload(
    WalletSnapshot Wallet,
    IReadOnlyList<CharacterSnapshot> Characters,
    IReadOnlyList<ItemSnapshot> Items,
    IReadOnlyList<SquadSnapshot> Squads,
    IReadOnlyList<QuestSnapshot> Quests,
    IReadOnlyList<SignInSnapshot> SignIns,
    IReadOnlyList<ActivitySnapshot> Activities);

public sealed class SnapshotService(
    IServiceScopeFactory scopeFactory,
    IGameClock clock,
    ILogger<SnapshotService> logger) : ISnapshotService
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    public async Task<ActionResult> SaveAsync(long userId, string note, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return PlayerService.NoIdentity;
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        if (!await db.CorePlayers.AnyAsync(x => x.UserId == userId, cancellationToken))
        {
            return ActionResult.Fail("你还没有存档", "没有建立快照",
                "先 /注册 建立档案，再回来备份", "/注册");
        }

        SaveSnapshot snapshot = await CaptureAsync(db, userId, SnapshotRules.KindManual, note, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PruneAsync(db, userId, cancellationToken);

        return ActionResult.Success("💾 已备份",
            $"{SnapshotRules.MaxSnapshotsPerUser} 份为上限，最旧的会被挤掉",
            [
                $"- 时间：{snapshot.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
                $"- 金币 {GameplayRules.Money(snapshot.Credits)}　钻石 {GameplayRules.Money(snapshot.Materials)}",
                $"- 角色 {snapshot.CharacterCount} 名　物品 {snapshot.ItemCount} 格",
                note.Length == 0 ? "- 没有写备注（/备份存档 <备注> 可以写一句）" : $"- 备注：{note}",
                "- 还原：/还原存档（每天 1 次）"
            ],
            ["/还原存档", "/状态"]);
    }

    public async Task<SnapshotSummary?> LatestAsync(long userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SnapshotSummary> all = await ListAsync(userId, cancellationToken);
        return all.Count == 0 ? null : all[0];
    }

    public async Task<IReadOnlyList<SnapshotSummary>> ListAsync(
        long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return [];
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        List<SaveSnapshot> rows = await db.SaveSnapshots.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Take(SnapshotRules.MaxSnapshotsPerUser)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new SnapshotSummary(
            x.Id, x.Kind,
            x.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            x.Note, x.Credits, x.Materials, x.CharacterCount, x.ItemCount)).ToArray();
    }

    public async Task<ActionResult> RestoreLatestAsync(
        long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return PlayerService.NoIdentity;
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        SaveSnapshot? target = await db.SaveSnapshots
            .Where(x => x.UserId == userId && x.Kind == SnapshotRules.KindManual)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return ActionResult.Fail("没有可还原的存档", "没有改动任何数据",
                "先用 /备份存档 存一份；还原只认手动备份，不认还原前自动留底的那份", "/备份存档");
        }

        string today = clock.Today();
        int restoresToday = await db.SaveSnapshots.CountAsync(
            x => x.UserId == userId && x.Kind == SnapshotRules.KindPreRestore && x.GameDay == today,
            cancellationToken);
        if (restoresToday >= SnapshotRules.MaxRestoresPerDay)
        {
            return ActionResult.Fail("今天已经还原过了", "没有改动任何数据",
                $"每天最多还原 {SnapshotRules.MaxRestoresPerDay} 次（防止用它刷物品），明天再来",
                "/状态");
        }

        string? trade = await FirstPlayerTradeAsync(db, userId, target.CreatedAtUtc, cancellationToken);
        if (trade is not null)
        {
            return ActionResult.Fail("快照之后有过玩家间交易，不能还原", "没有改动任何数据",
                $"检测到 {trade}。那笔交易里的物品已经不在你名下，"
                + "回滚会让同一件东西出现在两个人手里，所以被拦下了；需要处理请联系管理员", "/状态");
        }

        SnapshotPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SnapshotPayload>(target.PayloadJson, PayloadOptions);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "快照 {SnapshotId} 的 payload 无法解析", target.Id);
            payload = null;
        }
        if (payload is null)
        {
            return ActionResult.Fail("这份快照已损坏", "没有改动任何数据",
                "换一份更新的备份，或联系管理员", "/备份存档");
        }

        // 还原前先留底：万一「还原」本身点错了，还能回到还原前的状态。
        await CaptureAsync(db, userId, SnapshotRules.KindPreRestore, $"还原到 {target.Id} 之前", cancellationToken);

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        // ---- 清掉当前人物状态 ----
        await db.TrainingSessions.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        List<long> squadIds = await db.Squads.Where(x => x.UserId == userId).Select(x => x.Id)
            .ToListAsync(cancellationToken);
        await db.SquadMembers.Where(x => squadIds.Contains(x.SquadId)).ExecuteDeleteAsync(cancellationToken);
        await db.Squads.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.DailyQuests.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.SignInRecords.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ActivityProgress.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.CoreItems.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.CoreCharacters.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // ---- 按快照重建 ----
        foreach (CharacterSnapshot row in payload.Characters)
        {
            db.CoreCharacters.Add(new CoreCharacterRecord
            {
                Guid = row.Guid, UserId = userId, CatalogId = row.CatalogId,
                Level = row.Level, Exp = row.Exp, ExLevel = row.ExLevel, MaxLevel = row.MaxLevel,
                LevelBreak = row.LevelBreak, Promotion = row.Promotion,
                SnapshotJson = row.SnapshotJson, UpdatedAtUtc = clock.UtcNow
            });
        }
        // 先落库拿到新的自增键：小队成员用 Guid 重新对，旧 LocalId 不再有意义。
        await db.SaveChangesAsync(cancellationToken);

        foreach (ItemSnapshot row in payload.Items)
        {
            db.CoreItems.Add(new CoreItemRecord
            {
                Guid = row.Guid, UserId = userId, EscrowUserId = row.EscrowUserId,
                CatalogId = row.CatalogId, Quality = row.Quality, Price = row.Price,
                IsLock = row.IsLock, IsTradable = row.IsTradable, IsBound = row.IsBound,
                ItemType = row.ItemType, EquipSlotType = row.EquipSlotType, Quantity = row.Quantity,
                SnapshotJson = row.SnapshotJson, UpdatedAtUtc = clock.UtcNow
            });
        }

        Dictionary<string, long> guidToLocal = await db.CoreCharacters
            .Where(x => x.UserId == userId)
            .ToDictionaryAsync(x => x.Guid, x => x.LocalId, cancellationToken);
        foreach (SquadSnapshot squad in payload.Squads)
        {
            Squad row = new() { UserId = userId, Name = squad.Name, IsActive = squad.IsActive };
            db.Squads.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            foreach (SquadMemberSnapshot member in squad.Members)
            {
                if (!guidToLocal.TryGetValue(member.CharacterGuid, out long localId)) continue;
                db.SquadMembers.Add(new SquadMember { SquadId = row.Id, CharacterId = localId, Slot = member.Slot });
            }
        }

        foreach (QuestSnapshot row in payload.Quests)
        {
            db.DailyQuests.Add(new DailyQuest
            {
                UserId = userId, QuestKey = row.QuestKey, QuestDate = row.QuestDate,
                Status = row.Status, ProgressJson = row.ProgressJson, ClaimedAtUtc = row.ClaimedAtUtc
            });
        }
        foreach (SignInSnapshot row in payload.SignIns)
        {
            db.SignInRecords.Add(new SignInRecord
            {
                UserId = userId, SignInDate = row.SignInDate,
                RewardJson = row.RewardJson, CreatedAtUtc = row.CreatedAtUtc
            });
        }
        foreach (ActivitySnapshot row in payload.Activities)
        {
            db.ActivityProgress.Add(new ActivityProgress
            {
                UserId = userId, ActivityKey = row.ActivityKey,
                ProgressJson = row.ProgressJson, ClaimedJson = row.ClaimedJson, UpdatedAtUtc = row.UpdatedAtUtc
            });
        }

        CorePlayerRecord? wallet = await db.CorePlayers.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (wallet is null)
        {
            wallet = new CorePlayerRecord { UserId = userId };
            db.CorePlayers.Add(wallet);
        }
        ApplyWallet(wallet, payload.Wallet, clock.UtcNow);

        // 还原动作本身不另建记录行：上面那份 pre-restore 快照（GameDay=今天）就是「今天还原过一次」的凭据。
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await PruneAsync(db, userId, cancellationToken);

        logger.LogInformation("用户 {UserId} 还原到快照 {SnapshotId}（{CreatedAt:u}）",
            userId, target.Id, target.CreatedAtUtc);

        return ActionResult.Success("♻️ 已还原",
            $"回到 {target.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} 的状态",
            [
                $"- 金币 {GameplayRules.Money(payload.Wallet.Credits)}　钻石 {GameplayRules.Money(payload.Wallet.Materials)}",
                $"- 角色 {payload.Characters.Count} 名　物品 {payload.Items.Count} 格　小队 {payload.Squads.Count} 队",
                "- 市场挂单、报价、私信、社团与在途探索/锻造**没有**回滚（它们是跨玩家的）",
                "- 还原前的状态已自动留底，可用 /还原存档 再切回去（受每天 1 次限制）"
            ],
            ["/状态", "/背包", "/备份存档"]);
    }

    /// <summary>把当前人物状态序列化成一条快照（不 SaveChanges）。</summary>
    private async Task<SaveSnapshot> CaptureAsync(
        BotDbContext db, long userId, string kind, string note, CancellationToken cancellationToken)
    {
        CorePlayerRecord wallet = await db.CorePlayers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken) ?? new CorePlayerRecord { UserId = userId };

        List<CoreCharacterRecord> characters = await db.CoreCharacters.AsNoTracking()
            .Where(x => x.UserId == userId).OrderBy(x => x.LocalId).ToListAsync(cancellationToken);
        List<CoreItemRecord> items = await db.CoreItems.AsNoTracking()
            .Where(x => x.UserId == userId).OrderBy(x => x.LocalId).ToListAsync(cancellationToken);

        Dictionary<long, string> guidByLocal = characters.ToDictionary(x => x.LocalId, x => x.Guid);
        List<Squad> squads = await db.Squads.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        List<long> squadIds = squads.Select(x => x.Id).ToList();
        List<SquadMember> members = await db.SquadMembers.AsNoTracking()
            .Where(x => squadIds.Contains(x.SquadId)).OrderBy(x => x.Slot).ToListAsync(cancellationToken);

        SnapshotPayload payload = new(
            new WalletSnapshot(wallet.Credits, wallet.Materials, wallet.MainCharacterGuid,
                wallet.SignInStreak, wallet.SignedOn,
                wallet.ForgePoints, wallet.HorseRacingPoints, wallet.CooperativePoints),
            characters.Select(x => new CharacterSnapshot(
                x.LocalId, x.Guid, x.CatalogId, x.Level, x.Exp, x.ExLevel,
                x.MaxLevel, x.LevelBreak, x.Promotion, x.SnapshotJson)).ToArray(),
            items.Select(x => new ItemSnapshot(
                x.Guid, x.EscrowUserId, x.CatalogId, x.Quality, x.Price,
                x.IsLock, x.IsTradable, x.IsBound, x.ItemType, x.EquipSlotType, x.Quantity,
                x.SnapshotJson)).ToArray(),
            squads.Select(s => new SquadSnapshot(s.Name, s.IsActive,
                members.Where(m => m.SquadId == s.Id)
                    .Select(m => new SquadMemberSnapshot(
                        guidByLocal.TryGetValue(m.CharacterId, out string? guid) ? guid : "", m.Slot))
                    .Where(m => m.CharacterGuid.Length > 0).ToArray())).ToArray(),
            (await db.DailyQuests.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => new QuestSnapshot(x.QuestKey, x.QuestDate, x.Status, x.ProgressJson, x.ClaimedAtUtc)).ToArray(),
            (await db.SignInRecords.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => new SignInSnapshot(x.SignInDate, x.RewardJson, x.CreatedAtUtc)).ToArray(),
            (await db.ActivityProgress.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => new ActivitySnapshot(x.ActivityKey, x.ProgressJson, x.ClaimedJson, x.UpdatedAtUtc)).ToArray());

        SaveSnapshot snapshot = new()
        {
            UserId = userId,
            Kind = kind,
            GameDay = clock.Today(),
            CreatedAtUtc = clock.UtcNow,
            Note = note,
            Credits = wallet.Credits,
            Materials = wallet.Materials,
            CharacterCount = characters.Count,
            ItemCount = items.Count,
            PayloadJson = JsonSerializer.Serialize(payload, PayloadOptions)
        };
        db.SaveSnapshots.Add(snapshot);
        return snapshot;
    }

    /// <summary>只保留最近的若干份；按时间淘汰，避免快照把库撑大。</summary>
    private async Task PruneAsync(BotDbContext db, long userId, CancellationToken cancellationToken)
    {
        List<long> stale = await db.SaveSnapshots
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip(SnapshotRules.MaxSnapshotsPerUser)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0) return;

        await db.SaveSnapshots.Where(x => stale.Contains(x.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>快照之后是否有过玩家间交易；有就返回第一条的原因，没有返回 null。</summary>
    private static async Task<string?> FirstPlayerTradeAsync(
        BotDbContext db, long userId, DateTime since, CancellationToken cancellationToken)
    {
        return await db.LedgerEntries.AsNoTracking()
            .Where(x => x.UserId == userId && x.CreatedAtUtc > since
                && SnapshotRules.PlayerTradeReasons.Contains(x.Reason))
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Reason)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void ApplyWallet(CorePlayerRecord row, WalletSnapshot wallet, DateTime now)
    {
        row.Credits = wallet.Credits;
        row.Materials = wallet.Materials;
        row.MainCharacterGuid = wallet.MainCharacterGuid;
        row.SignInStreak = wallet.SignInStreak;
        row.SignedOn = wallet.SignedOn;
        row.ForgePoints = wallet.ForgePoints;
        row.HorseRacingPoints = wallet.HorseRacingPoints;
        row.CooperativePoints = wallet.CooperativePoints;
        row.UpdatedAtUtc = now;
    }
}
