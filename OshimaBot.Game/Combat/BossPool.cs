using FunGame.Core.Entity;
using FunGame.Core.Library.Constant;
using FunGame.Core.Model.EffectContext;
using OshimaBot.Content.Effects.OpenEffects;
using OshimaBot.Game.Services;
using CoreCharacter = FunGame.Core.Entity.Character;

namespace OshimaBot.Game.Combat;

/// <summary>
/// 讨伐目标。<see cref="Character"/> 是池内的持久实例：全服共享，剩余生命会跨挑战累计。
/// </summary>
public sealed class BossEntry
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required CoreCharacter Character { get; init; }

    public string Display => $"{Name} Lv.{Level}";

    /// <summary>剩余生命百分比（0–1）。</summary>
    public double HPPercent => Character.MaxHP <= 0
        ? 0
        : Math.Clamp(Character.HP / Character.MaxHP, 0, 1);
}

/// <summary>
/// Boss 池：对应旧版 <c>FunGameService.GenerateBoss</c> / <c>EnhanceBoss</c>。
/// <para>旧版规则：池上限 10；等级 / 技能 / 魔法 / 普攻按 <c>MaxLevel / cutRate</c> 折算（cutRate 1/2/4）；
/// 穿 Red 品质的武器、防具、鞋子、饰品×2，外加金色魔法卡包与 2 个消耗品；
/// 再挂随机被动、随机绝招与 <c>DynamicsEffect</c> 的一整套加成。</para>
/// <para>Boss 是**全服共享的持久实体**：挑战失败只写回剩余生命，下次接着打；被击杀后从池中移除并补一只新的，
/// 与旧版 <c>FunGameService.Bosses</c> 的行为一致。池状态是进程内的（重启后重置），旧版同样是静态字典。</para>
/// </summary>
public static class BossPool
{
    public const int PoolSize = 10;

    private static readonly Lock Sync = new();
    private static readonly List<BossEntry> Entries = [];
    private static int _nextIndex = 1;

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

    /// <summary>
    /// 当前可挑战的目标：优先残血的那只（全服共享，鼓励玩家收掉同一个目标），
    /// 血量相同时按索引升序，保证结果确定。
    /// </summary>
    public static BossEntry? Current()
    {
        lock (Sync)
        {
            Ensure();
            return Entries
                .OrderBy(b => b.HPPercent)
                .ThenBy(b => b.Index)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// 取一份可战斗的副本。副本继承当前剩余生命，因此伤害会跨挑战累计。
    /// <para>用 <see cref="Create"/> 按索引确定性重建，而不是 <c>Character.Copy()</c>：
    /// Core 的 <c>Copy()</c> 默认既不复制 <c>Ex*</c> 也不复制装备，直接复制会得到一个没有强化、没穿装备的 Boss。</para>
    /// </summary>
    public static CoreCharacter CreateFighter(BossEntry entry)
    {
        lock (Sync)
        {
            CoreCharacter boss = Create(entry.Index).Character;
            boss.HP = entry.Character.HP;
            boss.MP = entry.Character.MP;
            boss.EP = entry.Character.EP;
            return boss;
        }
    }

    /// <summary>
    /// 写回战斗结果：存活则累计伤害，阵亡则移出池并补充新目标。
    /// </summary>
    /// <returns>本次是否击杀了该目标。</returns>
    public static bool ApplyResult(BossEntry entry, double hp, double mp, double ep)
    {
        lock (Sync)
        {
            if (!Entries.Contains(entry)) return false;
            if (hp <= 0)
            {
                Entries.Remove(entry);
                Ensure();
                return true;
            }

            entry.Character.HP = Math.Clamp(hp, 0, entry.Character.MaxHP);
            entry.Character.MP = Math.Clamp(mp, 0, entry.Character.MaxMP);
            entry.Character.EP = Math.Max(0, ep);
            return false;
        }
    }

    /// <summary>清空并重建（测试用）。</summary>
    public static void Reset()
    {
        lock (Sync)
        {
            Entries.Clear();
            _nextIndex = 1;
            Ensure();
        }
    }

    private static void Ensure()
    {
        while (Entries.Count < PoolSize)
        {
            Entries.Add(Create(_nextIndex++));
        }
    }

    private static BossEntry Create(int index)
    {
        // 与旧版一致：cutRate 1 / 2 / 4 → 等级 60 / 30 / 15，技能与魔法同理折算。
        // 注意括号：写成 `index % 3 switch` 会被解析为 `index % (3 switch {...})`，让 cutRate 变成 index % 4。
        int cutRate = (index % 3) switch
        {
            0 => 1,
            1 => 2,
            _ => 4
        };
        var equilibrium = General.GameplayEquilibriumConstant;
        int level = Math.Max(1, equilibrium.MaxLevel / cutRate);
        int skillLevel = Math.Max(1, equilibrium.MaxSkillLevel / cutRate);
        int magicLevel = Math.Max(1, equilibrium.MaxMagicLevel / cutRate);
        int attackLevel = Math.Max(1, equilibrium.MaxNormalAttackLevel / cutRate);

        // 固定种子：同一索引生成同一只，便于回归与对拍。
        Random random = new(unchecked(index * 7919 + 17));

        List<CoreCharacter> templates = FunGameService.Characters;
        CoreCharacter template = templates[(index - 1) % templates.Count];
        CoreCharacter boss = CombatCharacterFactory.Build(template, level);
        boss.Name = $"{template.Name}·讨伐目标";
        boss.NormalAttack.Level = attackLevel;

        BossEnhance.EquipRedGear(boss, random);
        BossEnhance.EquipMagicCardPack(boss, random, magicLevel);
        BossEnhance.AddConsumables(boss, random);
        BossEnhance.AddBossSkills(boss, random, skillLevel);
        BossEnhance.ApplyDynamics(boss, level);

        // 旧版以 Recovery() 收尾：让 MaxHP/MaxMP 按新装备与加成重算，并回满状态。
        boss.Recovery();

        return new BossEntry
        {
            Index = index,
            Name = boss.Name,
            Level = level,
            Character = boss
        };
    }
}
