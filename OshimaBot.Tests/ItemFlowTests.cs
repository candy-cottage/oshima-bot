using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using OshimaBot.Content.Items;
using OshimaBot.Game.Services;
using OshimaBot.Gameplay;
using OshimaBot.Models;
using OshimaBot.Security;
using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using Xunit;

namespace OshimaBot.Tests;

/// <summary>
/// 物品相关的旧版补齐项：库存搜索 / 兑换金币 / 魔法卡使用与合成 / 品质分解。
/// 全部走真实 SQLite 内存库与真实 <see cref="PlayerService"/>。
/// </summary>
public sealed class ItemFlowTests
{
    private const long UserId = 42;

    [Fact]
    public void ExchangePlan_MatchesLegacyRounding()
    {
        Assert.Equal((10, 2000L), GameplayRules.ExchangePlan(0));
        Assert.Equal((30, 6000L), GameplayRules.ExchangePlan(35));   // 35 → 30
        Assert.Equal((100, 20000L), GameplayRules.ExchangePlan(100));
    }

    [Fact]
    public async Task Exchange_FloorToTenUnitsAndKeepRemainder()
    {
        using ItemFlowDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SetWalletAsync(db, credits: 0, materials: 25);

        // 请求 25 → 向下取整到 20；20 / 10 * 2000 = 4000 金币
        ActionResult result = await players.ExchangeCreditsAsync(UserId, 25);

        Assert.True(result.Ok, result.Title);
        CorePlayerRecord wallet = await WalletAsync(db);
        Assert.Equal(4000, wallet.Credits, 3);
        Assert.Equal(5, wallet.Materials, 3);      // 零头保留

        // 不足一档按 10 钻算，钱不够就整笔回滚
        Assert.False((await players.ExchangeCreditsAsync(UserId, 100)).Ok);
        Assert.Equal(5, (await WalletAsync(db)).Materials, 3);
    }

    [Fact]
    public async Task Search_MatchesNameAndDescription()
    {
        using ItemFlowDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SeedCountedAsync(db,
            (Named("龙鳞碎片", "来自北境的碎片，摸起来还是温的", QualityType.Green), 1),
            (Named("回复药", "喝下去能立刻止血", QualityType.White), 2));

        IReadOnlyList<BagSearchEntry> byName = await players.SearchBagAsync(UserId, "龙鳞");
        BagSearchEntry hit = Assert.Single(byName);
        Assert.Equal(1, hit.Index);                 // @编号 与 /背包 一致
        Assert.Equal("龙鳞碎片", hit.Name);

        IReadOnlyList<BagSearchEntry> byDescription = await players.SearchBagAsync(UserId, "北境");
        Assert.Equal("龙鳞碎片", Assert.Single(byDescription).Name);

        Assert.Empty(await players.SearchBagAsync(UserId, "不存在的关键词"));

        // 旧版用区分大小写的 Contains；新版统一不区分大小写（见 DIV-013），英文名搜小写也能命中
        await SeedCountedAsync(db, (Named("Ash Blade", "", QualityType.Blue), 1));
        Assert.Equal("Ash Blade", Assert.Single(await players.SearchBagAsync(UserId, "ash")).Name);
    }

    [Fact]
    public async Task SalvageByQuality_DecomposesUnlockedItemsOfThatTier()
    {
        using ItemFlowDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SetWalletAsync(db, credits: 0, materials: 0);
        Item locked = Named("被锁住的绿装", "", QualityType.Green);
        locked.IsLock = true;
        await SeedCountedAsync(db,
            (Named("普通甲", "", QualityType.White), 3),
            (Named("普通乙", "", QualityType.White), 5),
            (Named("稀有护符", "", QualityType.Blue), 1),
            (locked, 1));
        ActionResult result = await players.SalvageByQualityAsync(UserId, (int)QualityType.White);

        Assert.True(result.Ok, result.Title);
        // 白装每件 1 钻，3 + 5 = 8 件
        Assert.Equal(8, (await WalletAsync(db)).Materials, 3);
        IReadOnlyList<BagEntry> left = await players.GetBagAsync(UserId);
        Assert.Equal(2, left.Count);                     // 蓝装 + 被锁住的绿装都还在
        Assert.DoesNotContain(left, x => x.Name.StartsWith("普通", StringComparison.Ordinal));

        // 品质序号越界要给出明确失败，而不是静默什么都不做
        Assert.False((await players.SalvageByQualityAsync(UserId, 9)).Ok);
    }

    [Fact]
    public async Task CraftMagicCardPack_ConsumesThreeCards()
    {
        using ItemFlowDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SeedAsync(db,
            Card("火之矢卡", MagicSkill(0)), Card("冰霜卡", MagicSkill(1)), Card("风刃卡", MagicSkill(2)));

        ActionResult crafted = await players.CraftMagicCardPackAsync(UserId, 1, 2, 3);

        Assert.True(crafted.Ok, crafted.Title);
        PlayerItem pack = Assert.Single(await ItemsAsync(db));
        Assert.Equal(ItemType.MagicCardPack, pack.Item.ItemType);
        Assert.Equal(GameplayRules.MagicCardPackMaxMagics, pack.Item.Skills.Magics.Count);
        Assert.StartsWith("包含魔法：", pack.Item.Description, StringComparison.Ordinal);

        // 材料已经消耗，再合一次必然失败
        Assert.False((await players.CraftMagicCardPackAsync(UserId, 1, 2, 3)).Ok);
    }

    [Fact]
    public async Task CraftMagicCardPack_RejectsDuplicateIndices()
    {
        using ItemFlowDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SeedAsync(db, Card("火之矢卡"), Card("冰霜卡"));

        ActionResult result = await players.CraftMagicCardPackAsync(UserId, 1, 1, 2);

        Assert.False(result.Ok);
        Assert.Contains("重复", result.Title);
        Assert.Equal(2, (await ItemsAsync(db)).Count);
    }

    /// <summary>
    /// 任一张是锁定的就整体拒绝（旧版 <c>ConflateMagicCardPack</c> 的前置检查）。
    /// 夹具 FIX-ITM-MAGICCARD 声明了 rejectsLocked，但之前只有「重复编号」和「用完」两个用例。
    /// </summary>
    [Fact]
    public async Task CraftMagicCardPack_RejectsLockedMaterial()
    {
        using ItemFlowDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        Item locked = Card("火之矢卡", MagicSkill(0));
        locked.IsLock = true;
        await SeedAsync(db, locked, Card("冰霜卡", MagicSkill(1)), Card("风刃卡", MagicSkill(2)));

        ActionResult result = await players.CraftMagicCardPackAsync(UserId, 1, 2, 3);

        Assert.False(result.Ok);
        Assert.Contains("锁定", result.Title);
        Assert.Equal(3, (await ItemsAsync(db)).Count);      // 一张都没被消耗
    }

    [Fact]
    public async Task UseMagicCard_LevelsUpMagicAlreadyInsidePack()
    {
        using ItemFlowDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        Skill fire = MagicSkill(0);
        await SeedAsync(db,
            Card("火之矢卡·甲", fire), Card("冰霜卡", MagicSkill(1)), Card("风刃卡", MagicSkill(2)));
        Assert.True((await players.CraftMagicCardPackAsync(UserId, 1, 2, 3)).Ok);

        // 卡包（三围全 0 → 普通）在前，新的 1 级火之矢卡在后
        await SeedAsync(db, Card("火之矢卡·乙", fire));

        List<PlayerItem> bag = await ItemsAsync(db);
        int packIndex = bag.FindIndex(x => x.Item.ItemType == ItemType.MagicCardPack) + 1;
        int cardIndex = bag.FindIndex(x => x.Item.ItemType == ItemType.MagicCard) + 1;

        ActionResult used = await players.UseMagicCardAsync(UserId, cardIndex, packIndex);

        Assert.True(used.Ok, used.Title);
        PlayerItem pack = Assert.Single(await ItemsAsync(db));
        Skill inPack = Assert.Single(pack.Item.Skills.Magics, m => m.Id == fire.Id);
        Assert.Equal(2, inPack.Level);                 // 1 级魔法 + 1 级卡 → 2 级
        // 存档翻译把技能经工厂重建为具体类（否则特效/描述全空），具体类名是常量，
        // 合成时拼进名字的「+1」后缀会回到模板名 —— 取「技能能生效」舍「名字后缀」。
        Assert.StartsWith("包含魔法：", pack.Item.Description, StringComparison.Ordinal);
        Assert.True(inPack.Description.Length > 0, "还原后的具体技能应有描述（截图里技能描述为空）");
    }

    [Fact]
    public async Task UseMagicCard_RejectsQualityMismatch()
    {
        using ItemFlowDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SeedAsync(db,
            Card("白卡·甲", MagicSkill(0)), Card("白卡·乙", MagicSkill(1)), Card("白卡·丙", MagicSkill(2)));
        Assert.True((await players.CraftMagicCardPackAsync(UserId, 1, 2, 3)).Ok);

        await SeedAsync(db, Card("蓝卡", quality: QualityType.Blue));
        List<PlayerItem> bag = await ItemsAsync(db);
        int packIndex = bag.FindIndex(x => x.Item.ItemType == ItemType.MagicCardPack) + 1;
        int cardIndex = bag.FindIndex(x => x.Item.ItemType == ItemType.MagicCard) + 1;

        ActionResult result = await players.UseMagicCardAsync(UserId, cardIndex, packIndex);

        Assert.False(result.Ok);
        Assert.Contains("品质", result.Title);
        Assert.Equal(2, (await ItemsAsync(db)).Count);   // 卡包与蓝卡都没动
    }

    [Theory]
    [InlineData(6, ItemType.MagicCard)]
    [InlineData(0, ItemType.MagicCardPack)]
    public async Task Gacha_GeneratesUsableMagicCards(int kind, ItemType expected)
    {
        // 旧版这两类产出在内容包里没有模板，靠 GenerateMagicCard(品质) 当场生成；
        // 这里用固定随机源把「必中 + 指定产出类型」钉死。
        using ItemFlowDatabase db = new(random: new ScriptedRandomSource(kind));
        IPlayerService players = db.Get<IPlayerService>();
        await players.GetSnapshotAsync(UserId);
        await SetWalletAsync(db, credits: 0, materials: 100);

        Assert.True((await players.GachaAsync(UserId, 1, useMaterial: true)).Ok);

        PlayerItem item = Assert.Single(await ItemsAsync(db));
        Assert.Equal(expected, item.Item.ItemType);
        if (expected == ItemType.MagicCard)
        {
            Assert.NotNull(item.Item.Skills.Active);
            Assert.Equal(1, item.Item.RemainUseTimes);
        }
        else
        {
            Assert.Equal(GameplayRules.MagicCardPackMaxMagics, item.Item.Skills.Magics.Count);
        }
    }

    /// <summary>
    /// 存档翻译：礼包经数据库往返后必须是具体 GiftBox 类型（带 Gifts），
    /// 而不是 ItemConverter.NewInstance() 的基类 Item —— 这是礼包 OnItemUsed 能生效的前提。
    /// 同时验证魔法卡不被 Copy 重建、技能等级保持。
    /// </summary>
    [Fact]
    public async Task Restore_GiftBoxKeepsConcreteType()
    {
        using ItemFlowDatabase db = new();
        await SeedAsync(db, new 圣诞礼包(), new 大经验书());

        List<PlayerItem> bag = await ItemsAsync(db);
        PlayerItem box = Assert.Single(bag, x => x.Item.ItemType == ItemType.GiftBox);
        Assert.IsAssignableFrom<礼包.GiftBox>(box.Item);
        Assert.NotEmpty(((礼包.GiftBox)box.Item).Gifts);
        Assert.Contains(((礼包.GiftBox)box.Item).Gifts.Keys, k => k.Contains("金币"));
    }

    /// <summary>
    /// 诊断：魔法卡包经数据库往返后，Magics/Passives 与描述是否保留。
    /// </summary>
    [Fact]
    public async Task Diagnostic_MagicPackRoundTripKeepsSkillsAndDescription()
    {
        using ItemFlowDatabase db = new();
        Item pack = FunGameService.GenerateMagicCardPack(new Random(42), 3, QualityType.Blue)
            ?? throw new InvalidOperationException("测试随机源应能合成卡包");

        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            ItemStore.AddSnapshot(context, UserId, pack, "magic-card-pack");
            await context.SaveChangesAsync();
        }

        PlayerItem restored = Assert.Single(await ItemsAsync(db));
        Assert.Equal(ItemType.MagicCardPack, restored.Item.ItemType);
        Assert.True(restored.Item.Skills.Magics.Count > 0,
            $"读回后 Magics 应保留，实际 {restored.Item.Skills.Magics.Count}");
        Assert.True(restored.Item.Description.Contains("包含魔法", StringComparison.Ordinal),
            $"读回后描述应保留，实际「{restored.Item.Description}」");
        Assert.True(restored.Item.Skills.Magics.All(m => m.Description.Length > 0),
            $"读回后每个魔法技能都应有描述；实际 Effects=" +
            string.Join("|", restored.Item.Skills.Magics.Select(m =>
                $"{m.Name}:eff{m.Effects.Count}[" +
                string.Join(",", m.Effects.Select(e => $"{e.GetType().Name}#{e.Id}:desc[{e.Description}]")) +
                $"]:skilldesc[{m.Description}]")));
    }

    /// <summary>
    /// 卸下魔法卡包后必须原样回背包：仍是 MagicCardPack、技能与描述都在。
    /// 旧代码用 AddAsync 按 CatalogId 重建，运行时生成的卡包找不到模板 → 变成普通物品。
    /// </summary>
    [Fact]
    public async Task Unequip_MagicPackReturnsAsMagicPack()
    {
        using ItemFlowDatabase db = new();
        IPlayerService players = db.Get<IPlayerService>();
        await players.RegisterAsync(UserId);

        Item pack = FunGameService.GenerateMagicCardPack(new Random(7), 3, QualityType.Blue)
            ?? throw new InvalidOperationException("测试随机源应能合成卡包");
        using (IServiceScope scope = db.Provider.CreateScope())
        {
            BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            ItemStore.AddSnapshot(context, UserId, pack, "magic-card-pack");
            await context.SaveChangesAsync();
        }

        Assert.Single(await ItemsAsync(db));   // 背包只有这一件，@1 即它
        CharacterDetail beforeEquip = (await players.GetCharacterDetailAsync(UserId, 1))!;
        ActionResult equipped = await players.EquipAsync(UserId, 1);
        Assert.True(equipped.Ok, equipped.Title);
        CharacterDetail afterEquip = (await players.GetCharacterDetailAsync(UserId, 1))!;
        Assert.False(string.Join("|", beforeEquip.Stats) == string.Join("|", afterEquip.Stats),
            "装备卡包后角色属性应变化（被动 ExSTR/ExAGI/ExINT 生效）");
        ActionResult unequipped = await players.UnequipAsync(UserId, "magiccardpack");
        Assert.True(unequipped.Ok, unequipped.Title);

        PlayerItem back = Assert.Single(await ItemsAsync(db));
        Assert.Equal(ItemType.MagicCardPack, back.Item.ItemType);
        Assert.True(back.Item.Skills.Magics.Count > 0);
        Assert.True(back.Item.Skills.Magics.All(m => m.Description.Length > 0),
            "卸下后魔法卡包的技能描述应保留");
    }

    // ---------------- 夹具 ----------------

    private static Item Named(string name, string description, QualityType quality)
        => new() { Name = name, Description = description, QualityType = quality, ItemType = ItemType.SpecialItem };

    /// <summary>取内容包里的第 <paramref name="skip"/> 个魔法技能；不同卡要取不同的，否则会被合成合并成同一张。</summary>
    private static Skill MagicSkill(int skip = 0)
        => FunGameService.AllSkills.Where(skill => skill.IsMagic).Skip(skip).First();

    private static Item Card(string name, Skill? magic = null, QualityType quality = QualityType.White)
    {
        Item card = new()
        {
            Name = name, ItemType = ItemType.MagicCard, QualityType = quality, RemainUseTimes = 1
        };
        Skill skill = (magic ?? MagicSkill()).Copy();
        skill.Level = 1;
        skill.Item = card;
        card.Skills.Active = skill;
        return card;
    }

    private static Task SeedAsync(ItemFlowDatabase db, params Item[] items)
        => SeedCountedAsync(db, [.. items.Select(item => (item, 1))]);

    private static async Task SeedCountedAsync(ItemFlowDatabase db, params (Item Item, int Quantity)[] items)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        foreach ((Item item, int quantity) in items)
        {
            ItemStore.AddSnapshot(context, UserId, item, "test", quantity);
        }
        await context.SaveChangesAsync();
    }

    private static async Task<List<PlayerItem>> ItemsAsync(ItemFlowDatabase db)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return await ItemStore.ListAsync(context, UserId, CancellationToken.None);
    }

    private static async Task<CorePlayerRecord> WalletAsync(ItemFlowDatabase db)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return await context.CorePlayers.AsNoTracking().FirstAsync(x => x.UserId == UserId);
    }

    private static async Task SetWalletAsync(ItemFlowDatabase db, double credits, double materials)
    {
        using IServiceScope scope = db.Provider.CreateScope();
        BotDbContext context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        CorePlayerRecord wallet = await context.CorePlayers.FirstAsync(x => x.UserId == UserId);
        wallet.Credits = credits;
        wallet.Materials = materials;
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// 固定随机源：必定命中抽卡（<c>NextDouble() = 0.9</c>）且产出类型恒为指定值，
    /// 用于把「魔法卡 / 魔法卡包」这条分支钉死。
    /// </summary>
    private sealed class ScriptedRandomSource(int kind) : IRandomSource
    {
        public int Next(int maxExclusive) => Math.Min(kind, maxExclusive - 1);

        public int Next(int minInclusive, int maxExclusive) => minInclusive;

        public double NextDouble() => 0.9;
    }

    private sealed class ItemFlowDatabase : IDisposable
    {
        public SqliteConnection Connection { get; }
        public ServiceProvider Provider { get; }

        public ItemFlowDatabase(IRandomSource? random = null)
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
            services.AddGameplayRuntime(random: random);
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
