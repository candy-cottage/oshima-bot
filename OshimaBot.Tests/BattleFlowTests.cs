using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;
using OshimaBot.Game.Combat;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 战斗入口补齐：完整决斗（PvP）/ 小队决斗 / 指定编号 Boss / 小队讨伐。
/// <para>BossPool 是进程级全服状态，所以整类挂在 <see cref="BossPoolCollection"/> 下串行执行。</para>
/// </summary>
[Collection(BossPoolCollection.Name)]
public sealed class BattleFlowTests
{
    private const long Me = 42;
    private const long Rival = 77;

    /// <summary>BossPool 依赖内容包（角色模板），第一次碰它之前必须先把运行时初始化好。</summary>
    static BattleFlowTests() => FunGameRuntime.EnsureInitialized();

    [Fact]
    public async Task CombatUnits_CarryEquipmentAndSkillLevels()
    {
        using BattleDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        IEconomyService economy = db.Get<IEconomyService>();
        _ = economy;
        await db.CreateSaveAsync(players, Me, "我");

        // 给主战穿一件武器、把绝招点到 3 级
        Item weapon = FunGameService.AllItems.First(i => i.ItemType == ItemType.Weapon);
        string superName;
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == Me);
            PlayerCharacter main = (await CharacterStore.ListAsync(context, Me, wallet.MainCharacterGuid, default))[0];
            Assert.NotEmpty(main.Core.Skills);                       // 建档即挂签名技能
            Skill super = main.Core.Skills.First(x => x.SkillType == SkillType.SuperSkill);
            super.Level = 3;
            superName = super.Name;
            main.Core.Equip(weapon.Copy(), out Item? _);
            CharacterStore.Sync(main);
            await context.SaveChangesAsync();
        }

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == Me);
            PlayerCharacter main = (await CharacterStore.ListAsync(context, Me, wallet.MainCharacterGuid, default))[0];

            // 模板重建（旧口径）拿不到装备与技能；快照口径两者都在
            Character plain = CombatCharacterFactory.FromCatalog(main.CatalogId, main.Level)!;
            Character battle = CombatUnits.FromPlayer(main)!;

            Assert.True(battle.ATK > plain.ATK, $"装备应计入攻击力：{battle.ATK} vs {plain.ATK}");
            Skill super = Assert.Single(battle.Skills, x => x.SkillType == SkillType.SuperSkill);
            Assert.Equal(superName, super.Name);
            Assert.Equal(3, super.Level);
            Assert.Equal(battle.MaxHP, battle.HP, 3);                 // 满血进场
            Assert.Equal(200, battle.EP, 3);                         // 旧版 Recovery(EP: 200)
        }

        // 技能有等级了，/技能升级 的预览就能读到它
        ActionResult preview = await players.UpgradeSkillAsync(Me, 1, superName, preview: true);
        Assert.True(preview.Ok, preview.Title);
    }

    [Fact]
    public async Task DuelWith_RunsRealPvpAndReportsTarget()
    {
        using BattleDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreateSaveAsync(players, Me, "挑战者");
        await db.CreateSaveAsync(players, Rival, "应战者");

        BattleReport report = await economy.DuelWithAsync(Me, Rival.ToString());

        Assert.True(report.Result.Ok, report.Result.Title);
        Assert.NotEmpty(report.Log);                                  // 逐回合战报要给得出来
        Assert.Contains("应战者", string.Join(" ", report.Result.Lines));
    }

    [Fact]
    public async Task DuelWith_RejectsSelfAndUnknownTarget()
    {
        using BattleDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreateSaveAsync(players, Me, "自己");

        BattleReport self = await economy.DuelWithAsync(Me, Me.ToString());
        Assert.False(self.Result.Ok);
        Assert.Contains("不能和自己打", self.Result.Title);

        BattleReport unknown = await economy.DuelWithAsync(Me, "查无此人");
        Assert.False(unknown.Result.Ok);
        Assert.Contains("找不到对方", unknown.Result.Title);

        // 昵称也能定位对手
        await db.CreateSaveAsync(players, Rival, "昵称对手");
        BattleReport byNickname = await economy.DuelWithAsync(Me, "昵称对手");
        Assert.True(byNickname.Result.Ok, byNickname.Result.Title);
    }

    [Fact]
    public async Task TeamDuel_NeedsSquadsOnBothSides()
    {
        using BattleDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreateSaveAsync(players, Me, "我方");
        await db.CreateSaveAsync(players, Rival, "对方");

        BattleReport withoutSquad = await economy.TeamDuelWithAsync(Me, Rival.ToString());
        Assert.False(withoutSquad.Result.Ok);
        Assert.Contains("小队", withoutSquad.Result.Title);

        Assert.True((await players.SquadAsync(Me, "add", 1)).Ok);
        BattleReport opponentMissing = await economy.TeamDuelWithAsync(Me, Rival.ToString());
        Assert.False(opponentMissing.Result.Ok);
        Assert.Contains("对方尚未编成小队", opponentMissing.Result.Title);

        Assert.True((await players.SquadAsync(Rival, "add", 1)).Ok);
        BattleReport report = await economy.TeamDuelWithAsync(Me, Rival.ToString());
        Assert.True(report.Result.Ok, report.Result.Title);
        Assert.NotEmpty(report.Log);
    }

    [Fact]
    public async Task BossAt_FightsTheRequestedTargetAndSharesHp()
    {
        BossPool.Reset();
        using BattleDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreateSaveAsync(players, Me, "讨伐者");

        BossEntry target = BossPool.All.OrderBy(x => x.Level).First();
        double before = target.Character.HP;

        ActionResult result = await economy.BossAtAsync(Me, target.Index);

        Assert.True(result.Ok, result.Title);
        Assert.Contains($"{target.Index}", string.Join(" ", result.Lines));
        Assert.True(target.Character.HP <= before, "共享血量应只减不增");

        ActionResult missing = await economy.BossAtAsync(Me, 9999);
        Assert.False(missing.Ok);
        Assert.Contains("9999", missing.Title);
    }

    [Fact]
    public async Task BossTeam_RequiresSquadAndReturnsBattleLog()
    {
        BossPool.Reset();
        using BattleDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        IEconomyService economy = db.Get<IEconomyService>();
        await db.CreateSaveAsync(players, Me, "队长");

        int index = BossPool.All.Min(x => x.Index);
        BattleReport withoutSquad = await economy.BossTeamAsync(Me, index);
        Assert.False(withoutSquad.Result.Ok);
        Assert.Contains("没有小队编成", withoutSquad.Result.Title);

        Assert.True((await players.SquadAsync(Me, "add", 1)).Ok);
        BattleReport report = await economy.BossTeamAsync(Me, index);
        Assert.True(report.Result.Ok, report.Result.Title);
        Assert.NotEmpty(report.Log);
        Assert.Contains("全服共享", string.Join(" ", report.Result.Lines));
    }

    private sealed class BattleDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public BattleDatabase()
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
            services.AddGameplayRuntime();
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<IEconomyService, EconomyService>();
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        /// <summary>建存档（懒建档）+ 补一条 <c>Users</c> 行，让「按 UID / 昵称找对手」能命中。</summary>
        public async Task CreateSaveAsync(IPlayerService players, long userId, string nickname)
        {
            await players.GetSnapshotAsync(userId);
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            context.Users.Add(new OshimaBot.Data.Entities.User { Id = userId, Handle = $"handle{userId}", Nickname = nickname });
            await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
