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
public sealed class PlayerService(
    IServiceScopeFactory scopeFactory,
    ILogger<PlayerService> logger,
    IGameClock clock,
    IRandomSource random) : IPlayerService
{
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
            return ActionResult.Success("📅 签到成功",
                $"连续 {state.SignInStreak} 天 · 金币 +{credits}{(materials > 0 ? $" · 钻石 +{materials}" : "")}",
                [
                    $"- 当前金币：{state.Credits}　钻石：{state.Materials}",
                    "- 旧版规则：金币 1000–2999，钻石 5–14，连续天数越高加成越多",
                    "- 今日任务「今日签到」进度已更新"
                ], ["/每日任务", "/状态", "/抽卡"]);
        }, cancellationToken);

    public Task<ActionResult> GachaAsync(long userId, int count, CancellationToken cancellationToken = default)
        => MutateAsync(userId, async (db, state, ct) =>
        {
            int pulls = Math.Clamp(count, 1, GameplayRules.MaxGachaPerCall);
            int cost = pulls == 10 ? GameplayRules.GachaTenCost : GameplayRules.GachaCost * pulls;
            if (state.Materials < cost)
            {
                return ActionResult.Fail("钻石不足", "没有扣除钻石，也没有生成物品",
                    $"需要 {cost} 钻石，当前 {state.Materials}；可先签到或领取每日任务", "/签到");
            }
            ApplyCurrency(db, state, -cost, "diamonds", "gacha", "gacha", clock.Today());

            var catalog = FunGameService.AllItems;
            List<(string Name, int Quality)> drawn = [];
            int hits = 0;
            for (int i = 0; i < pulls; i++)
            {
                // 旧版：每次独立掷 NextDouble()，> 0.8 才中奖；十连无保底、可能全空。
                if (!GameplayRules.RollGachaHit(random)) continue;
                hits++;
                int quality = GameplayRules.RollGachaQuality(random);
                string name = catalog.Count == 0
                    ? "无名的遗物"
                    : catalog[random.Next(catalog.Count)].Name;
                drawn.Add((name, quality));
            }
            foreach ((string name, int quality) in drawn)
            {
                await AddItemAsync(db, userId, name, quality, 1, ct);
            }
            await AdvanceQuestAsync(db, userId, "gacha", ct);

            List<string> lines = [.. drawn.Select((item, i) =>
                $"- {i + 1}. {GameplayRules.QualityName(item.Quality)} · {item.Name}")];
            if (hits == 0) lines.Add($"- {pulls} 次都没抽中，什么也没得到");
            lines.Add($"- 消耗钻石 {cost}，剩余 {state.Materials}（命中 {hits}/{pulls}）");
            lines.Add("- 用 /背包 查看物品编号后再 @编号 使用或分解");
            return ActionResult.Success(pulls == 10 ? "🎰 十连抽卡" : "🎲 抽卡",
                hits == 0 ? "什么也没抽中" : $"抽中 {hits} 件 · 已放入背包",
                lines, ["/背包", "/状态", "/抽卡"]);
        }, cancellationToken);

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
                PlayerCharacter? character = member is null ? null : characters.FirstOrDefault(x => x.Id == member.CharacterId);
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

        var characterTemplates = FunGameService.Characters;
        if (characterTemplates.Count > 0)
        {
            var template = characterTemplates[0];
            PlayerCharacter starter = CharacterStore.Create(
                db, userId, template, template.Id.ToString(CultureInfo.InvariantCulture));
            wallet.MainCharacterGuid = starter.Guid;
        }
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

    /// <summary>货币变动与 ledger 必须成对出现；Add 后由调用方的 SaveChanges 一次提交。</summary>
    internal static void ApplyCurrency(
        BotDbContext db, PlayerAccount account, double delta, string currency,
        string reason, string refType, string refId)
    {
        if (delta == 0) return;
        if (currency == "diamonds") account.Materials += delta;
        else account.Credits += delta;
        double balance = currency == "diamonds" ? account.Materials : account.Credits;
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
