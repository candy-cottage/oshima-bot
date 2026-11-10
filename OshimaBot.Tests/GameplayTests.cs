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
        Assert.Equal(1, await context.PlayerStates.CountAsync());
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
        Assert.Equal(GameplayRules.SignInCredits(0), entry.Delta);
        Assert.Equal(1, await context.SignInRecords.CountAsync());
        Assert.Equal(1, await context.LedgerEntries.CountAsync(x => x.Reason == "signin"));
    }

    [Fact]
    public async Task Gacha_WithInsufficientDiamondsRollsBackEverything()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SetDiamondsAsync(db, 0);

        ActionResult result = await players.GachaAsync(UserId, 10);

        Assert.False(result.Ok);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(0, (await context.CorePlayers.FirstAsync(x => x.UserId == UserId)).Materials);
        Assert.Empty(await context.LedgerEntries.Where(x => x.Reason == "gacha").ToListAsync());
        Assert.Equal(GameplayRules.StartingItems, await context.ItemInstances.CountAsync(x => x.UserId == UserId));
    }

    [Fact]
    public async Task Gacha_TenPullDebitsDiscountAndKeepsLedgerBalanced()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SetDiamondsAsync(db, 100);

        ActionResult result = await players.GachaAsync(UserId, 10);

        Assert.True(result.Ok, result.Title);
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord state = await context.CorePlayers.FirstAsync(x => x.UserId == UserId);
        Assert.Equal(100 - GameplayRules.GachaTenCost, state.Materials, 3);
        LedgerEntry entry = await context.LedgerEntries.FirstAsync(x => x.Reason == "gacha");
        Assert.Equal(-GameplayRules.GachaTenCost, entry.Delta);
        Assert.Equal(state.Materials, entry.BalanceAfter, 3);
        Assert.True(await context.ItemInstances.CountAsync(x => x.UserId == UserId) > GameplayRules.StartingItems);
        Assert.Contains(await context.ItemInstances.Where(x => x.CatalogId == "gacha").ToListAsync(),
            item => item.Quality >= 4); // 十连保底
    }

    [Fact]
    public async Task Bag_LockSalvageAndEquipLifecycle()
    {
        using GameplayDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();

        IReadOnlyList<BagEntry> bag = await players.GetBagAsync(UserId);
        Assert.Equal(GameplayRules.StartingItems, bag.Count);
        Assert.Equal(1, bag[0].Index);

        Assert.True((await players.SetLockAsync(UserId, 1, true)).Ok);
        Assert.False((await players.SalvageAsync(UserId, 1, 1)).Ok);
        Assert.True((await players.SetLockAsync(UserId, 1, false)).Ok);

        PlayerSnapshot before = (await players.GetSnapshotAsync(UserId))!;
        ActionResult salvage = await players.SalvageAsync(UserId, 1, 1);
        Assert.True(salvage.Ok, salvage.Title);
        PlayerSnapshot after = (await players.GetSnapshotAsync(UserId))!;
        Assert.Equal(before.Credits + GameplayRules.SalvageValue(1, 1), after.Credits);

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        LedgerEntry entry = await context.LedgerEntries.FirstAsync(x => x.Reason == "salvage");
        Assert.Equal(after.Credits, entry.BalanceAfter);

        Assert.True((await players.EquipAsync(UserId, 2)).Ok);
        Assert.True((await players.GetBagAsync(UserId))[1].Equipped);
        Assert.False((await players.EquipAsync(UserId, 2)).Ok); // 已绑定，不能重复装备
        Assert.False((await players.SalvageAsync(UserId, 2, 1)).Ok); // 绑定物品不可分解
        Assert.True((await players.UnequipAsync(UserId, "weapon")).Ok);
        Assert.False((await players.GetBagAsync(UserId))[1].Equipped);
        Assert.True((await players.SalvageAsync(UserId, 2, 1)).Ok);
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
        Assert.False((await players.GachaAsync(0, 1)).Ok);
        Assert.Null(await players.GetSnapshotAsync(0));
        Assert.Empty(await players.GetBagAsync(0));

        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        Assert.Equal(0, await context.PlayerStates.CountAsync());
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
        Assert.InRange(GameplayRules.RollQuality(new SeededRandomSource(1)), 1, 5);
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

            // 进度表已不再持有货币列
            Assert.False(context.Model.FindEntityType(typeof(PlayerState))!
                .FindProperty("Credits") is not null);
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
            Assert.True((await players.GachaAsync(UserId, 10)).Ok);
            using IServiceScope scope = db.Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            List<string> names = await context.ItemInstances.AsNoTracking()
                .OrderBy(x => x.Id).Select(x => x.PayloadJson).ToListAsync();
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
