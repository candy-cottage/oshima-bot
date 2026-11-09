using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;
using DataCharacter = OshimaBot.Data.Entities.Character;

namespace OshimaBot.Gameplay;

public interface IPlayerService
{
    Task<PlayerSnapshot?> GetSnapshotAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> RegisterAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> SignInAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> GachaAsync(long userId, int count, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BagEntry>> GetBagAsync(long userId, CancellationToken cancellationToken = default);
    Task<ActionResult> UseAsync(long userId, int index, int quantity, CancellationToken cancellationToken = default);
    Task<ActionResult> SalvageAsync(long userId, int index, int quantity, CancellationToken cancellationToken = default);
    Task<ActionResult> SetLockAsync(long userId, int index, bool locked, CancellationToken cancellationToken = default);
    Task<ActionResult> EquipAsync(long userId, int index, CancellationToken cancellationToken = default);
    Task<ActionResult> UnequipAsync(long userId, string slot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CharacterEntry>> GetCharactersAsync(long userId, CancellationToken cancellationToken = default);
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
public sealed class PlayerService(IServiceScopeFactory scopeFactory, ILogger<PlayerService> logger) : IPlayerService
{
    private static ActionResult NoIdentity => ActionResult.Fail(
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
            PlayerState state = await EnsureAsync(db, userId, ct);
            List<DataCharacter> characters = await db.Characters.AsNoTracking()
                .Where(x => x.UserId == userId).OrderBy(x => x.Id).ToListAsync(ct);
            int itemCount = await db.ItemInstances.AsNoTracking().CountAsync(x => x.UserId == userId, ct);
            List<long> squadIds = await db.Squads.AsNoTracking()
                .Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(ct);
            int squadCount = squadIds.Count == 0 ? 0 : await db.SquadMembers.AsNoTracking()
                .CountAsync(x => squadIds.Contains(x.SquadId), ct);
            int pendingTrainings = await db.TrainingSessions.AsNoTracking()
                .CountAsync(x => x.UserId == userId && x.SettledAtUtc == null, ct);
            return new PlayerSnapshot(userId, state.Level, state.Exp, state.Credits, state.Diamonds,
                state.SignInStreak, state.SignedOn, characters.FirstOrDefault(x => x.IsMain)?.Name,
                characters.Count, itemCount, squadCount, pendingTrainings);
        }, cancellationToken);

    public Task<IReadOnlyList<BagEntry>> GetBagAsync(long userId, CancellationToken cancellationToken = default)
        => userId <= 0 ? Task.FromResult<IReadOnlyList<BagEntry>>([]) : ReadAsync(async (db, ct) =>
        {
            await EnsureAsync(db, userId, ct);
            List<ItemInstance> items = await db.ItemInstances.AsNoTracking()
                .Where(x => x.UserId == userId).OrderBy(x => x.Id).ToListAsync(ct);
            List<long> ids = items.Select(x => x.Id).ToList();
            HashSet<long> equipped = (await db.CharacterEquipment.AsNoTracking()
                .Where(x => ids.Contains(x.ItemInstanceId))
                .Select(x => x.ItemInstanceId).ToListAsync(ct)).ToHashSet();
            return (IReadOnlyList<BagEntry>)items.Select((item, i) => new BagEntry(
                i + 1, item.Id, ReadName(item), item.CatalogId, item.Quantity,
                item.Quality, item.Locked, item.Bound, equipped.Contains(item.Id))).ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<CharacterEntry>> GetCharactersAsync(long userId, CancellationToken cancellationToken = default)
        => userId <= 0 ? Task.FromResult<IReadOnlyList<CharacterEntry>>([]) : ReadAsync(async (db, ct) =>
        {
            await EnsureAsync(db, userId, ct);
            List<DataCharacter> characters = await db.Characters.AsNoTracking()
                .Where(x => x.UserId == userId).OrderBy(x => x.Id).ToListAsync(ct);
            Dictionary<long, int> equipment = await db.CharacterEquipment.AsNoTracking()
                .GroupBy(x => x.CharacterId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return (IReadOnlyList<CharacterEntry>)characters.Select((c, i) => new CharacterEntry(
                i + 1, c.Id, c.Name, c.Level, c.Exp, c.BreakLevel, c.IsMain,
                equipment.GetValueOrDefault(c.Id))).ToArray();
        }, cancellationToken);

    public async Task<IReadOnlyList<QuestEntry>> GetQuestsAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) return [];
        string date = GameplayRules.Today();
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
            : GameplayRules.Fortune(userId, GameplayRules.Today()));

    // ---------------- 写（事务） ----------------

    private async Task<ActionResult> MutateAsync(
        long userId,
        Func<BotDbContext, PlayerState, CancellationToken, Task<ActionResult>> body,
        CancellationToken cancellationToken)
    {
        if (userId <= 0) return NoIdentity;
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            BotDbContext db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(cancellationToken);

            PlayerState state = await EnsureAsync(db, userId, cancellationToken);
            ActionResult result = await body(db, state, cancellationToken);
            if (!result.Ok)
            {
                await transaction.RollbackAsync(cancellationToken);
                return result;
            }
            state.UpdatedAtUtc = DateTime.UtcNow;
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
            bool existed = await db.PlayerStates.AsNoTracking().AnyAsync(x => x.UserId == userId, ct);
            PlayerState state = await EnsureAsync(db, userId, ct);
            return ActionResult.Success("🎒 旅人登记",
                existed ? "存档已存在，未重复发放初始资源" : "初始资源已发放",
                [
                    $"- 旅人等级：Lv.{state.Level}　经验：{state.Exp}",
                    $"- 金币：{state.Credits}　钻石：{state.Diamonds}",
                    $"- 角色：{await db.Characters.CountAsync(x => x.UserId == userId, ct)} 名",
                    $"- 物品：{await db.ItemInstances.CountAsync(x => x.UserId == userId, ct)} 格",
                    "- 旧版 JSON 存档未迁移，所有旅人从今天重新开始"
                ], ["/状态", "/签到", "/抽卡", "/背包"]);
        }, cancellationToken);

    public Task<ActionResult> SignInAsync(long userId, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            string today = GameplayRules.Today();
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

            long credits = GameplayRules.SignInCredits(state.SignInStreak - 1);
            ApplyCurrency(db, state, credits, "credits", "signin", "signin", today);
            int diamonds = state.SignInStreak % GameplayRules.SignInDiamondEveryDays == 0
                ? GameplayRules.SignInDiamondAmount : 0;
            if (diamonds > 0)
            {
                ApplyCurrency(db, state, diamonds, "diamonds", "signin_streak", "signin", today);
            }
            db.SignInRecords.Add(new SignInRecord
            {
                UserId = userId, SignInDate = today,
                RewardJson = JsonSerializer.Serialize(new { credits, diamonds }),
                CreatedAtUtc = DateTime.UtcNow
            });
            await AdvanceQuestAsync(db, userId, "signin", ct);
            return ActionResult.Success("📅 签到成功",
                $"连续 {state.SignInStreak} 天 · 金币 +{credits}{(diamonds > 0 ? $" · 钻石 +{diamonds}" : "")}",
                [
                    $"- 当前金币：{state.Credits}　钻石：{state.Diamonds}",
                    $"- 连续签到每满 {GameplayRules.SignInDiamondEveryDays} 天额外送 {GameplayRules.SignInDiamondAmount} 钻石",
                    "- 今日任务「今日签到」进度已更新"
                ], ["/每日任务", "/状态", "/抽卡"]);
        }, cancellationToken);

    public Task<ActionResult> GachaAsync(long userId, int count, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            int pulls = Math.Clamp(count, 1, GameplayRules.MaxGachaPerCall);
            int cost = pulls == 10 ? GameplayRules.GachaTenCost : GameplayRules.GachaCost * pulls;
            if (state.Diamonds < cost)
            {
                return ActionResult.Fail("钻石不足", "没有扣除钻石，也没有生成物品",
                    $"需要 {cost} 钻石，当前 {state.Diamonds}；可先签到或领取每日任务", "/签到");
            }
            ApplyCurrency(db, state, -cost, "diamonds", "gacha", "gacha", GameplayRules.Today());

            var catalog = FunGameService.AllItems;
            List<(string Name, int Quality)> drawn = [];
            for (int i = 0; i < pulls; i++)
            {
                int quality = GameplayRules.RollQuality(Random.Shared);
                // 十连保底至少一件史诗及以上。
                if (pulls == 10 && i == pulls - 1 && drawn.All(x => x.Quality < 4)) quality = 4;
                string name = catalog.Count == 0
                    ? "无名的遗物"
                    : catalog[Random.Shared.Next(catalog.Count)].Name;
                drawn.Add((name, quality));
            }
            foreach ((string name, int quality) in drawn)
            {
                await AddItemAsync(db, userId, name, quality, 1, ct);
            }
            await AdvanceQuestAsync(db, userId, "gacha", ct);

            List<string> lines = drawn.Select((item, i) =>
                $"- {i + 1}. {GameplayRules.QualityName(item.Quality)} · {item.Name}").ToList();
            lines.Add($"- 消耗钻石 {cost}，剩余 {state.Diamonds}");
            lines.Add("- 用 /背包 查看物品编号后再 @编号 使用或分解");
            return ActionResult.Success(pulls == 10 ? "🎰 十连抽卡" : "🎲 抽卡",
                $"{pulls} 次抽取 · 已全部放入背包",
                lines, ["/背包", "/状态", "/抽卡"]);
        }, cancellationToken);

    public Task<ActionResult> UseAsync(long userId, int index, int quantity, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            ItemInstance? item = await ItemByIndexAsync(db, userId, index, ct);
            if (item is null) return NoSuchItem(index);
            if (await db.CharacterEquipment.AsNoTracking()
                .AnyAsync(x => x.ItemInstanceId == item.Id, ct))
            {
                return ActionResult.Fail("该物品已装备", "没有消耗物品",
                    "先 /卸下 <槽位> 再使用", "/背包");
            }
            int amount = Math.Clamp(quantity, 1, item.Quantity);
            int exp = GameplayRules.UseExp(item.Quality, amount);
            state.Exp += exp;
            Consume(db, item, amount);
            return ActionResult.Success("🧪 使用物品",
                $"{ReadName(item)} ×{amount} · 旅人经验 +{exp}",
                [$"- 当前经验：{state.Exp}　等级：Lv.{state.Level}", "- 物品已扣除，未产生货币变动"],
                ["/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> SalvageAsync(long userId, int index, int quantity, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            ItemInstance? item = await ItemByIndexAsync(db, userId, index, ct);
            if (item is null) return NoSuchItem(index);
            if (item.Locked || item.Bound)
            {
                return ActionResult.Fail("该物品已锁定或绑定", "没有分解任何物品",
                    "先用 /解锁 @编号，绑定装备需先 /卸下", "/背包");
            }
            int amount = Math.Clamp(quantity, 1, item.Quantity);
            string name = ReadName(item);
            long credits = GameplayRules.SalvageValue(item.Quality, amount);
            ApplyCurrency(db, state, credits, "credits", "salvage", "item", item.Id.ToString(CultureInfo.InvariantCulture));
            Consume(db, item, amount);
            return ActionResult.Success("♻️ 分解完成",
                $"{name} ×{amount} · 金币 +{credits}",
                [$"- 品质：{GameplayRules.QualityName(item.Quality)}　当前金币：{state.Credits}", "- 已写入账本 ledger，可对账"],
                ["/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> SetLockAsync(long userId, int index, bool locked, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            ItemInstance? item = await ItemByIndexAsync(db, userId, index, ct);
            if (item is null) return NoSuchItem(index);
            item.Locked = locked;
            return ActionResult.Success(locked ? "🔒 物品已锁定" : "🔓 物品已解锁",
                $"{ReadName(item)} 现在{(locked ? "不可分解/丢弃" : "可分解")}",
                ["- 锁定状态已写入存档"], ["/背包"]);
        }, cancellationToken);

    public Task<ActionResult> EquipAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            ItemInstance? item = await ItemByIndexAsync(db, userId, index, ct);
            if (item is null) return NoSuchItem(index);
            if (item.Bound) return ActionResult.Fail("该物品已装备", "没有重复装备", "先 /卸下 <槽位>", "/背包");
            DataCharacter? main = await MainCharacterAsync(db, userId, ct);
            if (main is null) return NoMainCharacter();
            List<string> used = await db.CharacterEquipment.AsNoTracking()
                .Where(x => x.CharacterId == main.Id).Select(x => x.Slot).ToListAsync(ct);
            string? slot = GameplayRules.EquipmentSlots.FirstOrDefault(s => !used.Contains(s));
            if (slot is null)
            {
                return ActionResult.Fail("装备槽已满", "没有装备该物品",
                    "先 /卸下 weapon / armor / trinket 之一", "/角色");
            }
            item.Bound = true;
            db.CharacterEquipment.Add(new CharacterEquipment
            {
                CharacterId = main.Id, Slot = slot, ItemInstanceId = item.Id
            });
            return ActionResult.Success("🛡️ 装备成功",
                $"{ReadName(item)} → {main.Name} 的 {slot} 槽",
                ["- 装备后自动绑定，需先卸下才能分解", "- 槽位：weapon / armor / trinket"],
                ["/角色", "/背包", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> UnequipAsync(long userId, string slot, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            DataCharacter? main = await MainCharacterAsync(db, userId, ct);
            if (main is null) return NoMainCharacter();
            CharacterEquipment? equipment = await db.CharacterEquipment
                .FirstOrDefaultAsync(x => x.CharacterId == main.Id && x.Slot == slot, ct);
            if (equipment is null)
            {
                return ActionResult.Fail("该槽位没有装备", "没有卸下任何物品",
                    "用 /角色 查看当前装备，槽位为 weapon / armor / trinket", "/角色");
            }
            ItemInstance? item = await db.ItemInstances
                .FirstOrDefaultAsync(x => x.Id == equipment.ItemInstanceId && x.UserId == userId, ct);
            db.CharacterEquipment.Remove(equipment);
            if (item is not null) item.Bound = false;
            return ActionResult.Success("📦 已卸下",
                $"{slot} 槽 · {(item is null ? "未知物品" : ReadName(item))}",
                ["- 物品已回到背包并解除绑定"], ["/背包", "/角色"]);
        }, cancellationToken);

    public Task<ActionResult> SetMainAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
        {
            List<DataCharacter> characters = await db.Characters
                .Where(x => x.UserId == userId).OrderBy(x => x.Id).ToListAsync(ct);
            if (index <= 0 || index > characters.Count) return NoSuchCharacter(index);
            foreach (DataCharacter character in characters) character.IsMain = false;
            DataCharacter selected = characters[index - 1];
            selected.IsMain = true;
            return ActionResult.Success("⭐ 主战角色已更新", selected.Name,
                ["- 装备、练级、突破都作用于当前主战角色"], ["/角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> LevelUpAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            DataCharacter? character = await CharacterByIndexAsync(db, userId, index, ct);
            if (character is null) return NoSuchCharacter(index);
            int cap = GameplayRules.LevelCap(character.BreakLevel);
            if (character.Level >= cap)
            {
                return ActionResult.Fail("已达当前等级上限", "没有消耗经验",
                    $"上限 Lv.{cap}，先用 /突破 @编号 提升", "/角色");
            }
            long cost = GameplayRules.ExpForLevel(character.Level);
            if (state.Exp < cost)
            {
                return ActionResult.Fail("旅人经验不足", "没有消耗经验，等级未变",
                    $"升级需要 {cost} 经验，当前 {state.Exp}；练级或使用物品可获得经验", "/练级 @1");
            }
            state.Exp -= cost;
            character.Level += 1;
            character.Exp += cost;
            return ActionResult.Success("⬆️ 角色升级", $"{character.Name} → Lv.{character.Level}",
                [$"- 消耗旅人经验 {cost}，剩余 {state.Exp}", $"- 当前上限 Lv.{cap}"],
                ["/角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> BreakAsync(long userId, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            DataCharacter? character = await CharacterByIndexAsync(db, userId, index, ct);
            if (character is null) return NoSuchCharacter(index);
            int cap = GameplayRules.LevelCap(character.BreakLevel);
            if (character.Level < cap)
            {
                return ActionResult.Fail("尚未达到当前等级上限", "没有消耗金币",
                    $"需要 Lv.{cap}，当前 Lv.{character.Level}", "/角色");
            }
            long cost = GameplayRules.BreakCost(character.BreakLevel);
            if (state.Credits < cost)
            {
                return ActionResult.Fail("金币不足", "没有消耗金币，突破等级未变",
                    $"需要 {cost} 金币，当前 {state.Credits}", "/签到");
            }
            ApplyCurrency(db, state, -cost, "credits", "break", "character", character.Id.ToString(CultureInfo.InvariantCulture));
            character.BreakLevel += 1;
            return ActionResult.Success("💥 突破成功",
                $"{character.Name} · 突破 {character.BreakLevel} 阶 · 上限提升至 Lv.{GameplayRules.LevelCap(character.BreakLevel)}",
                [$"- 消耗金币 {cost}，剩余 {state.Credits}", "- 已写入账本 ledger"],
                ["/角色", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> TrainStartAsync(long userId, int index, int minutes, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            DataCharacter? character = await CharacterByIndexAsync(db, userId, index, ct);
            if (character is null) return NoSuchCharacter(index);
            if (await db.TrainingSessions.AnyAsync(x => x.UserId == userId && x.SettledAtUtc == null, ct))
            {
                return ActionResult.Fail("已有角色在练级", "没有重复扣除金币",
                    "等待结束后用 /收取练级 结算", "/收取练级");
            }
            if (state.Credits < GameplayRules.TrainCostCredits)
            {
                return ActionResult.Fail("金币不足", "没有开始练级",
                    $"需要 {GameplayRules.TrainCostCredits} 金币，当前 {state.Credits}", "/签到");
            }
            int duration = Math.Clamp(minutes, 1, GameplayRules.TrainMaxMinutes);
            DateTime now = DateTime.UtcNow;
            ApplyCurrency(db, state, -GameplayRules.TrainCostCredits, "credits", "train_start", "character", character.Id.ToString(CultureInfo.InvariantCulture));
            db.TrainingSessions.Add(new TrainingSession
            {
                UserId = userId, CharacterId = character.Id,
                StartedAtUtc = now, EndsAtUtc = now.AddMinutes(duration)
            });
            return ActionResult.Success("🏋️ 开始练级",
                $"{character.Name} · {duration} 分钟后可用 /收取练级 结算",
                [$"- 消耗金币 {GameplayRules.TrainCostCredits}，剩余 {state.Credits}",
                    $"- 结算经验：{GameplayRules.TrainExpBase + GameplayRules.TrainExpPerLevel * character.Level}"],
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
            if (session.EndsAtUtc > DateTime.UtcNow)
            {
                int seconds = (int)Math.Ceiling((session.EndsAtUtc - DateTime.UtcNow).TotalSeconds);
                return ActionResult.Fail("练级尚未结束", "没有结算任何经验",
                    $"还需约 {seconds} 秒，稍后再用 /收取练级", "/收取练级");
            }
            DataCharacter? character = await db.Characters
                .FirstOrDefaultAsync(x => x.Id == session.CharacterId && x.UserId == userId, ct);
            long exp = GameplayRules.TrainExpBase + GameplayRules.TrainExpPerLevel * (character?.Level ?? 1);
            state.Exp += exp;
            if (character is not null) character.Exp += exp;
            session.SettledAtUtc = DateTime.UtcNow;
            return ActionResult.Success("🎓 练级结算",
                $"{(character is null ? "角色" : character.Name)} · 旅人经验 +{exp}",
                [$"- 当前经验：{state.Exp}　等级：Lv.{state.Level}", "- 经验可用于 /升级 @编号"],
                ["/角色", "/升级 @1", "/状态"]);
        }, cancellationToken);

    public Task<ActionResult> SquadAsync(long userId, string operation, int index, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, _, ct) =>
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
            List<DataCharacter> characters = await db.Characters.AsNoTracking()
                .Where(x => x.UserId == userId).OrderBy(x => x.Id).ToListAsync(ct);

            string op = operation.ToLowerInvariant();
            if (op is "add" or "加入")
            {
                if (index <= 0 || index > characters.Count) return NoSuchCharacter(index);
                if (members.Any(x => x.CharacterId == characters[index - 1].Id))
                {
                    return ActionResult.Fail("该角色已在小队中", "没有重复加入", "用 /小队 查看编成", "/小队");
                }
                if (members.Count >= 3)
                {
                    return ActionResult.Fail("小队已满（3 人）", "没有加入新成员",
                        "先用 /小队 移除 @编号 腾出位置", "/小队");
                }
                int slot = Enumerable.Range(1, 3).First(s => members.All(x => x.Slot != s));
                db.SquadMembers.Add(new SquadMember { SquadId = squad.Id, CharacterId = characters[index - 1].Id, Slot = slot });
                return ActionResult.Success("👥 小队编成", $"{characters[index - 1].Name} 加入 {slot} 号位",
                    ["- 小队上限 3 人"], ["/小队", "/角色"]);
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
            foreach (int slot in Enumerable.Range(1, 3))
            {
                SquadMember? member = members.FirstOrDefault(x => x.Slot == slot);
                DataCharacter? character = member is null ? null : characters.FirstOrDefault(x => x.Id == member.CharacterId);
                lines.Add($"- @{slot} {(character is null ? "（空）" : character.Name)}");
            }
            lines.Add("- /小队 加入 @编号　·　/小队 移除 @位置");
            return ActionResult.Success("👥 主力小队", $"{members.Count}/3 人",
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
            string date = GameplayRules.Today();
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
                    $"- 当前金币：{state.Credits}　钻石：{state.Diamonds}",
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

    /// <summary>首次访问懒建档：player_state + 初始角色 + 初始物品 + 初始资金流水，全部一次保存。</summary>
    private static async Task<PlayerState> EnsureAsync(BotDbContext db, long userId, CancellationToken ct)
    {
        PlayerState? state = await db.PlayerStates.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (state is not null) return state;

        state = new PlayerState
        {
            UserId = userId, Credits = GameplayRules.StartingCredits,
            Diamonds = GameplayRules.StartingDiamonds, Level = 1,
            LogonOn = GameplayRules.Today(), UpdatedAtUtc = DateTime.UtcNow
        };
        db.PlayerStates.Add(state);

        var characterTemplates = FunGameService.Characters;
        if (characterTemplates.Count > 0)
        {
            var template = characterTemplates[0];
            db.Characters.Add(new DataCharacter
            {
                UserId = userId,
                CatalogId = template.Id.ToString(CultureInfo.InvariantCulture),
                CatalogVersion = GameplayRules.CatalogVersion,
                Name = template.GetName(), Level = 1, IsMain = true,
                AttrsJson = "{}", CreatedAtUtc = DateTime.UtcNow
            });
        }
        var itemTemplates = FunGameService.AllItems;
        for (int i = 0; i < Math.Min(GameplayRules.StartingItems, itemTemplates.Count); i++)
        {
            var template = itemTemplates[i];
            db.ItemInstances.Add(new ItemInstance
            {
                UserId = userId, CatalogId = template.Id.ToString(CultureInfo.InvariantCulture),
                Quantity = 1, Quality = 1, PayloadJson = JsonSerializer.Serialize(new { name = template.Name }),
                AcquiredAtUtc = DateTime.UtcNow
            });
        }
        db.LedgerEntries.Add(new LedgerEntry
        {
            UserId = userId, Currency = "credits", Delta = GameplayRules.StartingCredits,
            BalanceAfter = GameplayRules.StartingCredits, Reason = "register",
            RefType = "player", RefId = userId.ToString(CultureInfo.InvariantCulture),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return state;
    }

    /// <summary>货币变动与 ledger 必须成对出现；Add 后由调用方的 SaveChanges 一次提交。</summary>
    private static void ApplyCurrency(
        BotDbContext db, PlayerState state, long delta, string currency,
        string reason, string refType, string refId)
    {
        if (delta == 0) return;
        if (currency == "diamonds") state.Diamonds += delta;
        else state.Credits += delta;
        long balance = currency == "diamonds" ? state.Diamonds : state.Credits;
        db.LedgerEntries.Add(new LedgerEntry
        {
            UserId = state.UserId, Currency = currency, Delta = delta, BalanceAfter = balance,
            Reason = reason, RefType = refType, RefId = refId, CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static async Task AddItemAsync(
        BotDbContext db, long userId, string name, int quality, int quantity, CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(new { name });
        ItemInstance? stack = await db.ItemInstances.FirstOrDefaultAsync(
            x => x.UserId == userId && x.CatalogId == "gacha" && x.Quality == quality
                 && x.PayloadJson == payload && !x.Bound && !x.Locked, ct);
        if (stack is null)
        {
            db.ItemInstances.Add(new ItemInstance
            {
                UserId = userId, CatalogId = "gacha", Quantity = quantity, Quality = quality,
                PayloadJson = payload, AcquiredAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            stack.Quantity += quantity;
        }
    }

    private static async Task<ItemInstance?> ItemByIndexAsync(
        BotDbContext db, long userId, int index, CancellationToken ct)
        => index <= 0 ? null : await db.ItemInstances
            .Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Skip(index - 1).FirstOrDefaultAsync(ct);

    private static async Task<DataCharacter?> CharacterByIndexAsync(
        BotDbContext db, long userId, int index, CancellationToken ct)
        => index <= 0 ? null : await db.Characters
            .Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Skip(index - 1).FirstOrDefaultAsync(ct);

    private static async Task<DataCharacter?> MainCharacterAsync(BotDbContext db, long userId, CancellationToken ct)
        => await db.Characters.Where(x => x.UserId == userId && x.IsMain)
            .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);

    private static void Consume(BotDbContext db, ItemInstance item, int amount)
    {
        if (item.Quantity <= amount) db.ItemInstances.Remove(item);
        else item.Quantity -= amount;
    }

    private static async Task AdvanceQuestAsync(BotDbContext db, long userId, string key, CancellationToken ct)
    {
        (string _, string _, int target) = GameplayRules.DailyQuests.First(x => x.Key == key);
        string date = GameplayRules.Today();
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

    private static int ReadProgress(DailyQuest quest)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(quest.ProgressJson);
            return document.RootElement.TryGetProperty("count", out JsonElement count)
                ? count.GetInt32() : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static string ReadName(ItemInstance item)
    {
        if (string.IsNullOrWhiteSpace(item.PayloadJson)) return $"物品 {item.CatalogId}";
        try
        {
            using JsonDocument document = JsonDocument.Parse(item.PayloadJson);
            return document.RootElement.TryGetProperty("name", out JsonElement name)
                ? name.GetString() ?? $"物品 {item.CatalogId}"
                : $"物品 {item.CatalogId}";
        }
        catch (JsonException) { return $"物品 {item.CatalogId}"; }
    }

    private static ActionResult NoSuchItem(int index)
        => ActionResult.Fail($"背包 @ {index} 不存在", "没有改动任何物品",
            "用 /背包 查看当前编号，编号按背包顺序稳定排列", "/背包");

    private static ActionResult NoSuchCharacter(int index)
        => ActionResult.Fail($"角色 @ {index} 不存在", "没有改动任何角色",
            "用 /角色 查看当前编号", "/角色");

    private static ActionResult NoMainCharacter()
        => ActionResult.Fail("没有主战角色", "没有改动任何装备",
            "用 /角色 查看角色，再用 /设主战 @编号 指定", "/角色");
}
