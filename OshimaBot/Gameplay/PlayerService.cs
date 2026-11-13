using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Game.Services;

namespace OshimaBot.Gameplay;

public interface IPlayerService
{
    /// <summary>服务端认定的今天（Asia/Shanghai 游戏日），命令层显示日期时用它而不是系统时间。</summary>
    string TodayLabel { get; }

    Task<PlayerSnapshot?> GetSnapshotAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> RegisterAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> SignInAsync(long userId, CancellationToken cancellationToken = default);
    /// <param name="useMaterial">true = 钻石抽卡（5/次），false = 金币抽卡（1000/次）；旧版是两条独立通道。</param>
    Task<ActionResult> GachaAsync(long userId, int count, bool useMaterial, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BagEntry>> GetBagAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>库存搜索：按名称或描述匹配关键词，返回结果保留 /背包 的 @编号。</summary>
    Task<IReadOnlyList<BagSearchEntry>> SearchBagAsync(long userId, string keyword,
        CancellationToken cancellationToken = default);

    Task<ActionResult> UseAsync(long userId, int index, int quantity, CancellationToken cancellationToken = default);
    Task<ActionResult> SalvageAsync(long userId, int index, int quantity, CancellationToken cancellationToken = default);

    /// <summary>按品质档位（0–6，对应 White…Gold）批量分解背包里可分解的物品。</summary>
    Task<ActionResult> SalvageByQualityAsync(long userId, int qualityIndex,
        CancellationToken cancellationToken = default);

    /// <summary>钻石兑换金币：10 钻一档，每档 2000 金币（旧版 ExchangeCredits）。</summary>
    Task<ActionResult> ExchangeCreditsAsync(long userId, int materials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 把魔法卡打进魔法卡包。<paramref name="packIndex"/> 为 0 时作用于主战角色已装备的卡包。
    /// </summary>
    Task<ActionResult> UseMagicCardAsync(long userId, int cardIndex, int packIndex,
        CancellationToken cancellationToken = default);

    /// <summary>3 张魔法卡合成一个魔法卡包。</summary>
    Task<ActionResult> CraftMagicCardPackAsync(long userId, int first, int second, int third,
        CancellationToken cancellationToken = default);

    Task<ActionResult> SetLockAsync(long userId, int index, bool locked, CancellationToken cancellationToken = default);
    Task<ActionResult> EquipAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> UnequipAsync(long userId, string slot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterEntry>> GetCharactersAsync(long userId, CancellationToken cancellationToken = default);
    /// <param name="preview">true 只预览需求，不扣任何材料。</param>
    Task<ActionResult> UpgradeNormalAttackAsync(long userId, int index, bool preview,
        CancellationToken cancellationToken = default);

    /// <param name="skillName">技能名（角色技能表里按名字匹配）。</param>
    Task<ActionResult> UpgradeSkillAsync(long userId, int index, string skillName, bool preview,
        CancellationToken cancellationToken = default);

    /// <summary>角色改名：提交审核队列，管理员批准后才生效。</summary>
    Task<ActionResult> RenameAsync(long userId, string newName, CancellationToken cancellationToken = default);

    /// <summary>自己还没被处理的改名申请。</summary>
    Task<IReadOnlyList<RenameRequestEntry>> MyRenameRequestsAsync(long userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 自建角色重随：预览（扣 <see cref="GameplayRules.CustomRerollCost"/> 钻摇一组新属性）。
    /// <para>只对建档即送的自建角色生效——内容包角色的属性由模板决定，没有可重摇的部分。</para>
    /// </summary>
    Task<ActionResult> RerollPreviewAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>确认重随：把预览属性写到自建角色上并回满状态。</summary>
    Task<ActionResult> RerollConfirmAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>取消重随：丢弃预览（钻石不退，同旧版）。</summary>
    Task<ActionResult> RerollCancelAsync(long userId, CancellationToken cancellationToken = default);

    Task<ActionResult> SetMainAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> LevelUpAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> BreakAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> TrainStartAsync(long userId, int index, int minutes, CancellationToken cancellationToken = default);
    Task<ActionResult> TrainClaimAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> SquadAsync(long userId, string operation, int index, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuestEntry>> GetQuestsAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> ClaimQuestAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> RecordSimulationAsync(long userId, CancellationToken cancellationToken = default);
    Task<string> GetNoticeAsync(CancellationToken cancellationToken = default);
    Task<string> GetFortuneAsync(long userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// P1 玩法仓储：所有「改货币 + 改物品 + 写 ledger」在一个 SQLite 事务内完成。
/// singleton（与 CommandCatalog 生命周期一致），每次操作自己开 scope；userId&lt;=0 一律拒绝。
/// </summary>
public sealed class PlayerService(
    IServiceScopeFactory scopeFactory,
    ILogger<PlayerService> logger,
    IGameClock clock,
    IRandomSource random) : IPlayerService
{
    public string TodayLabel => clock.Today();

    internal static ActionResult NoIdentity => ActionResult.Fail(
        "身份尚未就绪", "没有读写任何存档", "请通过 QQ 消息触发注册，或稍后重试", "/状态");

    // ---------------- 读 ----------------

    private async Task<T> ReadAsync<T>(
        Func<BotDbContext, CancellationToken, Task<T>> body, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return await body(db, cancellationToken);
    }

    public Task<PlayerSnapshot?> GetSnapshotAsync(long userId, CancellationToken cancellationToken = default)
        => userId <= 0 ? Task.FromResult<PlayerSnapshot?>(null) : ReadAsync<PlayerSnapshot?>(async (db, ct) =>
        {
            PlayerAccount state = await PlayerService.EnsureAsync(db, userId, ct);
            List<PlayerCharacter> characters = await CharacterStore.ListAsync(
                db, userId, state.Wallet.MainCharacterGuid, ct);
            int itemCount = await db.CoreItems.AsNoTracking().CountAsync(x => x.UserId == userId, ct);
            List<long> squadIds = await db.Squads.AsNoTracking()
                .Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(ct);
            int squadCount = squadIds.Count == 0 ? 0 : await db.SquadMembers.AsNoTracking()
                .CountAsync(x => squadIds.Contains(x.SquadId), ct);
            int pendingTrainings = await db.TrainingSessions.AsNoTracking()
                .CountAsync(x => x.UserId == userId && x.SettledAtUtc == null, ct);
            PlayerCharacter? main = characters.FirstOrDefault(x => x.IsMain) ?? characters.FirstOrDefault();
            return new PlayerSnapshot(userId, state.Credits, state.Materials,
                state.SignInStreak, state.SignedOn, main?.Name, main?.Level ?? 0, main?.Exp ?? 0,
                characters.Count, itemCount, squadCount, pendingTrainings);
        }, cancellationToken);

    public Task<IReadOnlyList<BagEntry>> GetBagAsync(long userId, CancellationToken cancellationToken = default)
        => userId <= 0 ? Task.FromResult<IReadOnlyList<BagEntry>>([]) : ReadAsync(async (db, ct) =>
        {
            await PlayerService.EnsureAsync(db, userId, ct);
            List<PlayerItem> items = await ItemStore.ListAsync(db, userId, ct);
            // 装备直接进角色快照（6 槽），背包里只剩下未装备的物品。
            return (IReadOnlyList<BagEntry>)items.Select((item, i) => new BagEntry(
                i + 1, item.Id, ReadName(item), item.CatalogId, item.Quantity,
                item.Quality, item.Locked, item.Bound, false)).ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<BagSearchEntry>> SearchBagAsync(long userId, string keyword,
        CancellationToken cancellationToken = default)
        => userId <= 0
            ? Task.FromResult<IReadOnlyList<BagSearchEntry>>([])
            : ReadAsync(async (db, ct) =>
            {
                await PlayerService.EnsureAsync(db, userId, ct);
                List<PlayerItem> items = await ItemStore.ListAsync(db, userId, ct);
                List<BagSearchEntry> result = [];
                for (int i = 0; i < items.Count; i++)
                {
                    PlayerItem item = items[i];
                    string name = ReadName(item);
                    string description = item.Item.Description ?? "";
                    // 旧版两条入口：库存搜索2 只比名称，库存搜索 连描述一起比。这里合并为后者（前者是它的子集）。
                    if (!name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        && !description.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(new BagSearchEntry(
                        i + 1, item.Id, name, Snippet(description), item.Quantity, item.Quality, item.Locked));
                }
                return (IReadOnlyList<BagSearchEntry>)result;
            }, cancellationToken);

    public Task<IReadOnlyList<CharacterEntry>> GetCharactersAsync(long userId, CancellationToken cancellationToken = default)
        => userId <= 0 ? Task.FromResult<IReadOnlyList<CharacterEntry>>([]) : ReadAsync(async (db, ct) =>
        {
            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, ct);
            List<PlayerCharacter> characters = await CharacterStore.ListAsync(
                db, userId, account.Wallet.MainCharacterGuid, ct);
            return (IReadOnlyList<CharacterEntry>)characters.Select((c, i) => new CharacterEntry(
                i + 1, c.Id, c.Name, c.Level, c.Exp, c.BreakLevel, c.IsMain,
                CountEquipped(c.Core))).ToArray();
        }, cancellationToken);

    public async Task<IReadOnlyList<QuestEntry>> GetQuestsAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return [];
        string date = clock.Today();
        return await ReadAsync(async (db, ct) =>
        {
            Dictionary<string, DailyQuest> rows = await db.DailyQuests.AsNoTracking()
                .Where(x => x.UserId == userId && x.QuestDate == date)
                .ToDictionaryAsync(x => x.QuestKey, ct);
            List<QuestEntry> result = [];
            for (int i = 0; i < GameplayRules.DailyQuests.Count; i++)
            {
                (string key, string name, int target) = GameplayRules.DailyQuests[i];
                rows.TryGetValue(key, out DailyQuest? row);
                result.Add(new QuestEntry(i + 1, key, name, row?.Status ?? 0,
                    row is null ? 0 : ReadProgress(row), target));
            }
            return (IReadOnlyList<QuestEntry>)result;
        }, cancellationToken);
    }

    public Task<string> GetNoticeAsync(CancellationToken cancellationToken = default)
        => ReadAsync(async (db, ct) =>
        {
            AppConfig? config = await db.AppConfigs.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == "notice", ct);
            if (config is null) return GameplayRules.DefaultNotice;
            try
            {
                using JsonDocument document = JsonDocument.Parse(config.ValueJson);
                return document.RootElement.TryGetProperty("text", out JsonElement text)
                    ? text.GetString() ?? GameplayRules.DefaultNotice
                    : GameplayRules.DefaultNotice;
            }
            catch (JsonException) { return GameplayRules.DefaultNotice; }
        }, cancellationToken);

    public Task<string> GetFortuneAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult(userId <= 0
            ? "身份尚未就绪，无法占卜。"
            : GameplayRules.Fortune(userId, clock.Today()));

    // ---------------- 写（事务） ----------------

    private async Task<ActionResult> MutateAsync(
        long userId,
        Func<BotDbContext, PlayerAccount, CancellationToken, Task<ActionResult>> body,
        CancellationToken cancellationToken)
    {
        if (userId <= 0) return NoIdentity;
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);

            PlayerAccount account = await PlayerService.EnsureAsync(db, userId, cancellationToken);
            ActionResult result = await body(db, account, cancellationToken);
            if (!result.Ok)
            {
                await transaction.RollbackAsync(cancellationToken);
                return result;
            }
            account.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActionResult.Fail("存档正被另一条消息修改", "本次操作已回滚", "稍等一秒后重试", "/状态");
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "玩法写入冲突：UserId={UserId}", userId);
            return ActionResult.Fail("存档写入冲突", "本次操作已回滚", "稍后重试", "/状态");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "玩法操作失败：UserId={UserId}", userId);
            return ActionResult.Fail("玩法操作出现异常", "本次操作已回滚", "稍后重试，或输入 /帮助", "/帮助");
        }
    }

    public Task<ActionResult> RegisterAsync(long userId, CancellationToken cancellationToken = default)
        => userId <= 0 ? Task.FromResult(NoIdentity) : ReadAsync<ActionResult>(async (db, ct) =>
        {
            // 读路径自己判断是否为首次建档，避免 EnsureAsync 已建档后误报「存档已存在」。
            bool existed = await db.CorePlayers.AsNoTracking().AnyAsync(x => x.UserId == userId, ct);
            PlayerAccount state = await PlayerService.EnsureAsync(db, userId, ct);
            return ActionResult.Success("🎒 旅人登记",
                existed ? "存档已存在，未重复发放初始资源" : "初始资源已发放",
                [
                    "- 等级与经验记在角色身上（Core 模型）",
                    $"- 金币：{state.Credits}　钻石：{state.Materials}",
                    $"- 角色：{await db.CoreCharacters.CountAsync(x => x.UserId == userId, ct)} 名",
                    $"- 物品：{await db.CoreItems.CountAsync(x => x.UserId == userId, ct)} 格",
                    "- 旧版 JSON 存档未迁移，所有旅人从今天重新开始"
                ], ["/状态", "/签到", "/抽卡", "/背包"]);
        }, cancellationToken);

    public Task<ActionResult> SignInAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            string today = clock.Today();
            if (string.Equals(state.SignedOn, today, StringComparison.Ordinal))
            {
                return ActionResult.Fail("今天已经签到过了", "没有重复发放奖励",
                    "明天再来，或先完成每日任务", "/每日任务");
            }
            bool continuous = string.Equals(state.SignedOn,
                DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    .AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
            state.SignInStreak = continuous ? state.SignInStreak + 1 : 1;
            state.SignedOn = today;

            // 旧版 GetSignInResult：金币 Next(1000,3000)+10×已连续天数、材料 Next(5,15)+天数/7
            (long credits, int materials) = GameplayRules.SignInReward(random, state.SignInStreak - 1);
            ApplyCurrency(db, state, credits, "credits", "signin", "signin", today);
            if (materials > 0)
            {
                ApplyCurrency(db, state, materials, "diamonds", "signin", "signin", today);
            }
            db.SignInRecords.Add(new SignInRecord
            {
                UserId = userId, SignInDate = today,
                RewardJson = JsonSerializer.Serialize(new { credits, materials }),
                CreatedAtUtc = DateTime.UtcNow
            });
            await AdvanceQuestAsync(db, userId, "signin", ct);

            // 旧版 GetSignInResult 还会额外发一件东西，品质随连续天数上浮（+3%/天，封顶 +30%）。
            string extra = await GrantSignInExtraAsync(db, userId, state.SignInStreak - 1, ct);
            List<string> lines =
            [
                $"- 当前金币：{state.Credits}　钻石：{state.Materials}",
                "- 金币 1000–2999、钻石 5–14，连续天数越高加成越多",
                extra.Length == 0 ? "- 本次没有额外掉落" : $"- 额外获得：{extra}",
                "- 今日任务「今日签到」进度已更新"
            ];
            return ActionResult.Success("📅 签到成功",
                $"连续 {state.SignInStreak} 天 · 金币 +{credits}"
                + (materials > 0 ? $" · 钻石 +{materials}" : "")
                + (extra.Length == 0 ? "" : $" · {extra}"),
                lines, ["/每日任务", "/状态", "/抽卡", "/背包"]);
        }, cancellationToken);

    /// <summary>
    /// 签到的额外掉落（旧版 <c>GetSignInResult</c> 的 <c>switch (Next(6))</c>）：
    /// 1 武器 / 2 防具 / 3 鞋子 / 4 饰品（按 Id 前缀 11–14 取同品质模板）、5 魔法卡、0 魔法卡包；
    /// 橙品以上自动锁定（旧版同为「防止误分解」）。双旦活动期间再送一张十连奖券。
    /// </summary>
    private async Task<string> GrantSignInExtraAsync(
        BotDbContext db, long userId, int streakDays, CancellationToken ct)
    {
        QualityType quality = GameplayRules.QualityTypeOf(GameplayRules.RollSignInQuality(random, streakDays));
        string qualityName = GameplayRules.QualityName(GameplayRules.QualityTierOf(quality));
        Random local = GameplayRules.NewRandom(random);

        Item? item = local.Next(6) switch
        {
            1 => PickEquipment(local, quality, "11"),
            2 => PickEquipment(local, quality, "12"),
            3 => PickEquipment(local, quality, "13"),
            4 => PickEquipment(local, quality, "14"),
            5 => FunGameService.GenerateMagicCard(local, quality),
            _ => FunGameService.GenerateMagicCardPack(local, GameplayRules.MagicCardPackMaxMagics, quality)
        };

        List<string> gained = [];
        if (item is not null)
        {
            item.IsLock = item.QualityType >= QualityType.Orange;   // 旧版：橙以上不能被误分解
            ItemStore.AddSnapshot(db, userId, item, $"signin-{item.ItemType.ToString().ToLowerInvariant()}");
            gained.Add($"{qualityName}{ItemSet.GetItemTypeName(item.ItemType)}【{item.Name}】");
        }

        // 旧版：双旦活动进行中，签到再送一张十连奖券。
        (DateTime? start, DateTime? end) = ActivityCatalog.Window(ActivityWindow.WinterFestival, clock.UtcNow);
        if (ActivityCatalog.Phase((start, end), clock.UtcNow) == ActivityPhase.InProgress)
        {
            await AddItemAsync(db, userId, "十连奖券", 1, 1, ct, "18009");
            gained.Add("十连奖券 ×1（双旦活动）");
        }
        return string.Join("、", gained);
    }

    /// <summary>按 Id 前缀（11 武器 / 12 防具 / 13 鞋子 / 14 饰品）与品质取一件装备副本。</summary>
    private static Item? PickEquipment(Random random, QualityType quality, string idPrefix)
    {
        List<Item> pool = [.. FunGameService.AllItems
            .Where(i => i.Id.ToString(CultureInfo.InvariantCulture).StartsWith(idPrefix, StringComparison.Ordinal)
                        && i.QualityType == quality)];
        if (pool.Count == 0) return null;
        return pool[random.Next(pool.Count)].Copy();
    }

    public Task<ActionResult> GachaAsync(long userId, int count, bool useMaterial,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            int pulls = Math.Clamp(count, 1, GameplayRules.MaxGachaPerCall);
            string currencyName = useMaterial ? "钻石" : "金币";
            int unitCost = useMaterial ? GameplayRules.GachaCost : GameplayRules.GachaCostCredits;
            int cost = unitCost * pulls;         // 旧版十连按 10 倍直乘，没有折扣
            double balance = useMaterial ? state.Materials : state.Credits;
            if (balance < cost)
            {
                return ActionResult.Fail($"{currencyName}不足", $"没有扣除{currencyName}，也没有生成物品",
                    $"需要 {cost} {currencyName}，当前 {balance:0.##}；可先 /签到 或 /领奖",
                    useMaterial ? "/签到" : "/签到");
            }
            ApplyCurrency(db, state, -cost, useMaterial ? "diamonds" : "credits",
                "gacha", "gacha", clock.Today());

            List<string> lines = [];
            int hits = 0;
            for (int i = 0; i < pulls; i++)
            {
                // 旧版：每次独立掷 NextDouble()，> 0.8 才中奖；十连无保底、可能全空。
                if (!GameplayRules.RollGachaHit(random)) continue;
                hits++;
                string detail = await DrawGachaOnceAsync(db, state, ct);
                lines.Add($"- {lines.Count + 1}. {detail}");
            }
            await AdvanceQuestAsync(db, userId, "gacha", ct);

            if (hits == 0) lines.Add($"- {pulls} 次都没抽中，什么也没得到");
            lines.Add($"- 消耗{currencyName} {cost}，剩余 {(useMaterial ? state.Materials : state.Credits):0.##}（命中 {hits}/{pulls}）");
            lines.Add("- 用 /背包 查看物品编号后再 @编号 使用或分解");
            return ActionResult.Success(pulls == 10 ? "🎰 十连抽卡" : "🎲 抽卡",
                hits == 0 ? "什么也没抽中" : $"抽中 {hits} 件 · 已放入背包",
                lines, ["/背包", "/状态", "/抽卡", "/钻石抽卡"]);
        }, cancellationToken);

    /// <summary>
    /// 抽卡单次结算（旧版 <c>GetDrawCardResult</c>）：先掷品质，再掷 8 类产出之一。
    /// 装备类按「该品质的同类型装备」挑，找不到则放宽到任意品质，避免旧版那种空数组越界。
    /// </summary>
    private async Task<string> DrawGachaOnceAsync(BotDbContext db, PlayerAccount state, CancellationToken ct)
    {
        int quality = GameplayRules.RollGachaQuality(random);
        GameplayRules.GachaKind kind = GameplayRules.RollGachaKind(random);
        string qualityName = GameplayRules.QualityName(quality);

        if (kind == GameplayRules.GachaKind.Character)
        {
            List<FunGame.Core.Entity.Character> characters = FunGameService.Characters;
            if (characters.Count == 0) return $"{qualityName} · 角色（内容包为空）";

            FunGame.Core.Entity.Character template = characters[random.Next(characters.Count)];
            string catalogId = template.Id.ToString(CultureInfo.InvariantCulture);
            bool owned = await db.CoreCharacters.AnyAsync(
                x => x.UserId == state.UserId && x.CatalogId == catalogId, ct);
            if (owned)
            {
                ApplyCurrency(db, state, GameplayRules.GachaDuplicateCharacterMaterials,
                    "diamonds", "gacha_duplicate", "character", catalogId);
                return $"重复角色【{template.GetName()}】→ 钻石 +{GameplayRules.GachaDuplicateCharacterMaterials}";
            }
            PlayerCharacter created = CharacterStore.Create(db, state.UserId, template, catalogId);
            return $"{qualityName} · 角色【{created.Name}】";
        }

        // 魔法卡与魔法卡包在内容包里没有模板——旧版就是按抽到的品质当场生成的，
        // 这里同样走内容包的 GenerateMagicCard / GenerateMagicCardPack（3 张卡合包）。
        if (kind is GameplayRules.GachaKind.MagicCard or GameplayRules.GachaKind.MagicCardPack)
        {
            Random local = GameplayRules.NewRandom(random);
            QualityType rolled = GameplayRules.QualityTypeOf(quality);
            if (kind == GameplayRules.GachaKind.MagicCard)
            {
                Item card = FunGameService.GenerateMagicCard(local, rolled);
                card.IsLock = GameplayRules.AutoLockOnDraw(card.QualityType);   // 旧版橙品以上抽到即锁
                ItemStore.AddSnapshot(db, state.UserId, card, "magic-card");
                return $"{qualityName} · 魔法卡【{card.Name}】{Locked(card)}";
            }
            Item? pack = FunGameService.GenerateMagicCardPack(local, GameplayRules.MagicCardPackMaxMagics, rolled);
            if (pack is null) return $"{qualityName} · 魔法卡包（生成失败）";
            pack.IsLock = GameplayRules.AutoLockOnDraw(pack.QualityType);
            ItemStore.AddSnapshot(db, state.UserId, pack, "magic-card-pack");
            return $"{qualityName} · 魔法卡包【{pack.Name}】{Locked(pack)}";
        }

        ItemType? wanted = kind switch
        {
            GameplayRules.GachaKind.Weapon => ItemType.Weapon,
            GameplayRules.GachaKind.Armor => ItemType.Armor,
            GameplayRules.GachaKind.Shoes => ItemType.Shoes,
            GameplayRules.GachaKind.Accessory => ItemType.Accessory,
            _ => null
        };

        List<Item> pool = FunGameService.AllItems
            .Where(GameplayRules.IsDrawable)
            .Where(i => wanted is null || i.ItemType == wanted)
            .ToList();
        if (pool.Count == 0) pool = FunGameService.AllItems.Where(GameplayRules.IsDrawable).ToList();
        if (pool.Count == 0) return $"{qualityName} · 无名的遗物（内容包为空）";

        List<Item> exact = pool.Where(i => GameplayRules.QualityTierOf(i.QualityType) == quality).ToList();
        List<Item> candidates = exact.Count > 0 ? exact : pool;
        Item pick = candidates[random.Next(candidates.Count)];

        // 物品品质由模板决定（旧版就是按品质筛出模板再挑），不要用掷出的档位覆盖。
        // 旧版抽到橙品以上会直接把这一件锁上（防止误分解）；锁定件不能并进已有堆叠，按快照单独入包。
        if (GameplayRules.AutoLockOnDraw(pick.QualityType))
        {
            Item locked = pick.Copy();
            locked.IsLock = true;
            ItemStore.AddSnapshot(db, state.UserId, locked, pick.Id.ToString(CultureInfo.InvariantCulture));
            return $"{GameplayRules.QualityName(GameplayRules.QualityTierOf(pick.QualityType))} · {pick.Name}{Locked(locked)}";
        }
        await AddItemAsync(db, state.UserId, pick.Name,
            GameplayRules.QualityTierOf(pick.QualityType), 1, ct,
            pick.Id.ToString(CultureInfo.InvariantCulture));
        return $"{GameplayRules.QualityName(GameplayRules.QualityTierOf(pick.QualityType))} · {pick.Name}";
    }

    private static string Locked(Item item) => item.IsLock ? "（已锁定）" : "";

    public Task<ActionResult> UseAsync(long userId, int index, int quantity, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            PlayerItem? item = await ItemByIndexAsync(db, userId, index, ct);
            if (item is null) return NoSuchItem(index);

            string name = ReadName(item);
            if (!ItemUsage.CanUse(item.Item))
            {
                return ActionResult.Fail("这个物品无法使用", "没有消耗物品",
                    $"{name} 不能直接使用；装备类请用 /装备 @编号", "/背包");
            }

            int amount = Math.Clamp(quantity, 1, item.Quantity);
            PlayerCharacter? main = await MainCharacterAsync(db, userId, ct);
            List<PlayerCharacter> targets = main is null ? [] : [main];

            // 物品行为由内容包定义（经验书 / 回复药 / 礼包），这里只是装配 Core 的 User 并调用 UseItem。
            ItemUsage.Result used = ItemUsage.Use(state, item, amount, targets);
            if (!used.Used)
            {
                return ActionResult.Fail("使用失败", "没有消耗物品",
                    string.IsNullOrWhiteSpace(used.Message) ? $"{name} 无法使用" : used.Message, "/背包");
            }

            string itemRef = item.Id.ToString(CultureInfo.InvariantCulture);
            if (used.CreditDelta != 0)
            {
                ApplyCurrency(db, state, used.CreditDelta, "credits", "item_use", "item", itemRef);
            }
            if (used.MaterialDelta != 0)
            {
                ApplyCurrency(db, state, used.MaterialDelta, "diamonds", "item_use", "item", itemRef);
            }

            List<string> lines = [];
            foreach (PlayerCharacter target in targets)
            {
                double gained = used.ExpGained.GetValueOrDefault(target.Core);
                int beforeLevel = target.Level;
                target.Core.OnLevelUp();              // Core 模型：经验够了立即升级
                CharacterStore.Sync(target);
                int levels = target.Level - beforeLevel;
                lines.Add(gained > 0
                    ? $"- {target.Name} 经验 +{gained:0.##} → Lv.{target.Level}"
                        + (levels > 0 ? $"（升 {levels} 级）" : $"（还需 {NeedExp(target)}）")
                    : $"- {target.Name} 当前 Lv.{target.Level}");
            }
            if (lines.Count == 0) lines.Add("- 没有可作用的角色");
            lines.Add($"- 物品：{name} ×{amount} 已扣除");
            if (!string.IsNullOrWhiteSpace(used.Message)) lines.Insert(0, $"- {used.Message}");

            Consume(db, item, amount);
            return ActionResult.Success("🧪 使用物品", $"{name} ×{amount}", lines,
                ["/角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> SalvageAsync(long userId, int index, int quantity, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            PlayerItem? item = await ItemByIndexAsync(db, userId, index, ct);
            if (item is null) return NoSuchItem(index);
            if (item.Locked || item.Bound)
            {
                return ActionResult.Fail("该物品已锁定或绑定", "没有分解任何物品",
                    "先用 /解锁 @编号，绑定装备需先 /卸下", "/背包");
            }
            int amount = Math.Clamp(quantity, 1, item.Quantity);
            string name = ReadName(item);
            // 旧版 DecomposedMaterials：按品质返还固定材料（1/3/8/16/32/64/128），进 Materials 不是金币
            long materials = GameplayRules.SalvageMaterials(item.Quality, amount);
            string qualityName = GameplayRules.QualityName(item.Quality);
            ApplyCurrency(db, state, materials, "diamonds", "salvage", "item", item.Id.ToString(CultureInfo.InvariantCulture));
            Consume(db, item, amount);
            return ActionResult.Success("♻️ 分解完成",
                $"{name} ×{amount} · 钻石 +{materials}",
                [$"- 品质：{qualityName}　当前钻石：{state.Materials}", "- 已写入账本 ledger，可对账"],
                ["/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> SalvageByQualityAsync(long userId, int qualityIndex,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            // 旧版 DecomposeItem3：品质索引直接用 QualityType 的整数值（White 0 … Gold 6）。
            if (qualityIndex < 0 || qualityIndex > (int)QualityType.Gold)
            {
                return ActionResult.Fail("品质序号无效", "没有分解任何物品",
                    $"可用 0–6：{string.Join(" / ", Enumerable.Range(0, 7).Select(i => $"{i} {GameplayRules.QualityName(i + 1)}"))}",
                    "/背包");
            }
            int tier = qualityIndex + 1;
            string qualityName = GameplayRules.QualityName(tier);
            List<PlayerItem> items = await ItemStore.ListAsync(db, userId, ct);
            // 旧版过滤条件：同品质、未装备、未上锁、未在交易中；本版装备不在背包里，另加「未绑定」。
            List<PlayerItem> targets = [.. items.Where(x => x.Quality == tier && !x.Locked && !x.Bound)];
            int units = targets.Sum(x => x.Quantity);
            if (units == 0)
            {
                return ActionResult.Fail($"背包里没有可分解的{qualityName}物品", "没有分解任何物品",
                    "只有未锁定、未绑定的物品才会被批量分解", "/背包");
            }

            long materials = GameplayRules.SalvageMaterials(tier, units);
            ApplyCurrency(db, state, materials, "diamonds", "salvage_quality", "quality",
                qualityIndex.ToString(CultureInfo.InvariantCulture));
            foreach (PlayerItem item in targets) ItemStore.Remove(db, item);

            List<string> lines =
            [
                $"- 分解 {targets.Count} 种共 {units} 件{qualityName}物品",
                $"- 钻石 +{materials}　当前钻石：{state.Materials:0.##}",
                "- 已写入账本 ledger，可对账"
            ];
            if (targets.Count <= 8)
            {
                lines.InsertRange(0, targets.Select(x => $"- {ReadName(x)} ×{x.Quantity}"));
            }
            return ActionResult.Success("♻️ 品质分解完成", $"{qualityName} ×{units} · 钻石 +{materials}",
                lines, ["/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> ExchangeCreditsAsync(long userId, int materials,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            (int reduce, long reward) = GameplayRules.ExchangePlan(materials);
            if (state.Materials < reduce)
            {
                return ActionResult.Fail("钻石不足", "没有兑换任何金币",
                    $"本次需要 {reduce} 钻（最低 {GameplayRules.ExchangeMaterialUnit} 钻一档），"
                    + $"当前 {state.Materials:0.##} 钻", "/签到");
            }
            string refId = clock.Today();
            ApplyCurrency(db, state, -reduce, "diamonds", "exchange", "exchange", refId);
            ApplyCurrency(db, state, reward, "credits", "exchange", "exchange", refId);
            return ActionResult.Success("💱 兑换完成",
                $"{reduce} 钻 → {reward} 金币",
                [
                    $"- 汇率：{GameplayRules.ExchangeMaterialUnit} 钻 = {GameplayRules.ExchangeCreditsPerUnit} 金币",
                    $"- 当前金币：{state.Credits:0.##}　钻石：{state.Materials:0.##}",
                    "- 不足一档的零头会保留，不会被扣掉"
                ], ["/状态", "/商店"]);
        }, cancellationToken);

    public Task<ActionResult> UseMagicCardAsync(long userId, int cardIndex, int packIndex,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            PlayerItem? card = await ItemByIndexAsync(db, userId, cardIndex, ct);
            if (card is null) return NoSuchItem(cardIndex);
            if (card.Item.ItemType != ItemType.MagicCard)
            {
                return ActionResult.Fail("这个物品不是魔法卡", "没有使用任何魔法卡",
                    $"{ReadName(card)} 是{ItemSet.GetItemTypeName(card.Item.ItemType)}，"
                    + "魔法卡请用 /使用 @编号", "/背包");
            }
            if (card.Locked)
            {
                return ActionResult.Fail("魔法卡已锁定", "没有使用任何魔法卡",
                    $"先 /解锁 @{cardIndex}", "/背包");
            }
            if (card.Item.RemainUseTimes <= 0)
            {
                return ActionResult.Fail("这张魔法卡的次数已用完", "没有使用任何魔法卡",
                    "用 /合成魔法卡 把剩余的卡合成卡包", "/背包");
            }
            if (card.Item.Skills.Active is null)
            {
                return ActionResult.Fail("这张魔法卡没有任何魔法", "没有使用任何魔法卡",
                    "内容包里这张卡是空卡，换一张试试", "/背包");
            }

            // 目标卡包：显式给编号就作用于背包里的卡包，否则作用于主战角色已装备的卡包。
            Item? pack = null;
            PlayerItem? packRow = null;
            PlayerCharacter? main = null;
            if (packIndex > 0)
            {
                packRow = await ItemByIndexAsync(db, userId, packIndex, ct);
                if (packRow is null) return NoSuchItem(packIndex);
                if (packRow.Item.ItemType != ItemType.MagicCardPack)
                {
                    return ActionResult.Fail("目标不是魔法卡包", "没有使用任何魔法卡",
                        $"@{packIndex} 是{ItemSet.GetItemTypeName(packRow.Item.ItemType)}", "/背包");
                }
                pack = packRow.Item;
            }
            else
            {
                main = await MainCharacterAsync(db, userId, ct);
                pack = main?.Core.EquipSlot.MagicCardPack;
                if (pack is null)
                {
                    return ActionResult.Fail("主战角色没有装备魔法卡包", "没有使用任何魔法卡",
                        "先 /装备 @编号 装上卡包，或 /使用魔法卡 @卡编号 @卡包编号 直接指定", "/我的角色");
                }
            }

            if (card.Item.QualityType != pack.QualityType)
            {
                return ActionResult.Fail("品质不一致", "没有使用任何魔法卡",
                    $"魔法卡是{GameplayRules.QualityName(card.Quality)}，"
                    + $"卡包是{GameplayRules.QualityName(GameplayRules.QualityTierOf(pack.QualityType))}；"
                    + "只能对相同品质的魔法卡包使用魔法卡", "/背包");
            }

            Skill magic = card.Item.Skills.Active;
            string packName = string.IsNullOrWhiteSpace(pack.Name) ? "魔法卡包" : pack.Name;
            List<string> lines = [];
            // 用技能 Id 认同一张魔法：旧版按 GetIdName()（Id + 名字）比较，
            // 而叠等级时名字会被追加「 +N」，第二次使用就认不出来、会把同一张魔法重复塞进卡包。
            Skill? existing = pack.Skills.Magics.FirstOrDefault(m => m.Id == magic.Id);
            if (existing is not null && existing.Level < General.GameplayEquilibriumConstant.MaxMagicLevel)
            {
                int cap = General.GameplayEquilibriumConstant.MaxMagicLevel;
                int before = existing.Level;
                existing.Level = Math.Min(cap, before + magic.Level);
                existing.Name = TrimMagicName(existing.Name) + (existing.Level > 1 ? $" +{existing.Level - 1}" : "");
                lines.Add($"- 魔法【{magic.Name}】等级 {before} → {existing.Level}");

                // 旧版这里写了一段「等级溢出补偿 10 钻/级」，但 diff 恒为 0，是死代码；这里按原意实现。
                int overflow = magic.Level - (existing.Level - before);
                if (overflow > 0)
                {
                    int compensation = overflow * GameplayRules.MagicCardLevelCompensationMaterials;
                    ApplyCurrency(db, state, compensation, "diamonds", "magic_card_overflow", "item",
                        card.Id.ToString(CultureInfo.InvariantCulture));
                    lines.Add($"- 技能已满级，溢出 {overflow} 级折算钻石 +{compensation}");
                }
            }
            else if (pack.Skills.Magics.Count < GameplayRules.MagicCardPackMaxMagics)
            {
                Skill copied = magic.Copy();
                copied.Level = magic.Level;
                pack.Skills.Magics.Add(copied);
                lines.Add($"- 魔法【{magic.Name}】已写入卡包（{pack.Skills.Magics.Count}/{GameplayRules.MagicCardPackMaxMagics}）");
            }
            else
            {
                return ActionResult.Fail("卡包里没有可用的位置", "没有使用任何魔法卡",
                    $"魔法【{magic.Name}】在{packName}中不存在或已满级，且卡包已有 "
                    + $"{GameplayRules.MagicCardPackMaxMagics} 个魔法", "/背包");
            }

            RefreshMagicPackDescription(pack);
            card.Item.RemainUseTimes = Math.Max(0, card.Item.RemainUseTimes - 1);
            if (card.Item.RemainUseTimes == 0) ItemStore.Remove(db, card);
            else ItemStore.Sync(card);
            if (packRow is not null) ItemStore.Sync(packRow);
            else if (main is not null) CharacterStore.Sync(main);

            lines.Add($"- {packName}：{DescribeMagicPack(pack)}");
            lines.Add($"- 魔法卡剩余次数：{card.Item.RemainUseTimes}");
            if (packRow is null) lines.Add("- 装备中的卡包需重新 /装备 才会把新魔法带进战斗");
            return ActionResult.Success("🃏 魔法卡已使用", $"{magic.Name} → {packName}", lines,
                ["/背包", "/我的角色"]);
        }, cancellationToken);

    public Task<ActionResult> CraftMagicCardPackAsync(long userId, int first, int second, int third,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            int[] indices = [first, second, third];
            if (indices.Any(x => x <= 0))
            {
                return ActionResult.Fail("需要 3 张魔法卡的编号", "没有合成任何卡包",
                    "用法：/合成魔法卡 @1 @2 @3", "/背包");
            }
            if (indices.Distinct().Count() < 3)
            {
                return ActionResult.Fail("三张魔法卡编号重复", "没有合成任何卡包",
                    "同一张卡不能既当材料又当材料，请选 3 个不同的 @编号", "/背包");
            }

            List<PlayerItem> cards = [];
            foreach (int index in indices)
            {
                PlayerItem? card = await ItemByIndexAsync(db, userId, index, ct);
                if (card is null) return NoSuchItem(index);
                if (card.Locked)
                {
                    return ActionResult.Fail("材料里有已锁定的物品", "没有合成任何卡包",
                        $"先 /解锁 @{index}", "/背包");
                }
                if (card.Item.ItemType != ItemType.MagicCard || card.Item.RemainUseTimes <= 0)
                {
                    return ActionResult.Fail("材料不是可用的魔法卡", "没有合成任何卡包",
                        $"@{index} 不是魔法卡，或次数已用完", "/背包");
                }
                cards.Add(card);
            }

            Item? pack = FunGameService.ConflateMagicCardPack(
                GameplayRules.NewRandom(random), cards.Select(x => x.Item));
            if (pack is null)
            {
                return ActionResult.Fail("合成失败", "没有消耗任何魔法卡",
                    "这三张魔法卡里没有任何魔法，换三张再试", "/背包");
            }
            ItemStore.AddSnapshot(db, userId, pack, "magic-card-pack");
            foreach (PlayerItem card in cards) ItemStore.Remove(db, card);

            string packName = string.IsNullOrWhiteSpace(pack.Name) ? "魔法卡包" : pack.Name;
            return ActionResult.Success("🎴 合成魔法卡包",
                $"{packName}（{GameplayRules.QualityName(GameplayRules.QualityTierOf(pack.QualityType))}）",
                [
                    $"- {DescribeMagicPack(pack)}",
                    $"- 消耗魔法卡：{string.Join("、", cards.Select(ReadName))}",
                    "- 用 /装备 @编号 给主战角色装上，再用 /使用魔法卡 继续强化"
                ], ["/背包", "/我的角色"]);
        }, cancellationToken);

    public Task<ActionResult> SetLockAsync(long userId, int index, bool locked, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            PlayerItem? item = await ItemByIndexAsync(db, userId, index, ct);
            if (item is null) return NoSuchItem(index);
            item.Locked = locked;
            return ActionResult.Success(locked ? "🔒 物品已锁定" : "🔓 物品已解锁",
                $"{ReadName(item)} 现在{(locked ? "不可分解/丢弃" : "可分解")}",
                ["- 锁定状态已写入存档"], ["/背包"]);
        }, cancellationToken);

    public Task<ActionResult> EquipAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            PlayerItem? item = await ItemByIndexAsync(db, userId, index, ct);
            if (item is null) return NoSuchItem(index);
            PlayerCharacter? main = await MainCharacterAsync(db, userId, ct);
            if (main is null) return NoMainCharacter();
            string name = ReadName(item);
            // Core 按 ItemType 决定槽位（Accessory 会自动找空位）；返回 false 表示不可装备。
            if (!main.Core.Equip(item.Item, out Item? replaced))
            {
                return ActionResult.Fail("该物品不能装备", "没有改动装备",
                    "只有武器/防具/鞋子/饰品/魔法卡包可以装备", "/背包");
            }
            ItemStore.Remove(db, item);                        // 从背包移出
            CharacterStore.Sync(main);
            if (replaced is not null)
            {
                await ItemStore.AddAsync(db, userId, replaced.Name,
                    GameplayRules.QualityTierOf(replaced.QualityType), 1, ct,
                    replaced.Id.ToString(CultureInfo.InvariantCulture));
            }
            return ActionResult.Success("🛡️ 装备成功",
                $"{name} → {main.Name} 的 {item.Item.EquipSlotType} 槽"
                + (replaced is null ? "" : $"（换下 {replaced.Name} 已回背包）"),
                ["- 槽位由物品自身决定：Weapon / Armor / Shoes / Accessory1 / Accessory2 / MagicCardPack"],
                ["/角色", "/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> UnequipAsync(long userId, string slot, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            PlayerCharacter? main = await MainCharacterAsync(db, userId, ct);
            if (main is null) return NoMainCharacter();
            if (!TryParseSlot(slot, out EquipSlotType type))
            {
                return ActionResult.Fail("槽位名无效", "没有卸下任何物品",
                    "可用：weapon / armor / shoes / accessory1 / accessory2 / magiccardpack", "/角色");
            }

            Item? removed = main.Core.UnEquip(type);
            if (removed is null)
            {
                return ActionResult.Fail("该槽位没有装备", "没有卸下任何物品",
                    $"槽位 {type} 是空的，用 /角色 查看当前装备", "/角色");
            }
            CharacterStore.Sync(main);
            await ItemStore.AddAsync(db, userId, removed.Name,
                GameplayRules.QualityTierOf(removed.QualityType), 1, ct,
                removed.Id.ToString(CultureInfo.InvariantCulture));
            return ActionResult.Success("📦 已卸下",
                $"{type} 槽 · {removed.Name}",
                ["- 物品已回到背包"], ["/背包", "/角色"]);
        }, cancellationToken);

    // ---------------- 角色养成：普攻 / 技能 / 改名 ----------------

    public Task<ActionResult> UpgradeNormalAttackAsync(long userId, int index, bool preview,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            PlayerCharacter? character = await CharacterByIndexAsync(db, userId, index, ct);
            if (character is null) return NoSuchCharacter(index);

            NormalAttack? attack = character.Core.NormalAttack;
            if (attack is null)
            {
                return ActionResult.Fail("该角色没有普攻", "等级未变", "换个角色试试", "/角色");
            }

            int current = attack.Level;
            int cap = General.GameplayEquilibriumConstant.MaxNormalAttackLevel;
            if (current >= cap)
            {
                return ActionResult.Fail("普攻已满级", "等级未变", $"当前 Lv.{current}，上限 Lv.{cap}", "/角色");
            }
            UpgradeNeed? need = GameplayRules.NormalAttackUpgradeCost(current + 1);
            if (need is null)
            {
                return ActionResult.Fail("没有这一级的配方", "等级未变",
                    $"目标等级 {current + 1}", "/角色");
            }

            string label = $"普攻 Lv.{current} → Lv.{current + 1}";
            Dictionary<string, int> held = await ItemStore.CountByNamesAsync(
                db, userId, [.. need.Items.Select(x => x.Name)], ct);
            if (preview) return PreviewUpgrade("普攻升级", character.Name, label, need, state, held);

            string? lack = await ConsumeUpgradeAsync(db, state, character, need, held, ct);
            if (lack is not null)
            {
                return ActionResult.Fail("升级条件未满足", "没有消耗任何材料，等级未变", lack, "/背包");
            }

            attack.Level = current + 1;
            CharacterStore.Sync(character);
            return ActionResult.Success("⚔️ 普攻升级", $"{character.Name} {label}",
                [$"- 剩余：金币 {state.Credits:0.##}　钻石 {state.Materials:0.##}"],
                ["/角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> UpgradeSkillAsync(long userId, int index, string skillName, bool preview,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            PlayerCharacter? character = await CharacterByIndexAsync(db, userId, index, ct);
            if (character is null) return NoSuchCharacter(index);
            if (string.IsNullOrWhiteSpace(skillName))
            {
                return ActionResult.Fail("没说升级哪个技能", "等级未变",
                    "/技能升级 @1 技能名，技能名见 /角色", "/角色");
            }

            Skill? skill = character.Core.Skills
                .FirstOrDefault(x => string.Equals(x.Name, skillName.Trim(), StringComparison.Ordinal));
            if (skill is null)
            {
                return ActionResult.Fail("没有这个技能", "等级未变",
                    $"{character.Name} 身上找不到「{skillName.Trim()}」", "/角色");
            }
            if (skill.SkillType is not (SkillType.Skill or SkillType.SuperSkill))
            {
                return ActionResult.Fail("这个技能不能升级", "等级未变",
                    "只有主动技能与绝招可以升级（普攻用 /普攻升级）", "/角色");
            }

            int current = skill.Level;
            int cap = General.GameplayEquilibriumConstant.MaxSkillLevel;
            if (current >= cap)
            {
                return ActionResult.Fail("技能已满级", "等级未变", $"当前 Lv.{current}，上限 Lv.{cap}", "/角色");
            }
            UpgradeNeed? need = GameplayRules.SkillUpgradeCost(current + 1);
            if (need is null)
            {
                return ActionResult.Fail("没有这一级的配方", "等级未变", $"目标等级 {current + 1}", "/角色");
            }

            // 旧版：技能 Level 0 表示还没学，第一次「升级」其实是学习到 1 级
            string label = current == 0
                ? $"学习「{skill.Name}」→ Lv.1"
                : $"「{skill.Name}」Lv.{current} → Lv.{current + 1}";
            Dictionary<string, int> held = await ItemStore.CountByNamesAsync(
                db, userId, [.. need.Items.Select(x => x.Name)], ct);
            if (preview) return PreviewUpgrade("技能升级", character.Name, label, need, state, held);

            string? lack = await ConsumeUpgradeAsync(db, state, character, need, held, ct);
            if (lack is not null)
            {
                return ActionResult.Fail("升级条件未满足", "没有消耗任何材料，等级未变", lack, "/背包");
            }

            skill.Level = current + 1;
            CharacterStore.Sync(character);
            return ActionResult.Success("📖 技能升级", $"{character.Name} · {label}",
                [$"- 剩余：金币 {state.Credits:0.##}　钻石 {state.Materials:0.##}"],
                ["/角色", "/状态"]);
        }, cancellationToken);

    /// <summary>
    /// 角色改名：**提交审核**而不是立即生效（角色名会出现在群聊、排行榜、交易里）。
    /// <para>只写 <c>RenameRequests</c>（TargetType=character，TargetId=角色 Guid），
    /// 管理员用 /改名审核 批准后才改角色名，并同步 <c>Users.Nickname</c>（显示名）。</para>
    /// </summary>
    public Task<ActionResult> RenameAsync(long userId, string newName,
        CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            string name = (newName ?? "").Trim();
            (bool allowed, string reason) = NamingPolicy.Check(name, 2, 12, "角色名");
            if (!allowed)
            {
                return ActionResult.Fail("角色名不符合命名规范", "没有提交改名申请", reason, "/角色");
            }

            PlayerCharacter? main = await MainCharacterAsync(db, userId, ct);
            if (main is null) return NoMainCharacter();
            string before = main.Name;
            if (string.Equals(before, name, StringComparison.Ordinal))
            {
                return ActionResult.Fail("新名字和原来一样", "没有提交改名申请", "换一个名字", "/角色");
            }

            // 同一个角色同时只允许一个待审申请（与社团改名同一套规则）。
            bool pending = await db.RenameRequests.AnyAsync(x =>
                x.TargetType == RenameTargets.Character && x.TargetId == main.Guid && x.Status == 0, ct);
            if (pending)
            {
                return ActionResult.Fail("已有一个改名申请在审核中", "没有重复提交",
                    "等管理员处理，或用 /我的改名 看当前申请", "/我的改名");
            }

            // 平台规范：公开可见的名字先审后生效，不能由玩家直接改。
            db.RenameRequests.Add(new RenameRequest
            {
                UserId = userId,
                TargetType = RenameTargets.Character,
                TargetId = main.Guid,
                RequestedName = name,
                Status = 0,
                CreatedAtUtc = clock.UtcNow
            });

            return ActionResult.Success("📨 改名申请已提交",
                $"{before} → 「{name}」",
                [
                    "- 角色名要通过审核才会生效（防冒充官方与站外引流）",
                    "- 审核期间仍用现在的名字，被拒绝会保持原样",
                    "- 改名后交易、社团、私信里显示的名字也会跟着变"
                ], ["/我的改名", "/角色", "/状态"]);
        }, cancellationToken);

    /// <summary>查自己待审的改名（旧版没有，但没有它玩家只能干等）。</summary>
    public Task<IReadOnlyList<RenameRequestEntry>> MyRenameRequestsAsync(long userId,
        CancellationToken cancellationToken = default)
        => ReadAsync<IReadOnlyList<RenameRequestEntry>>(async (db, ct) =>
        {
            List<RenameRequest> rows = await db.RenameRequests.AsNoTracking()
                .Where(x => x.UserId == userId && x.Status == 0)
                .OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
            if (rows.Count == 0) return [];

            List<PlayerCharacter> characters = await AllCharactersAsync(db, userId, ct);
            Dictionary<string, string> names = characters.ToDictionary(x => x.Guid, x => x.Name);
            return [.. rows.Select((row, i) => new RenameRequestEntry(
                i + 1, row.Id, row.TargetType, RenameTargets.Display(row.TargetType),
                names.GetValueOrDefault(row.TargetId, "角色已不存在"),
                row.RequestedName, "我",
                row.CreatedAtUtc.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)))];
        }, cancellationToken);

    // ---------------- 自建角色重随（旧版 RandomCustomCharacter）----------------

    /// <summary>预览暂存在 ActivityProgress 里，确认或取消时清掉；键名固定。</summary>
    private const string RerollKey = "custom-reroll";

    private sealed record RerollAttributes(
        int PrimaryAttribute, double HP, double MP, double ATK, double DEF,
        double STR, double AGI, double INT,
        double STRG, double AGIG, double INTG,
        double SPD, double HR, double MR);

    private sealed record RerollRecord(string Guid, RerollAttributes Attributes);

    public Task<ActionResult> RerollPreviewAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            PlayerCharacter? main = await MainCharacterAsync(db, userId, ct);
            if (main is null) return NoMainCharacter();
            if (main.CatalogId != CharacterStore.CustomCatalogId)
            {
                return ActionResult.Fail("只有自建角色能重随", "没有扣除任何钻石",
                    $"{main.Name} 来自内容包，属性由模板决定，没有可重摇的部分", "/我的角色");
            }

            ActivityProgress? row = await db.ActivityProgress
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityKey == RerollKey, ct);
            RerollAttributes? pending = row is null ? null : ReadReroll(row.ProgressJson, main.Guid);
            if (pending is not null)
            {
                // 旧版同一个待确认预览会重复展示，不会重复扣钻。
                List<string> existing = [.. RerollLines(main, pending)];
                existing.Add("- 用 /确认重随 应用，或 /取消重随 放弃这一组");
                return new ActionResult(false, "你还有一组待确认的重随属性",
                    "没有重复扣除钻石", existing, ["/确认重随", "/取消重随"]);
            }
            if (state.Materials < GameplayRules.CustomRerollCost)
            {
                return ActionResult.Fail("钻石不足", "没有扣除任何钻石",
                    $"重随一次需要 {GameplayRules.CustomRerollCost} 钻石，当前 {state.Materials:0.##}", "/状态");
            }

            Character roll = new OshimaBot.Content.Characters.CustomCharacter(
                CharacterStore.CustomCharacterId, main.Name, nickname: main.Name);
            CharacterStore.NormalizePrimaryAttribute(roll);
            RerollAttributes attributes = RerollAttributesOf(roll);

            PlayerService.ApplyCurrency(db, state, -GameplayRules.CustomRerollCost, "diamonds",
                "custom_reroll", "character", main.Guid);
            state.UpdatedAtUtc = clock.UtcNow;

            string json = JsonSerializer.Serialize(new RerollRecord(main.Guid, attributes));
            if (row is null)
            {
                db.ActivityProgress.Add(new ActivityProgress
                {
                    UserId = userId, ActivityKey = RerollKey, ProgressJson = json,
                    ClaimedJson = "[]", UpdatedAtUtc = clock.UtcNow
                });
            }
            else
            {
                row.ProgressJson = json;
                row.UpdatedAtUtc = clock.UtcNow;
            }

            List<string> lines = [.. RerollLines(main, attributes)];
            lines.Add("- 满意就 /确认重随，不满意可以 /取消重随（钻石不退，同旧版）");
            return ActionResult.Success("🎲 重随预览",
                $"消耗 {GameplayRules.CustomRerollCost} 钻石 · 当前钻石 {state.Materials:0.##}", lines,
                ["/确认重随", "/取消重随", "/我的角色"]);
        }, cancellationToken);

    public Task<ActionResult> RerollConfirmAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            PlayerCharacter? main = await MainCharacterAsync(db, userId, ct);
            if (main is null) return NoMainCharacter();

            ActivityProgress? row = await db.ActivityProgress
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityKey == RerollKey, ct);
            RerollAttributes? pending = row is null ? null : ReadReroll(row.ProgressJson, main.Guid);
            if (pending is null)
            {
                return ActionResult.Fail("没有待确认的重随", "没有修改任何属性",
                    "先 /角色重随 摇一组属性", "/角色重随");
            }

            Character target = main.Core;
            target.PrimaryAttribute = (PrimaryAttribute)pending.PrimaryAttribute;
            target.InitialHP = pending.HP;
            target.InitialMP = pending.MP;
            target.InitialATK = pending.ATK;
            target.InitialDEF = pending.DEF;
            target.InitialSTR = pending.STR;
            target.InitialAGI = pending.AGI;
            target.InitialINT = pending.INT;
            target.STRGrowth = pending.STRG;
            target.AGIGrowth = pending.AGIG;
            target.INTGrowth = pending.INTG;
            target.InitialSPD = pending.SPD;
            target.InitialHR = pending.HR;
            target.InitialMR = pending.MR;
            // 初始值变了，最大生命/魔法跟着变，回满才不会带着上一套数值的残血。
            target.Recovery();
            CharacterStore.Sync(main);
            db.ActivityProgress.Remove(row!);

            List<string> lines = [.. RerollLines(main, pending)];
            lines.Add("- 已回满生命与魔法");
            return ActionResult.Success("✅ 重随已确认", $"{main.Name} 的属性已更新", lines,
                ["/我的角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> RerollCancelAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            ActivityProgress? row = await db.ActivityProgress
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ActivityKey == RerollKey, ct);
            if (row is null)
            {
                return ActionResult.Fail("没有待确认的重随", "没有修改任何属性",
                    "用 /角色重随 摇一组属性", "/角色重随");
            }
            db.ActivityProgress.Remove(row);
            return ActionResult.Success("↩️ 已取消重随", "预览已丢弃，属性保持原样",
                ["- 已扣的钻石不退还（同旧版）"], ["/我的角色", "/角色重随"]);
        }, cancellationToken);

    private static RerollAttributes RerollAttributesOf(Character character) => new(
        (int)character.PrimaryAttribute, character.InitialHP, character.InitialMP, character.InitialATK,
        character.InitialDEF, character.InitialSTR, character.InitialAGI, character.InitialINT,
        character.STRGrowth, character.AGIGrowth, character.INTGrowth,
        character.InitialSPD, character.InitialHR, character.InitialMR);

    private static RerollAttributes? ReadReroll(string json, string guid)
    {
        try
        {
            RerollRecord? record = JsonSerializer.Deserialize<RerollRecord>(json);
            if (record is null || !string.Equals(record.Guid, guid, StringComparison.Ordinal)) return null;
            return record.Attributes;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>「旧值 => 新值」对比行，口径与旧版 RandomCustomCharacter 一致。</summary>
    private static IEnumerable<string> RerollLines(PlayerCharacter main, RerollAttributes next)
    {
        Character now = main.Core;
        yield return $"- 核心属性：{CharacterSet.GetPrimaryAttributeName(now.PrimaryAttribute)}"
            + $" => {CharacterSet.GetPrimaryAttributeName((PrimaryAttribute)next.PrimaryAttribute)}";
        yield return $"- 初始生命：{now.InitialHP:0.##} => {next.HP:0.##}";
        yield return $"- 初始魔法：{now.InitialMP:0.##} => {next.MP:0.##}";
        yield return $"- 初始攻击：{now.InitialATK:0.##} => {next.ATK:0.##}";
        yield return $"- 初始防御：{now.InitialDEF:0.##} => {next.DEF:0.##}";
        yield return $"- 初始力量：{now.InitialSTR:0.##}（+{now.STRGrowth:0.##}/Lv） => "
            + $"{next.STR:0.##}（+{next.STRG:0.##}/Lv）";
        yield return $"- 初始敏捷：{now.InitialAGI:0.##}（+{now.AGIGrowth:0.##}/Lv） => "
            + $"{next.AGI:0.##}（+{next.AGIG:0.##}/Lv）";
        yield return $"- 初始智力：{now.InitialINT:0.##}（+{now.INTGrowth:0.##}/Lv） => "
            + $"{next.INT:0.##}（+{next.INTG:0.##}/Lv）";
        yield return $"- 初始速度：{now.InitialSPD:0.##} => {next.SPD:0.##}";
        yield return $"- 生命回复：{now.InitialHR:0.##} => {next.HR:0.##}";
        yield return $"- 魔法回复：{now.InitialMR:0.##} => {next.MR:0.##}";
    }

    /// <summary>升级类操作共用的前置校验 + 扣除；返回 null 表示成功扣完。</summary>
    private static async Task<string?> ConsumeUpgradeAsync(
        BotDbContext db, PlayerAccount state, PlayerCharacter character,
        UpgradeNeed need, IReadOnlyDictionary<string, int> held, CancellationToken ct)
    {
        List<string> lacks = [];
        if (character.Level < need.RequiredLevel)
        {
            lacks.Add($"角色等级 {character.Level}/{need.RequiredLevel}");
        }
        if (need.RequiredBreak >= 0 && character.BreakLevel < need.RequiredBreak)
        {
            lacks.Add($"突破进度 {GameplayRules.BreakProgress(character.BreakLevel)}"
                + $"/{Math.Clamp(need.RequiredBreak + 1, 0, General.GameplayEquilibriumConstant.LevelBreakList.Count)}");
        }
        if (state.Credits < need.Credits) lacks.Add($"金币 {state.Credits:0.##}/{need.Credits}");
        if (state.Materials < need.Materials) lacks.Add($"钻石 {state.Materials:0.##}/{need.Materials}");
        foreach ((string name, int count) in need.Items)
        {
            int have = held.GetValueOrDefault(name);
            if (have < count) lacks.Add($"{name} {have}/{count}");
        }
        if (lacks.Count > 0) return "缺少：" + string.Join("　", lacks);

        string reference = character.Id.ToString(CultureInfo.InvariantCulture);
        if (need.Credits > 0)
        {
            ApplyCurrency(db, state, -need.Credits, "credits", "upgrade", "character", reference);
        }
        if (need.Materials > 0)
        {
            ApplyCurrency(db, state, -need.Materials, "diamonds", "upgrade", "character", reference);
        }
        if (need.Items.Count > 0)
        {
            await ItemStore.ConsumeByNamesAsync(db, state.UserId,
                need.Items.ToDictionary(x => x.Name, x => x.Count), ct);
        }
        return null;
    }

    private static ActionResult PreviewUpgrade(
        string title, string characterName, string label, UpgradeNeed need,
        PlayerAccount state, IReadOnlyDictionary<string, int> held)
    {
        List<string> lines = [$"- {characterName} · {label}"];
        lines.Add($"- 前置：角色 Lv.{need.RequiredLevel}"
            + (need.RequiredBreak >= 0 ? $"　突破进度 ≥ {need.RequiredBreak + 1}/"
                + $"{General.GameplayEquilibriumConstant.LevelBreakList.Count}" : ""));
        List<string> costs = [];
        if (need.Credits > 0) costs.Add($"金币 {need.Credits}（{state.Credits:0.##}）");
        if (need.Materials > 0) costs.Add($"钻石 {need.Materials}（{state.Materials:0.##}）");
        costs.AddRange(need.Items.Select(x => $"{x.Name} {x.Count}（{held.GetValueOrDefault(x.Name)}）"));
        lines.Add($"- 消耗：{string.Join("、", costs)}");
        return ActionResult.Success($"{title}（预览）", label, lines, ["/角色"]);
    }

    public Task<ActionResult> SetMainAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            List<PlayerCharacter> characters = await CharacterStore.ListAsync(
                db, userId, state.Wallet.MainCharacterGuid, ct);
            if (index <= 0 || index > characters.Count) return NoSuchCharacter(index);
            PlayerCharacter selected = characters[index - 1];
            state.Wallet.MainCharacterGuid = selected.Guid;
            return ActionResult.Success("⭐ 主战角色已更新", selected.Name,
                ["- 装备、练级、突破都作用于当前主战角色"], ["/角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> LevelUpAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            PlayerCharacter? character = await CharacterByIndexAsync(db, userId, index, ct);
            if (character is null) return NoSuchCharacter(index);
            int before = character.Level;
            character.Core.OnLevelUp();               // 消耗角色自身 EXP，受突破进度限制
            if (character.Level == before)
            {
                return ActionResult.Fail("经验不足或需要突破", "等级未变",
                    $"{character.Name} Lv.{character.Level} · 还需 {NeedExp(character)} 点经验"
                    + (NeedBreak(character) is int need ? $"；或到 Lv.{need} 后用 /突破 @编号" : ""),
                    "/练级 @1");
            }
            CharacterStore.Sync(character);
            return ActionResult.Success("⬆️ 角色升级",
                $"{character.Name} → Lv.{character.Level}（+{character.Level - before}）",
                [$"- 剩余经验：{character.Exp:0.##}",
                    NeedBreak(character) is int next ? $"- 下一阶段：Lv.{next} 后可 /突破" : "- 已达等级上限"],
                ["/角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> BreakAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            PlayerCharacter? character = await CharacterByIndexAsync(db, userId, index, ct);
            if (character is null) return NoSuchCharacter(index);

            int before = character.BreakLevel;
            if (NeedBreak(character) is not int needLevel)
            {
                return ActionResult.Fail("已完成全部突破阶段", "突破等级未变",
                    $"{character.Name} 已是 {before} 阶，无需再突破", "/角色");
            }
            if (character.Level < needLevel)
            {
                return ActionResult.Fail("突破条件未满足", "没有消耗任何材料，突破等级未变",
                    $"{character.Name} 需 Lv.{needLevel} 才能突破，当前 Lv.{character.Level}", "/练级 @1");
            }

            // Core 判定达标后**在等级远超时可能连升多阶**，所以先试算，再按跨越的每一档累加材料。
            character.Core.OnLevelBreak();
            int after = character.BreakLevel;
            if (after == before)
            {
                character.Core.LevelBreak = before;
                return ActionResult.Fail("突破条件未满足", "没有消耗任何材料，突破等级未变",
                    $"{character.Name} 需 Lv.{needLevel} 才能突破，当前 Lv.{character.Level}", "/角色");
            }

            (int materialNeed, Dictionary<string, int> itemNeeds) = BreakNeeds(before + 1, after);
            Dictionary<string, int> held = itemNeeds.Count == 0
                ? []
                : await ItemStore.CountByNamesAsync(db, userId, [.. itemNeeds.Keys], ct);

            // 先整体校验；任何一项不足就撤回试算的阶数，一件材料都不扣。
            List<string> lacks = [];
            string materialName = General.GameplayEquilibriumConstant.InGameMaterial;
            if (state.Materials < materialNeed)
            {
                lacks.Add($"{materialName} {state.Materials:0.##}/{materialNeed}");
            }
            foreach ((string name, int need) in itemNeeds)
            {
                int have = held.GetValueOrDefault(name);
                if (have < need) lacks.Add($"{name} {have}/{need}");
            }
            if (lacks.Count > 0)
            {
                character.Core.LevelBreak = before;
                return ActionResult.Fail("突破材料不足", "没有消耗任何材料，突破等级未变",
                    $"缺少：{string.Join("　", lacks)}", "/背包");
            }

            if (materialNeed > 0)
            {
                ApplyCurrency(db, state, -materialNeed, "diamonds", "break", "character",
                    character.Id.ToString(CultureInfo.InvariantCulture));
            }
            if (itemNeeds.Count > 0) await ItemStore.ConsumeByNamesAsync(db, userId, itemNeeds, ct);
            CharacterStore.Sync(character);

            List<string> lines =
            [
                $"- 突破进度 {GameplayRules.BreakProgress(before)} → {GameplayRules.BreakProgress(after)}（上限 Lv.{GameplayRules.NextBreakLevel(after)?.ToString() ?? General.GameplayEquilibriumConstant.MaxLevel.ToString()}）",
                $"- 消耗：{DescribeBreakNeeds(materialNeed, itemNeeds)}",
                $"- 剩余：{materialName} {state.Materials:0.##}"
            ];
            if (GameplayRules.LevelBreakNeedy(after + 1) is { Count: > 0 } next)
            {
                lines.Add($"- 下次突破需要：{DescribeBreakNeeds(next)}");
            }
            else
            {
                lines.Add("- 已完成全部突破阶段");
            }

            return ActionResult.Success("💥 突破成功",
                $"{character.Name} · 突破进度 {GameplayRules.BreakProgress(after)}",
                lines,
                ["/角色", "/练级 @1", "/状态"]);
        }, cancellationToken);

    /// <summary>汇总从 <paramref name="fromStage"/> 到 <paramref name="toStage"/> 各档的材料需求（同名合并）。</summary>
    private static (int Material, Dictionary<string, int> Items) BreakNeeds(int fromStage, int toStage)
    {
        int material = 0;
        Dictionary<string, int> items = [];
        for (int stage = fromStage; stage <= toStage; stage++)
        {
            foreach (BreakNeed need in GameplayRules.LevelBreakNeedy(stage))
            {
                if (need.IsMaterial) material += need.Count;
                else items[need.Name] = items.GetValueOrDefault(need.Name) + need.Count;
            }
        }
        return (material, items);
    }

    private static string DescribeBreakNeeds(int material, IReadOnlyDictionary<string, int> items)
    {
        List<string> parts = [];
        if (material > 0) parts.Add($"{General.GameplayEquilibriumConstant.InGameMaterial} ×{material}");
        parts.AddRange(items.Select(x => $"{x.Key} ×{x.Value}"));
        return parts.Count == 0 ? "无" : string.Join("、", parts);
    }

    private static string DescribeBreakNeeds(IReadOnlyList<BreakNeed> needs)
        => string.Join("、", needs.Select(x => $"{x.Name} ×{x.Count}"));

    public Task<ActionResult> TrainStartAsync(long userId, int index, int minutes, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            PlayerCharacter? character = await CharacterByIndexAsync(db, userId, index, ct);
            if (character is null) return NoSuchCharacter(index);
            if (await db.TrainingSessions.AnyAsync(x => x.UserId == userId && x.SettledAtUtc == null, ct))
            {
                return ActionResult.Fail("已有角色在练级", "没有重复扣除金币",
                    "等待结束后用 /收取练级 结算", "/收取练级");
            }
            int duration = Math.Clamp(minutes, 1, GameplayRules.TrainMaxMinutes);
            DateTime now = clock.UtcNow;
            db.TrainingSessions.Add(new TrainingSession
            {
                UserId = userId, CharacterId = character.Id,
                StartedAtUtc = now, EndsAtUtc = now.AddMinutes(duration)
            });
            return ActionResult.Success("🏋️ 开始练级",
                $"{character.Name} · {duration} 分钟后可用 /收取练级 结算",
                [
                    "- 旧版练级不收费，收益只看时长：每分钟 2 点经验（单次上限 2880 分钟）",
                    $"- 本次预计经验：{GameplayRules.TrainExpPerMinute * duration}"
                ],
                ["/收取练级", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> TrainClaimAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            TrainingSession? session = await db.TrainingSessions
                .Where(x => x.UserId == userId && x.SettledAtUtc == null)
                .OrderBy(x => x.EndsAtUtc).FirstOrDefaultAsync(ct);
            if (session is null)
            {
                return ActionResult.Fail("当前没有练级中的角色", "没有结算任何经验",
                    "先用 /练级 @编号 [分钟] 开始", "/练级 @1 1");
            }
            if (session.EndsAtUtc > clock.UtcNow)
            {
                int seconds = (int)Math.Ceiling((session.EndsAtUtc - clock.UtcNow).TotalSeconds);
                return ActionResult.Fail("练级尚未结束", "没有结算任何经验",
                    $"还需约 {seconds} 秒，稍后再用 /收取练级", "/收取练级");
            }
            string? mainGuid = state.Wallet.MainCharacterGuid;
            List<PlayerCharacter> all = await CharacterStore.ListAsync(db, userId, mainGuid, ct);
            PlayerCharacter? character = all.FirstOrDefault(x => x.Id == session.CharacterId);
            // 旧版 GetTrainingInfo：总经验 = min(分钟数, 2880) × 2
            int trainedMinutes = (int)Math.Min(
                Math.Max(0, (session.EndsAtUtc - session.StartedAtUtc).TotalMinutes),
                GameplayRules.TrainMaxMinutes);
            long exp = GameplayRules.TrainExpPerMinute * (long)trainedMinutes;
            string levelNote = "（角色已不存在）";
            if (character is not null)
            {
                int before = character.Level;
                character.Exp += exp;
                character.Core.OnLevelUp();
                CharacterStore.Sync(character);
                int gained = character.Level - before;
                levelNote = gained > 0
                    ? $"{character.Name} 升了 {gained} 级 → Lv.{character.Level}"
                    : $"{character.Name} 经验 +{exp}（Lv.{character.Level}，还需 {NeedExp(character)}）";
            }
            session.SettledAtUtc = DateTime.UtcNow;
            return ActionResult.Success("🎓 练级结算", levelNote,
                [$"- 金币：{state.Credits}", "- 经验记在角色身上，够了会自动升级"],
                ["/角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> SquadAsync(long userId, string operation, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            Squad? squad = await db.Squads.FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive, ct);
            if (squad is null)
            {
                squad = new Squad { UserId = userId, Name = "主力小队", IsActive = true };
                db.Squads.Add(squad);
                await db.SaveChangesAsync(ct);
            }
            List<SquadMember> members = await db.SquadMembers
                .Where(x => x.SquadId == squad.Id).OrderBy(x => x.Slot).ToListAsync(ct);
            List<PlayerCharacter> characters = await CharacterStore.ListAsync(
                db, userId, state.Wallet.MainCharacterGuid, ct);

            string op = operation.ToLowerInvariant();
            if (op is "add" or "加入")
            {
                if (index <= 0 || index > characters.Count) return NoSuchCharacter(index);
                if (members.Any(x => x.CharacterId == characters[index - 1].Id))
                {
                    return ActionResult.Fail("该角色已在小队中", "没有重复加入", "用 /小队 查看编成", "/小队");
                }
                if (members.Count >= GameplayRules.SquadSize)
                {
                    return ActionResult.Fail($"小队已满（{GameplayRules.SquadSize} 人）", "没有加入新成员",
                        "先用 /小队 移除 @编号 腾出位置", "/小队");
                }
                int slot = Enumerable.Range(1, GameplayRules.SquadSize).First(s => members.All(x => x.Slot != s));
                db.SquadMembers.Add(new SquadMember { SquadId = squad.Id, CharacterId = characters[index - 1].Id, Slot = slot });
                return ActionResult.Success("👥 小队编成", $"{characters[index - 1].Name} 加入 {slot} 号位",
                    [$"- 小队上限 {GameplayRules.SquadSize} 人"], ["/小队", "/角色"]);
            }
            if (op is "remove" or "移除" or "rm")
            {
                SquadMember? target = members.FirstOrDefault(x => x.Slot == index);
                if (target is null)
                {
                    return ActionResult.Fail("该位置没有成员", "没有移除任何角色",
                        "用 /小队 查看位置编号", "/小队");
                }
                db.SquadMembers.Remove(target);
                return ActionResult.Success("👥 小队编成", $"{index} 号位已移除",
                    ["- 其余成员位置保持不变"], ["/小队"]);
            }

            List<string> lines = [];
            foreach (int slot in Enumerable.Range(1, GameplayRules.SquadSize))
            {
                SquadMember? member = members.FirstOrDefault(x => x.Slot == slot);
                PlayerCharacter? character = member is null ? null : characters.FirstOrDefault(x => x.Id == member.CharacterId);
                lines.Add($"- @{slot} {(character is null ? "（空）" : character.Name)}");
            }
            lines.Add("- /小队 加入 @编号　·　/小队 移除 @位置");
            return ActionResult.Success("👥 主力小队", $"{members.Count}/{GameplayRules.SquadSize} 人",
                lines, ["/角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> ClaimQuestAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            if (index <= 0 || index > GameplayRules.DailyQuests.Count)
            {
                return ActionResult.Fail("任务编号无效", "没有领取任何奖励",
                    "用 /每日任务 查看编号", "/每日任务");
            }
            (string key, string name, int target) = GameplayRules.DailyQuests[index - 1];
            string date = clock.Today();
            DailyQuest? quest = await db.DailyQuests
                .FirstOrDefaultAsync(x => x.UserId == userId && x.QuestKey == key && x.QuestDate == date, ct);
            if (quest is null || ReadProgress(quest) < target || quest.Status == 0)
            {
                return ActionResult.Fail($"「{name}」尚未完成", "没有发放奖励",
                    "完成对应行为后再领取", "/每日任务");
            }
            if (quest.Status == 2)
            {
                return ActionResult.Fail("该任务奖励已领取", "没有重复发放",
                    "明天再来", "/每日任务");
            }
            ApplyCurrency(db, state, GameplayRules.QuestRewardCredits, "credits", "quest", "quest", key);
            ApplyCurrency(db, state, GameplayRules.QuestRewardDiamonds, "diamonds", "quest", "quest", key);
            quest.Status = 2;
            quest.ClaimedAtUtc = DateTime.UtcNow;
            return ActionResult.Success("✅ 任务奖励已领取", name,
                [
                    $"- 金币 +{GameplayRules.QuestRewardCredits}　钻石 +{GameplayRules.QuestRewardDiamonds}",
                    $"- 当前金币：{state.Credits}　钻石：{state.Materials}",
                    "- 已写入账本 ledger"
                ], ["/每日任务", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> RecordSimulationAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            await AdvanceQuestAsync(db, userId, "simulate", ct);
            return ActionResult.Success("模拟已计入每日任务", "「完成一次战斗模拟」进度 +1",
                [], ["/每日任务"]);
        }, cancellationToken);

    // ---------------- 内部工具 ----------------

    /// <summary>
    /// 首次访问懒建档：<c>CorePlayers</c> 一行 + 初始角色 + 初始物品 + 初始资金流水，全部一次保存。
    /// <para>钱包与签到进度同表（M1.5 切片③把 <c>PlayerStates</c> 并进来了），所以只查一次。</para>
    /// </summary>
    internal static async Task<PlayerAccount> EnsureAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        CorePlayerRecord? wallet = await db.CorePlayers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (wallet is not null) return new PlayerAccount(wallet);

        wallet = new CorePlayerRecord
        {
            UserId = userId,
            Credits = GameplayRules.StartingCredits,
            Materials = GameplayRules.StartingDiamonds,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.CorePlayers.Add(wallet);

        // 旧版注册即送一个与昵称同名的自建角色（CustomCharacter），不是内容包里的预制角色。
        // QQ 事件里拿不到昵称（openid 而已），所以库里的 Nickname 初值为空：
        // 这里补一个默认名，否则 /报价列表、社团成员这些地方会显示成随机 Handle。
        Data.Entities.User? user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        string nickname = user?.Nickname ?? "";
        if (string.IsNullOrWhiteSpace(nickname))
        {
            nickname = DefaultTravelerName();
            if (user is not null) user.Nickname = nickname;
        }
        PlayerCharacter starter = CharacterStore.CreateCustom(db, userId, nickname.Trim());
        wallet.MainCharacterGuid = starter.Guid;
        var itemTemplates = FunGameService.AllItems;
        for (int i = 0; i < Math.Min(GameplayRules.StartingItems, itemTemplates.Count); i++)
        {
            var template = itemTemplates[i];
            await ItemStore.AddAsync(db, userId, template.Name, 1, 1, ct,
                template.Id.ToString(CultureInfo.InvariantCulture));
        }
        db.LedgerEntries.Add(new LedgerEntry
        {
            UserId = userId, Currency = "credits", Delta = GameplayRules.StartingCredits,
            BalanceAfter = GameplayRules.StartingCredits, Reason = "register",
            RefType = "player", RefId = userId.ToString(CultureInfo.InvariantCulture),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return new PlayerAccount(wallet);
    }

    /// <summary>
    /// 起名用的字符集：去掉 0/O/1/I 这类容易看错的字符（旧版 <c>FunOsm-XXXXXXXX</c> 的思路）。
    /// </summary>
    private const string TravelerNameAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// 新号的默认名：`旅人` + 4 位随机字母数字。旧版是 `FunOsm-` + 8 位随机码，
    /// 这里只是短一些。<c>Random.Shared</c> 仅用于取名，不参与任何玩法判定，不影响同种子回归。
    /// </summary>
    private static string DefaultTravelerName()
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = TravelerNameAlphabet[Random.Shared.Next(TravelerNameAlphabet.Length)];
        }
        return "旅人" + new string(chars);
    }

    /// <summary>货币变动与 ledger 必须成对出现；Add 后由调用方的 SaveChanges 一次提交。</summary>
    internal static void ApplyCurrency(
        BotDbContext db, PlayerAccount account, double delta, string currency,
        string reason, string refType, string refId)
    {
        if (delta == 0) return;
        // credits / diamonds 之外，积分商店还有三种积分；都走同一套流水，便于对账。
        double balance = currency switch
        {
            "diamonds" => account.Materials += delta,
            "forge_points" => account.ForgePoints += delta,
            "horse_points" => account.HorseRacingPoints += delta,
            "coop_points" => account.CooperativePoints += delta,
            _ => account.Credits += delta
        };
        db.LedgerEntries.Add(new LedgerEntry
        {
            UserId = account.UserId, Currency = currency, Delta = delta, BalanceAfter = balance,
            Reason = reason, RefType = refType, RefId = refId, CreatedAtUtc = DateTime.UtcNow
        });
    }

    internal static Task AddItemAsync(
        BotDbContext db, long userId, string name, int quality, int quantity,
        CancellationToken ct, string catalogId = "gacha")
        => ItemStore.AddAsync(db, userId, name, quality, quantity, ct, catalogId);

    internal static Task<PlayerItem?> ItemByIndexAsync(
        BotDbContext db, long userId, int index, CancellationToken ct)
        => ItemStore.ByIndexAsync(db, userId, index, ct);

    /// <summary>玩家的全部角色（按创建顺序，序号与 <c>/我的角色</c> 一致）。</summary>
    internal static async Task<List<PlayerCharacter>> AllCharactersAsync(
        BotDbContext db, long userId, CancellationToken ct)
    {
        string? mainGuid = await MainGuidAsync(db, userId, ct);
        return await CharacterStore.ListAsync(db, userId, mainGuid, ct);
    }

    /// <summary>小队编成（按槽位排序）；未编成返回空列表。</summary>
    internal static async Task<List<PlayerCharacter>> SquadCharactersAsync(
        BotDbContext db, long userId, CancellationToken ct)
    {
        Squad? squad = await db.Squads.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.IsActive, ct);
        if (squad is null) return [];

        List<long> memberIds = await db.SquadMembers.AsNoTracking()
            .Where(x => x.SquadId == squad.Id).OrderBy(x => x.Slot)
            .Select(x => x.CharacterId).ToListAsync(ct);
        if (memberIds.Count == 0) return [];

        List<PlayerCharacter> loaded = await AllCharactersAsync(db, userId, ct);
        return [.. loaded.Where(x => memberIds.Contains(x.Id)).OrderBy(x => memberIds.IndexOf(x.Id))];
    }

    internal static async Task<PlayerCharacter?> CharacterByIndexAsync(
        BotDbContext db, long userId, int index, CancellationToken ct)
    {
        string? mainGuid = await MainGuidAsync(db, userId, ct);
        return await CharacterStore.ByIndexAsync(db, userId, index, mainGuid, ct);
    }

    internal static async Task<PlayerCharacter?> MainCharacterAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        string? mainGuid = await MainGuidAsync(db, userId, ct);
        return await CharacterStore.MainAsync(db, userId, mainGuid, ct);
    }

    internal static Task<string?> MainGuidAsync(BotDbContext db, long userId, CancellationToken ct)
        => db.CorePlayers.Where(x => x.UserId == userId)
            .Select(x => x.MainCharacterGuid).FirstOrDefaultAsync(ct);

    /// <summary>距离下一级还差多少经验（取自 Core 的 <c>EXPUpperLimit</c>）。</summary>
    private static string NeedExp(PlayerCharacter character)
    {
        var equilibrium = General.GameplayEquilibriumConstant;
        if (character.Level >= equilibrium.MaxLevel) return "0";
        return equilibrium.EXPUpperLimit.TryGetValue(character.Level, out double need)
            ? Math.Max(0, need - character.Exp).ToString("0.##", CultureInfo.InvariantCulture)
            : "?";
    }

    /// <summary>下一次突破所需等级；已无可突破阶数时返回 null。</summary>
    private static int? NeedBreak(PlayerCharacter character)
        => GameplayRules.NextBreakLevel(character.BreakLevel);

    internal static void Consume(BotDbContext db, PlayerItem item, int amount)
    {
        if (item.Quantity <= amount) ItemStore.Remove(db, item);
        else item.Quantity -= amount;
    }

    internal async Task AdvanceQuestAsync(BotDbContext db, long userId, string key, CancellationToken ct)
    {
        (string _, string _, int target) = GameplayRules.DailyQuests.First(x => x.Key == key);
        string date = clock.Today();
        DailyQuest? quest = await db.DailyQuests.FirstOrDefaultAsync(
            x => x.UserId == userId && x.QuestKey == key && x.QuestDate == date, ct);
        if (quest is null)
        {
            quest = new DailyQuest
            {
                UserId = userId, QuestKey = key, QuestDate = date,
                Status = target <= 1 ? 1 : 0,
                ProgressJson = JsonSerializer.Serialize(new { count = 1 })
            };
            db.DailyQuests.Add(quest);
            return;
        }
        if (quest.Status == 2) return;
        int count = ReadProgress(quest) + 1;
        quest.ProgressJson = JsonSerializer.Serialize(new { count });
        if (count >= target) quest.Status = 1;
    }

    internal static int ReadProgress(DailyQuest quest)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(quest.ProgressJson);
            return document.RootElement.TryGetProperty("count", out JsonElement count)
                ? count.GetInt32() : 0;
        }
        catch (JsonException) { return 0; }
    }

    internal static string ReadName(PlayerItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Name)) return item.Name;
        return $"物品 {item.CatalogId}";
    }

    /// <summary>描述摘要：压掉换行，超长截断，用于搜索结果的单行展示。</summary>
    private static string Snippet(string text, int max = 40)
    {
        string flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    // ---- 魔法卡 ----

    /// <summary>去掉名字里已有的「 +N」后缀，避免叠加等级时反复追加。</summary>
    internal static string TrimMagicName(string name)
    {
        int index = name.LastIndexOf(" +", StringComparison.Ordinal);
        return index > 0 && int.TryParse(name[(index + 2)..], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out _)
            ? name[..index]
            : name;
    }

    /// <summary>魔法显示名：1 级不带后缀，2 级起写成「名字 +1」（与旧版一致，后缀是叠加次数）。</summary>
    internal static string MagicLabel(string name, int level)
        => level > 1 ? $"{TrimMagicName(name)} +{level - 1}" : TrimMagicName(name);

    /// <summary>卡包内容的可读摘要。</summary>
    internal static string DescribeMagicPack(Item pack)
        => pack.Skills.Magics.Count == 0
            ? "包含魔法：（空卡包）"
            : $"包含魔法：{string.Join("，", pack.Skills.Magics.Select(m => MagicLabel(m.Name, m.Level)))}";

    /// <summary>重写卡包描述第一行（魔法列表），保留「增加角色属性」及其后的内容。</summary>
    internal static void RefreshMagicPackDescription(Item pack)
    {
        string head = DescribeMagicPack(pack);
        string description = pack.Description ?? "";
        int index = description.IndexOf("增加角色属性", StringComparison.Ordinal);
        pack.Description = index < 0 ? head : $"{head}\r\n{description[index..]}";
    }

    /// <summary>统计 Core 快照里 6 个装备槽已占用的数量。</summary>
    internal static int CountEquipped(Character character)
    {
        int count = 0;
        if (character.EquipSlot.MagicCardPack is not null) count++;
        if (character.EquipSlot.Weapon is not null) count++;
        if (character.EquipSlot.Armor is not null) count++;
        if (character.EquipSlot.Shoes is not null) count++;
        if (character.EquipSlot.Accessory1 is not null) count++;
        if (character.EquipSlot.Accessory2 is not null) count++;
        return count;
    }

    /// <summary>解析 /卸下 的槽位名；兼容旧的 trinket（→ Accessory1）。</summary>
    internal static bool TryParseSlot(string slot, out EquipSlotType type)
    {
        type = EquipSlotType.None;
        if (string.IsNullOrWhiteSpace(slot)) return false;
        string normalized = slot.Trim();
        if (normalized.Equals("trinket", StringComparison.OrdinalIgnoreCase))
        {
            type = EquipSlotType.Accessory1;
            return true;
        }
        return Enum.TryParse(normalized, true, out type) && type != EquipSlotType.None;
    }

    internal static ActionResult NoSuchItem(int index)
        => ActionResult.Fail($"背包 @ {index} 不存在", "没有改动任何物品",
            "用 /背包 查看当前编号，编号按背包顺序稳定排列", "/背包");

    internal static ActionResult NoSuchCharacter(int index)
        => ActionResult.Fail($"角色 @ {index} 不存在", "没有改动任何角色",
            "用 /角色 查看当前编号", "/角色");

    internal static ActionResult NoMainCharacter()
        => ActionResult.Fail("没有主战角色", "没有改动任何装备",
            "用 /角色 查看角色，再用 /设主战 @编号 指定", "/角色");
}
