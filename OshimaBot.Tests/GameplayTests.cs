using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>P1 玩法仓储：真实 SQLite 内存库，验证事务、ledger 对账与 @编号 生命周期。</summary>
public sealed class GameplayTests
{
    private const long UserId = 42;

    [Fact]
    public async Task Ensure_CreatesInitialStateOnlyOnce()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();

        PlayerSnapshot? first = await players.GetSnapshotAsync(UserId);
        PlayerSnapshot? second = await players.GetSnapshotAsync(UserId);

        Assert.NotNull(first);
        Assert.Equal(GameplayRules.StartingCredits, first.Credits);
        Assert.Equal(GameplayRules.StartingDiamonds, first.Materials);
        Assert.Equal(1, first.CharacterCount);
        Assert.Equal(GameplayRules.StartingItems, first.ItemCount);
        Assert.Equal(second!.Credits, first.Credits);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(1, await context.CorePlayers.CountAsync());   // 进度与钱包同一张表
        Assert.Equal(1, await context.LedgerEntries.CountAsync(x => x.Reason == "register"));
    }

    [Fact]
    public async Task SignIn_IsIdempotentAndWritesLedger()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();

        ActionResult first = await players.SignInAsync(UserId);
        ActionResult second = await players.SignInAsync(UserId);

        Assert.True(first.Ok);
        Assert.False(second.Ok);
        Assert.Contains("已经签到", second.Title);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord state = await context.CorePlayers.FirstAsync(x => x.UserId == UserId);
        LedgerEntry entry = await context.LedgerEntries.FirstAsync(x => x.Reason == "signin");
        Assert.Equal(state.Credits, entry.BalanceAfter, 3);
        // 旧版签到是随机区间（1000–2999 + 连续加成），只断言落在范围内
        Assert.InRange(entry.Delta, GameplayRules.SignInCreditsMin,
            GameplayRules.SignInCreditsMax - 1 + GameplayRules.SignInCreditsPerDay);
        Assert.Equal(1, await context.SignInRecords.CountAsync());
        // 签到现在同时发金币与材料，各记一条流水
        Assert.True(await context.LedgerEntries.CountAsync(x => x.Reason == "signin") >= 1);
    }

    [Fact]
    public async Task Gacha_WithInsufficientDiamondsRollsBackEverything()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SetDiamondsAsync(db, 0);

        ActionResult result = await players.GachaAsync(UserId, 10, useMaterial: true);

        Assert.False(result.Ok);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(0, (await context.CorePlayers.FirstAsync(x => x.UserId == UserId)).Materials);
        Assert.Empty(await context.LedgerEntries.Where(x => x.Reason == "gacha").ToListAsync());
        Assert.Equal(GameplayRules.StartingItems, await context.CoreItems.CountAsync(x => x.UserId == UserId));
    }

    [Fact]
    public async Task Gacha_TenPullDebitsCostAndKeepsLedgerBalanced()
    {
        // 旧版抽卡是 20% 命中、十连无保底；用「必中」随机源才能稳定验证费用与入包
        using GameplayDatabase db = new(random: new AlwaysHitRandomSource());
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SetDiamondsAsync(db, 100);

        ActionResult result = await players.GachaAsync(UserId, 10, useMaterial: true);

        Assert.True(result.Ok, result.Title);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord state = await context.CorePlayers.FirstAsync(x => x.UserId == UserId);
        Assert.Equal(100 - GameplayRules.GachaTenCost, state.Materials, 3);
        LedgerEntry entry = await context.LedgerEntries.FirstAsync(x => x.Reason == "gacha");
        Assert.Equal(-GameplayRules.GachaTenCost, entry.Delta);
        Assert.Equal(state.Materials, entry.BalanceAfter, 3);
        // 每次必中，十连就是 10 件；旧版没有「保底史诗」，且产出按 8 类分配（catalogId 是物品模板 Id）
        Assert.Equal(10, await context.CoreItems.CountAsync(x => x.UserId == UserId));
    }

    [Fact]
    public async Task Bag_LockSalvageAndEquipLifecycle()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();

        // 旧版注册不带物品（0 件），这里显式发 3 件
        await TestItems.SeedAsync(db.Provider, UserId, 3);

        IReadOnlyList<BagEntry> bag = await players.GetBagAsync(UserId);
        Assert.Equal(3, bag.Count);
        Assert.Equal(1, bag[0].Index);

        Assert.True((await players.SetLockAsync(UserId, 1, true)).Ok);
        Assert.False((await players.SalvageAsync(UserId, 1, 1)).Ok);
        Assert.True((await players.SetLockAsync(UserId, 1, false)).Ok);

        PlayerSnapshot before = (await players.GetSnapshotAsync(UserId))!;
        ActionResult salvage = await players.SalvageAsync(UserId, 1, 1);
        Assert.True(salvage.Ok, salvage.Title);
        PlayerSnapshot after = (await players.GetSnapshotAsync(UserId))!;
        // 旧版分解返还材料（钻石），按品质固定 1/3/8/16/32…
        Assert.Equal(before.Credits, after.Credits, 3);
        Assert.Equal(before.Materials + GameplayRules.SalvageMaterials(1, 1), after.Materials, 3);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        LedgerEntry entry = await context.LedgerEntries.FirstAsync(x => x.Reason == "salvage");
        Assert.Equal("diamonds", entry.Currency);
        Assert.Equal(after.Materials, entry.BalanceAfter, 3);

        // ---- 装备生命周期：装备直接进 Core 的 6 槽快照，并从背包移出 ----
        long equipLocalId;
        string slotName;
        using (IServiceScope equipScope = db.Provider.CreateScope())
        {
            BotDbContext equipContext = equipScope.ServiceProvider.GetRequiredService<BotDbContext>();
            List<CoreItemRecord> rows = await equipContext.CoreItems
                .Where(x => x.UserId == UserId).OrderBy(x => x.LocalId).ToListAsync();
            CoreItemRecord equippable = rows.First(r => r.ItemType <= (int)FunGame.Core.Library.Constant.ItemType.Accessory);
            equipLocalId = equippable.LocalId;
            // 模板的 EquipSlotType 恒为 None（装备时才写入），槽位要按 ItemType 推。
            slotName = equippable.ItemType switch
            {
                (int)FunGame.Core.Library.Constant.ItemType.Weapon => "Weapon",
                (int)FunGame.Core.Library.Constant.ItemType.Armor => "Armor",
                (int)FunGame.Core.Library.Constant.ItemType.Shoes => "Shoes",
                (int)FunGame.Core.Library.Constant.ItemType.Accessory => "Accessory1",
                _ => "MagicCardPack"
            };
        }

        int bagBeforeEquip = (await players.GetBagAsync(UserId)).Count;
        int equipIndex = (await players.GetBagAsync(UserId)).First(x => x.Id == equipLocalId).Index;
        Assert.True((await players.EquipAsync(UserId, equipIndex)).Ok);
        Assert.Equal(bagBeforeEquip - 1, (await players.GetBagAsync(UserId)).Count);
        Assert.Equal(1, Assert.Single(await players.GetCharactersAsync(UserId)).EquipmentCount);

        Assert.True((await players.UnequipAsync(UserId, slotName)).Ok);
        IReadOnlyList<BagEntry> restored = await players.GetBagAsync(UserId);
        Assert.Equal(bagBeforeEquip, restored.Count);
        Assert.True((await players.SalvageAsync(UserId, restored.Count, 1)).Ok);
    }

    [Fact]
    public async Task Train_RequiresExpiryThenGrantsExpForLevelUp()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();

        Assert.True((await players.TrainStartAsync(UserId, 1, 1)).Ok);
        Assert.False((await players.TrainClaimAsync(UserId)).Ok); // 尚未结束
        Assert.False((await players.TrainStartAsync(UserId, 1, 1)).Ok); // 不能重复开练

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            TrainingSession session = await context.TrainingSessions.FirstAsync(x => x.UserId == UserId);
            // 旧版按「开始 → 收取」的实际间隔算经验（2/分钟），所以起止都要放到过去
            session.StartedAtUtc = DateTime.UtcNow.AddMinutes(-11);
            session.EndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }

        Assert.True((await players.TrainClaimAsync(UserId)).Ok);

        // Core 模型：经验记在角色身上，够了自动升级（不再有旅人经验中转）。
        IReadOnlyList<CharacterEntry> characters = await players.GetCharactersAsync(UserId);
        CharacterEntry afterTrain = Assert.Single(characters);
        Assert.True(afterTrain.Level > 1 || afterTrain.Exp > 0,
            $"练级后应有经验或升级，实际 Lv.{afterTrain.Level} / Exp {afterTrain.Exp}");
    }

    [Fact]
    public async Task Squad_AcceptsFourMembersAndRejectsTheFifth()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();

        // 建档即送 1 个自建角色；再造 4 个备用，凑够 5 个角色用来验证上限
        Assert.True((await players.SquadAsync(UserId, "", 0)).Ok);
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            for (int i = 0; i < GameplayRules.SquadSize; i++)
            {
                CharacterStore.CreateCustom(context, UserId, $"备用{i + 1}");
            }
            await context.SaveChangesAsync();
        }

        for (int index = 1; index <= GameplayRules.SquadSize; index++)
        {
            ActionResult joined = await players.SquadAsync(UserId, "add", index);
            Assert.True(joined.Ok, $"第 {index} 名角色应能加入小队：{joined.Title}");
        }
        Assert.Equal(GameplayRules.SquadSize, (await players.GetSnapshotAsync(UserId))!.SquadCount);

        // 旧版「请先设置 1-4 名角色」：第 5 人必须被拒绝，且不写入成员表
        ActionResult overflow = await players.SquadAsync(UserId, "add", GameplayRules.SquadSize + 1);
        Assert.False(overflow.Ok);
        Assert.Contains("已满", overflow.Title);
        Assert.Equal(GameplayRules.SquadSize, (await players.GetSnapshotAsync(UserId))!.SquadCount);

        // 编成页要列出全部 4 个位置（含空位），不能只画 3 个
        ActionResult view = await players.SquadAsync(UserId, "", 0);
        Assert.True(view.Ok);
        Assert.Contains($"{GameplayRules.SquadSize} 人", view.Status);
        Assert.Equal(GameplayRules.SquadSize, view.Lines.Count(x => x.StartsWith("- @", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Squad_MainAndQuestRewardsArePersistent()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();

        Assert.True((await players.SquadAsync(UserId, "", 0)).Ok);
        Assert.True((await players.SquadAsync(UserId, "add", 1)).Ok);
        Assert.False((await players.SquadAsync(UserId, "add", 1)).Ok);
        Assert.True((await players.SquadAsync(UserId, "remove", 1)).Ok);
        Assert.Equal(0, (await players.GetSnapshotAsync(UserId))!.SquadCount);

        Assert.True((await players.SignInAsync(UserId)).Ok);
        IReadOnlyList<QuestEntry> quests = await players.GetQuestsAsync(UserId);
        Assert.Equal(1, quests[0].Status);
        Assert.Equal(0, quests[1].Status);

        PlayerSnapshot before = (await players.GetSnapshotAsync(UserId))!;
        Assert.True((await players.ClaimQuestAsync(UserId, 1)).Ok);
        Assert.False((await players.ClaimQuestAsync(UserId, 1)).Ok); // 不能重复领
        Assert.False((await players.ClaimQuestAsync(UserId, 2)).Ok); // 未完成
        PlayerSnapshot after = (await players.GetSnapshotAsync(UserId))!;
        Assert.Equal(before.Credits + GameplayRules.QuestRewardCredits, after.Credits);
        Assert.Equal(before.Materials + GameplayRules.QuestRewardDiamonds, after.Materials);
        Assert.Equal(2, (await players.GetQuestsAsync(UserId))[0].Status);
    }

    [Fact]
    public async Task SimulationProgress_AdvancesQuest()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();

        Assert.True((await players.RecordSimulationAsync(UserId)).Ok);
        Assert.Equal(1, (await players.GetQuestsAsync(UserId))[1].Status);
        Assert.True((await players.ClaimQuestAsync(UserId, 2)).Ok);
    }

    [Fact]
    public async Task NoIdentity_NeverWritesPlayerState()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();

        Assert.False((await players.SignInAsync(0)).Ok);
        Assert.False((await players.GachaAsync(0, 1, useMaterial: true)).Ok);
        Assert.Null(await players.GetSnapshotAsync(0));
        Assert.Empty(await players.GetBagAsync(0));

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(0, await context.CorePlayers.CountAsync());
        Assert.Equal(0, await context.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task ApiKey_LazilyBindsIndependentInternalUsers()
    {
        using GameplayDatabase db = new();
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            context.ApiKeys.AddRange(
                new ApiKey { Name = "a", KeyHash = ApiKeyDefaults.Hash("key-a"), ScopesJson = "[]" },
                new ApiKey { Name = "b", KeyHash = ApiKeyDefaults.Hash("key-b"), ScopesJson = "[]" });
            await context.SaveChangesAsync();
        }

        ApiKeyAuthenticator authenticator = db.Get<ApiKeyAuthenticator>();
        ApiKeyIdentity? a1 = await authenticator.AuthenticateAsync("key-a");
        ApiKeyIdentity? b1 = await authenticator.AuthenticateAsync("key-b");
        ApiKeyIdentity? a2 = await authenticator.AuthenticateAsync("key-a");

        Assert.NotNull(a1);
        Assert.NotNull(b1);
        Assert.NotNull(a2);
        Assert.True(a1.UserId > 0);
        Assert.True(b1.UserId > 0);
        Assert.NotEqual(a1.UserId, b1.UserId);
        Assert.Equal(a1.UserId, a2.UserId);
        Assert.Equal(BotRole.Player, Assert.Single(a1.Roles));

        using IServiceScope verifyScope = db.Provider.CreateScope();
        BotDbContext verifyContext = verifyScope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(2, await verifyContext.Users.CountAsync());
        Assert.Equal(2, await verifyContext.UserIdentities.CountAsync(x => x.Provider == "apikey"));
        Assert.Equal(0, await verifyContext.ApiKeys.CountAsync(x => x.UserId == null));
    }

    [Fact]
    public void Rules_AreDeterministicAndBounded()
    {
        Assert.Equal(GameplayRules.Fortune(7, "2026-11-10"), GameplayRules.Fortune(7, "2026-11-10"));
        Assert.Contains(GameplayRules.Fortune(8, "2026-11-10"), GameplayRules.FortuneTexts);
        int? firstBreak = GameplayRules.NextBreakLevel(0);
        Assert.NotNull(firstBreak);
        Assert.True(firstBreak > 1);
        Assert.True(GameplayRules.NextBreakLevel(1) > firstBreak);   // 阶数越高，所需等级越高
        Assert.InRange(GameplayRules.RollGachaQuality(new SeededRandomSource(1)), 1, 6);
    }

    [Fact]
    public async Task Break_ConsumesLegacyMaterialsAndRefusesWhenShort()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);

        // Core 语义：LevelBreak 从 -1 起算，第一次突破是 -1 → 0，取 0 档材料（钻石 40 + 升华之印 2）。
        int firstNeedLevel = GameplayRules.NextBreakLevel(-1)!.Value;
        Assert.Equal(10, firstNeedLevel);

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CoreCharacterRecord row = await context.CoreCharacters.SingleAsync(x => x.UserId == UserId);
            Assert.Equal(-1, row.LevelBreak);        // 建档后还没突破过

            PlayerCharacter character = new(row, System.Text.Json.JsonSerializer.Deserialize<FunGame.Core.Entity.Character>(
                row.SnapshotJson, FunGame.Core.Api.JsonTool.JsonSerializerOptions)!);
            character.Level = firstNeedLevel;
            CharacterStore.Sync(character);

            CorePlayerRecord wallet = await context.CorePlayers.SingleAsync(x => x.UserId == UserId);
            wallet.Materials = 0;
            await context.SaveChangesAsync();
        }

        // 材料不足：拒绝并且不扣任何东西（事务回滚），突破进度不变。
        ActionResult refused = await players.BreakAsync(UserId, 1);
        Assert.False(refused.Ok);
        Assert.Contains("突破材料不足", refused.Title);

        // 补齐「第一次突破」的需求：40 钻石 + 2 升华之印（旧版 LevelBreakNeedyList[0]）。
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.SingleAsync(x => x.UserId == UserId);
            wallet.Materials = 40;
            await ItemStore.AddAsync(context, UserId, "升华之印", 1, 2, CancellationToken.None, "18001");
            await context.SaveChangesAsync();
        }

        ActionResult ok = await players.BreakAsync(UserId, 1);
        Assert.True(ok.Ok, ok.Title);

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.SingleAsync(x => x.UserId == UserId);
            Assert.Equal(0, wallet.Materials, 6);
            Assert.Equal(0, await context.CoreItems.CountAsync(x => x.CatalogId == "18001"));

            CoreCharacterRecord row = await context.CoreCharacters.SingleAsync(x => x.UserId == UserId);
            Assert.Equal(0, row.LevelBreak);         // -1 → 0，只升一阶
            Assert.Contains(await context.LedgerEntries.ToListAsync(),
                x => x.Reason == "break" && x.Currency == "diamonds" && x.Delta == -40);
        }
    }

    [Fact]
    public async Task UpgradeNormalAttackAndSkillUseLegacyCosts()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);

        // 1) 预览不扣材料
        PlayerSnapshot walletBefore = (await players.GetSnapshotAsync(UserId))!;
        ActionResult preview = await players.UpgradeNormalAttackAsync(UserId, 1, preview: true);
        Assert.True(preview.Ok, preview.Title);
        Assert.Contains(preview.Lines, line => line.Contains("金币 2000"));

        // 2) 条件不足时拒绝（普攻 2 级要求角色 Lv.8，起始 Lv.1）
        ActionResult denied = await players.UpgradeNormalAttackAsync(UserId, 1, preview: false);
        Assert.False(denied.Ok);
        Assert.Contains("角色等级", string.Join(" ", denied.Lines));

        PlayerSnapshot walletAfter = (await players.GetSnapshotAsync(UserId))!;
        Assert.Equal(walletBefore.Credits, walletAfter.Credits, 3);
        Assert.Equal(walletBefore.Materials, walletAfter.Materials, 3);

        // 3) 抬到 Lv.8 + 备齐 2000 金币 / 30 钻石 / 1 个技能卷轴，升级应成功
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CoreCharacterRecord row = await context.CoreCharacters.SingleAsync(x => x.UserId == UserId);
            PlayerCharacter character = new(row, System.Text.Json.JsonSerializer.Deserialize<FunGame.Core.Entity.Character>(
                row.SnapshotJson, FunGame.Core.Api.JsonTool.JsonSerializerOptions)!);
            character.Level = 8;
            CharacterStore.Sync(character);

            CorePlayerRecord wallet = await context.CorePlayers.SingleAsync(x => x.UserId == UserId);
            wallet.Credits = 2000;
            wallet.Materials = 30;
            await ItemStore.AddAsync(context, UserId, "技能卷轴", 1, 1, CancellationToken.None, "18004");
            await context.SaveChangesAsync();
        }

        ActionResult upgraded = await players.UpgradeNormalAttackAsync(UserId, 1, preview: false);
        Assert.True(upgraded.Ok, upgraded.Title);

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.SingleAsync(x => x.UserId == UserId);
            Assert.Equal(0, wallet.Credits, 3);
            Assert.Equal(0, wallet.Materials, 3);
            Assert.Equal(0, await context.CoreItems.CountAsync(x => x.CatalogId == "18004"));

            CoreCharacterRecord row = await context.CoreCharacters.SingleAsync(x => x.UserId == UserId);
            FunGame.Core.Entity.Character core =
                System.Text.Json.JsonSerializer.Deserialize<FunGame.Core.Entity.Character>(
                    row.SnapshotJson, FunGame.Core.Api.JsonTool.JsonSerializerOptions)!;
            Assert.Equal(2, core.NormalAttack.Level);
        }
    }

    [Fact]
    public async Task RenameCharacterQueuesForReview()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);

        Assert.False((await players.RenameAsync(UserId, " ")).Ok);
        Assert.False((await players.RenameAsync(UserId, "超长的名字超过十二个字符了")).Ok);

        ActionResult ok = await players.RenameAsync(UserId, "新名字");
        Assert.True(ok.Ok, ok.Title);

        // 角色名是公开内容：提交只进队列，审核通过前名字不变（2026-11-12 用户要求）
        Assert.NotEqual("新名字", Assert.Single(await players.GetCharactersAsync(UserId)).Name);
        Assert.Equal("新名字", Assert.Single(await players.MyRenameRequestsAsync(UserId)).RequestedName);
    }

    [Fact]
    public void QualityTiers_MapToCoreQualityTypes()
    {
        Assert.Equal(FunGame.Core.Library.Constant.QualityType.White, GameplayRules.QualityTypeOf(1));
        Assert.Equal(FunGame.Core.Library.Constant.QualityType.Orange, GameplayRules.QualityTypeOf(5));
        Assert.Equal("普通", GameplayRules.QualityName(1));
        Assert.Equal("传说", GameplayRules.QualityName(5));

        for (int tier = 1; tier <= 5; tier++)
        {
            Assert.Equal(tier, GameplayRules.QualityTierOf(GameplayRules.QualityTypeOf(tier)));
        }
    }

    [Fact]
    public async Task Wallet_LivesInCorePlayersAndKeepsFractionalBalance()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == UserId);
            // Core 的货币是 double，这里写小数验证不会被整数截断
            wallet.Credits = 1234.75;
            wallet.Materials = 6.5;
            await context.SaveChangesAsync();

            // 进度表已整表并入 CorePlayers（钱包 + 签到同一行）
            Assert.Null(context.Model.FindEntityType("OshimaBot.Data.Entities.PlayerState"));
        }

        PlayerSnapshot snapshot = (await players.GetSnapshotAsync(UserId))!;
        Assert.Equal(1234.75, snapshot.Credits, 3);
        Assert.Equal(6.5, snapshot.Materials, 3);
    }

    [Fact]
    public async Task Training_UsesInjectedClockForCooldown()
    {
        FakeClock clock = new(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc));
        using GameplayDatabase db = new(clock);
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);

        Assert.True((await players.TrainStartAsync(UserId, 1, 10)).Ok);
        // 未到结束时间：不能结算
        Assert.False((await players.TrainClaimAsync(UserId)).Ok);

        clock.Advance(TimeSpan.FromMinutes(11));
        ActionResult claim = await players.TrainClaimAsync(UserId);
        Assert.True(claim.Ok, claim.Title);
    }

    [Fact]
    public async Task Gacha_UsesInjectedRandomSource()
    {
        static async Task<string> RunAsync(int seed)
        {
            using GameplayDatabase db = new(random: new SeededRandomSource(seed));
            IPlayerService players = db.Get<IPlayerService>();
            await players.GetSnapshotAsync(UserId);
            await SetDiamondsCoreAsync(db, 100);
            Assert.True((await players.GachaAsync(UserId, 10, useMaterial: true)).Ok);
            using IServiceScope scope = db.Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            List<string> names = await context.CoreItems.AsNoTracking()
                .OrderBy(x => x.LocalId).Select(x => x.CatalogId + ":" + x.Quality).ToListAsync();
            return string.Join("|", names);
        }

        Assert.Equal(await RunAsync(2026), await RunAsync(2026));
        Assert.NotEqual(await RunAsync(2026), await RunAsync(7));
    }

    [Fact]
    public async Task Characters_LiveInCoreSnapshotsAndPersistLevel()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);

        string guid;
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CoreCharacterRecord row = await context.CoreCharacters.SingleAsync(x => x.UserId == UserId);
            Assert.Equal(1, row.Level);
            Assert.False(string.IsNullOrWhiteSpace(row.SnapshotJson));
            guid = row.Guid;

            CorePlayerRecord wallet = await context.CorePlayers.SingleAsync(x => x.UserId == UserId);
            Assert.Equal(guid, wallet.MainCharacterGuid);   // 建档即设为主战

            // 直接把经验写进角色快照（Core 模型下经验属于角色）
            CoreCharacterRecord progress = await context.CoreCharacters.SingleAsync(x => x.UserId == UserId);
            PlayerCharacter mutable = new(progress,
                System.Text.Json.JsonSerializer.Deserialize<FunGame.Core.Entity.Character>(
                    progress.SnapshotJson, FunGame.Core.Api.JsonTool.JsonSerializerOptions)!);
            mutable.Exp += (long)FunGame.Core.Library.Constant.General.GameplayEquilibriumConstant.EXPUpperLimit[1];
            CharacterStore.Sync(mutable);
            await context.SaveChangesAsync();
        }

        Assert.True((await players.LevelUpAsync(UserId, 1)).Ok);

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            // 索引列与快照都要更新
            CoreCharacterRecord row = await context.CoreCharacters.SingleAsync(x => x.UserId == UserId);
            Assert.Equal(2, row.Level);

            CorePlayerRecord wallet = await context.CorePlayers.SingleAsync(x => x.UserId == UserId);
            List<PlayerCharacter> loaded = await CharacterStore.ListAsync(
                context, UserId, wallet.MainCharacterGuid, CancellationToken.None);
            PlayerCharacter reloaded = Assert.Single(loaded);
            Assert.Equal(guid, reloaded.Guid);
            Assert.Equal(2, reloaded.Level);
            Assert.True(reloaded.IsMain);
        }
    }

    private static async Task SetDiamondsCoreAsync(GameplayDatabase db, int diamonds)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == UserId);
        wallet.Materials = diamonds;
        await context.SaveChangesAsync();
    }

    private static async Task SetDiamondsAsync(GameplayDatabase db, int diamonds)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord state = await context.CorePlayers.FirstAsync(x => x.UserId == UserId);
        state.Materials = diamonds;
        await context.SaveChangesAsync();
    }

    private sealed class GameplayDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public GameplayDatabase(IGameClock? clock = null, IRandomSource? random = null)
        {
            FunGameRuntime.EnsureInitialized();
            Connection = new SqliteConnection("Data Source=:memory:");
            Connection.Open();
            ServiceCollection services = new();
            services.AddLogging();
            services.AddDbContext<BotDbContext>(options => options.UseSqlite(Connection));
            services.AddSingleton(Options.Create(new ServerOptions()));
            services.AddSingleton(Options.Create(new DatabaseOptions { ApplyMigrationsOnStartup = false }));
            services.AddSingleton<IUserDirectory, UserDirectory>();
            services.AddSingleton<ApiKeyAuthenticator>();
            services.AddGameplayRuntime(clock, random);
            services.AddSingleton<IPlayerService, PlayerService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
