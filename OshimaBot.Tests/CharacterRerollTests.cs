using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 自建角色（注册即送，非内容包预制）与它的属性重随（旧版 <c>RandomCustomCharacter</c>）。
/// <para>重随是「扣 3 钻摇一组 → 预览 → 确认 / 取消」的三段式，中间态落在 ActivityProgress。</para>
/// </summary>
public sealed class CharacterRerollTests
{
    private const long Player = 42;

    [Fact]
    public async Task Register_CreatesCustomCharacterNamedAfterNickname()
    {
        using RerollDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await db.CreatePlayerAsync(Player, "旅人小明");

        IReadOnlyList<CharacterEntry> characters = await players.GetCharactersAsync(Player);
        CharacterEntry main = Assert.Single(characters);

        Assert.Equal("旅人小明", main.Name);
        Assert.True(main.IsMain);
        Assert.Equal(1, main.Level);
        Assert.Equal(-1, main.BreakLevel);
    }

    [Fact]
    public async Task Reroll_PreviewChargesOnceThenConfirmApplies()
    {
        using RerollDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await db.CreatePlayerAsync(Player, "重随者");
        await db.SetDiamondsAsync(Player, 10);

        ActionResult preview = await players.RerollPreviewAsync(Player);
        Assert.True(preview.Ok, preview.Title);
        Assert.Contains("核心属性", string.Join(" ", preview.Lines));
        Assert.Equal(10 - GameplayRules.CustomRerollCost, await db.DiamondsAsync(Player), 3);

        // 有预览时再摇一次不会重复扣钻，只把已有的那组摆出来
        ActionResult again = await players.RerollPreviewAsync(Player);
        Assert.False(again.Ok);
        Assert.Contains("待确认", again.Title);
        Assert.Equal(10 - GameplayRules.CustomRerollCost, await db.DiamondsAsync(Player), 3);

        ActionResult confirm = await players.RerollConfirmAsync(Player);
        Assert.True(confirm.Ok, confirm.Title);
        Assert.Equal(10 - GameplayRules.CustomRerollCost, await db.DiamondsAsync(Player), 3);
        Assert.Equal(1, await db.LedgerCountAsync("custom_reroll"));

        // 预览已清掉，可以再摇
        Assert.True((await players.RerollPreviewAsync(Player)).Ok);
    }

    [Fact]
    public async Task Reroll_CancelKeepsAttributesAndRefundsNothing()
    {
        using RerollDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await db.CreatePlayerAsync(Player, "取消者");
        await db.SetDiamondsAsync(Player, 5);

        (double hp, double atk, double str) = await db.InitialsAsync(Player);
        Assert.True((await players.RerollPreviewAsync(Player)).Ok);
        ActionResult cancel = await players.RerollCancelAsync(Player);

        Assert.True(cancel.Ok, cancel.Title);
        Assert.Equal((hp, atk, str), await db.InitialsAsync(Player));
        Assert.Equal(5 - GameplayRules.CustomRerollCost, await db.DiamondsAsync(Player), 3);  // 不退
        Assert.Contains("没有待确认的重随", (await players.RerollCancelAsync(Player)).Title);
    }

    [Fact]
    public async Task Reroll_RejectsContentPackCharacterAndEmptyWallet()
    {
        using RerollDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await db.CreatePlayerAsync(Player, "穷人");

        // 0 钻石：先被余额拦住
        ActionResult poor = await players.RerollPreviewAsync(Player);
        Assert.False(poor.Ok);
        Assert.Contains("钻石不足", poor.Title);

        // 换成内容包角色：属性由模板决定，不能重随
        await db.SetDiamondsAsync(Player, 5);
        await db.SwitchMainToContentPackAsync(Player, players);
        ActionResult notCustom = await players.RerollPreviewAsync(Player);
        Assert.False(notCustom.Ok);
        Assert.Contains("只有自建角色", notCustom.Title);
        Assert.Equal(5, await db.DiamondsAsync(Player), 3);
    }

    private sealed class RerollDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public RerollDatabase()
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
            Provider = services.BuildServiceProvider();
            using IServiceScope scope = Provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.EnsureCreated();
        }

        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task CreatePlayerAsync(long userId, string nickname)
        {
            using (IServiceScope scope = Provider.CreateScope())
            {
                BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
                context.Users.Add(new OshimaBot.Data.Entities.User { Id = userId, Handle = $"h{userId}", Nickname = nickname });
                await context.SaveChangesAsync();
            }
            await Get<IPlayerService>().GetSnapshotAsync(userId);
        }

        public async Task SetDiamondsAsync(long userId, double diamonds)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == userId);
            wallet.Materials = diamonds;
            await context.SaveChangesAsync();
        }

        public async Task<double> DiamondsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return (await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId)).Materials;
        }

        public async Task<int> LedgerCountAsync(string reason)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            return await context.LedgerEntries.CountAsync(x => x.Reason == reason);
        }

        /// <summary>主战角色的三项初始属性，用来验证「取消重随不改属性」。</summary>
        public async Task<(double HP, double ATK, double STR)> InitialsAsync(long userId)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == userId);
            PlayerCharacter main = (await CharacterStore.MainAsync(context, userId, wallet.MainCharacterGuid, default))!;
            return (main.Core.InitialHP, main.Core.InitialATK, main.Core.InitialSTR);
        }

        /// <summary>把主战换成内容包里的第一个角色（模拟抽卡所得）。</summary>
        public async Task SwitchMainToContentPackAsync(long userId, IPlayerService players)
        {
            using IServiceScope scope = Provider.CreateScope();
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == userId);
            FunGame.Core.Entity.Character template = FunGameService.Characters[0];
            PlayerCharacter created = CharacterStore.Create(
                context, userId, template, template.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            wallet.MainCharacterGuid = created.Guid;
            await context.SaveChangesAsync();
            _ = players;
        }

        public void Dispose()
        {
            Provider.Dispose();
            Connection.Dispose();
        }
    }
}
