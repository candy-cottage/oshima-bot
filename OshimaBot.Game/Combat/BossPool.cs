using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using OshimaBot.Game.Services;
using CoreCharacter = FunGame.Core.Entity.Character;

namespace OshimaBot.Game.Combat;

/// <summary>讨伐目标。索引与等级在池内固定，当天按日期选一只。</summary>
public sealed class BossEntry
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required CoreCharacter Character { get; init; }

    public string Display => $"{Name} Lv.{Level}";
}

/// <summary>
/// Boss 池：对应旧版 <c>FunGameService.GenerateBoss</c> / <c>EnhanceBoss</c> 的精简实现。
/// <para>旧版规则：池上限 10，等级按 <c>MaxLevel / cutRate</c> 取 60 / 30 / 15（cutRate 1/2/4），
/// 装备 Red 品质的武器、防具、鞋子、饰品×2，再挂专属被动与 <c>DynamicsEffect</c>。</para>
/// <para>当前实现保留「池、等级分档、红装」；专属被动与 DynamicsEffect 待 M5 后续补齐（见计划 §8.3）。</para>
/// </summary>
public static class BossPool
{
    public const int PoolSize = 10;

    private static readonly Lock Sync = new();
    private static readonly List<BossEntry> Entries = [];

    public static IReadOnlyList<BossEntry> All
    {
        get
        {
            lock (Sync)
            {
                Ensure();
                return [.. Entries];
            }
        }
    }

    public static BossEntry? Get(int index)
    {
        lock (Sync)
        {
            Ensure();
            return Entries.FirstOrDefault(b => b.Index == index);
        }
    }

    /// <summary>当天固定一只，避免同一天刷新出不同目标。</summary>
    public static BossEntry ForDate(string date)
    {
        lock (Sync)
        {
            Ensure();
            int hash = 0;
            foreach (char c in date) hash = (hash * 31 + c) & 0x7FFFFFFF;
            return Entries[hash % Entries.Count];
        }
    }

    /// <summary>取一份可战斗的副本（池内实例不参与战斗，避免 HP 被改写）。</summary>
    public static CoreCharacter CreateFighter(BossEntry entry)
    {
        CoreCharacter boss = entry.Character.Copy();
        boss.Guid = Guid.NewGuid();   // Copy 会复制 Guid
        boss.HP = boss.MaxHP;
        return boss;
    }

    private static void Ensure()
    {
        if (Entries.Count >= PoolSize) return;
        for (int index = Entries.Count + 1; index <= PoolSize; index++)
        {
            Entries.Add(Create(index));
        }
    }

    private static BossEntry Create(int index)
    {
        // 与旧版一致：cutRate 1 / 2 / 4 → 等级 60 / 30 / 15
        // 注意括号：写成 `index % 3 switch` 会被解析为 `index % (3 switch {...})`，让 cutRate 变成 index % 4。
        int cutRate = (index % 3) switch
        {
            0 => 1,
            1 => 2,
            _ => 4
        };
        int level = Math.Max(1, General.GameplayEquilibriumConstant.MaxLevel / cutRate);

        List<CoreCharacter> templates = FunGameService.Characters;
        CoreCharacter template = templates[(index - 1) % templates.Count];
        CoreCharacter boss = CombatCharacterFactory.Build(template, level);
        boss.Name = $"{template.Name}·讨伐目标";

        EquipRedGear(boss);

        // 旧版通过 DynamicsEffect 施加；带 2 的参数（exhp2/exmp2）是**百分比加成**，
        // 对应 Core 的 ExXXPercentage，不是 ExXX2（后者是加值）。不带 2 的 exatk/exdef 才是加值。
        boss.ExATK2 = 3.4 * level;
        boss.ExDEF2 = 3.4 * level;
        boss.ExHPPercentage = 1.5;
        boss.ExMPPercentage = 0.8;
        boss.ExHR = 0.15 * level;
        boss.ExMR = 0.1 * level;
        boss.ExCDR = 0.25;
        // exCR / exCRD / exACC 在 Core 里没有同名字段（暴击/命中另有实现），待核对后再补。

        return new BossEntry
        {
            Index = index,
            Name = boss.Name,
            Level = level,
            Character = boss
        };
    }

    /// <summary>旧版给 Boss 穿 5 件 Red 品质装备（武器/防具/鞋子/饰品×2）。</summary>
    private static void EquipRedGear(CoreCharacter boss)
    {
        ItemType[] slots = [ItemType.Weapon, ItemType.Armor, ItemType.Shoes, ItemType.Accessory, ItemType.Accessory];
        foreach (ItemType type in slots)
        {
            Item? template = FunGameService.AllItems
                .FirstOrDefault(i => i.ItemType == type && i.QualityType == QualityType.Red);
            if (template is null) continue;

            Item gear = template.Copy();
            gear.Guid = Guid.NewGuid();
            gear.IsLock = true;
            boss.Equip(gear);
        }
    }
}
