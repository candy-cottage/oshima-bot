using System.Text.Json;
using FunGame.Core.Api;
using FunGame.Core.Library.Constant;
using Microsoft.EntityFrameworkCore;
using OshimaBot.Data;
using OshimaBot.Data.Core;
using CoreCharacter = FunGame.Core.Entity.Character;

namespace OshimaBot.Gameplay;

/// <summary>
/// 玩法层角色：Core 实体快照 + 持久化记录。
/// <para>对外保留整型 <see cref="Id"/>（= <see cref="CoreCharacterRecord.LocalId"/>），
/// 让小队 / 训练 / 装备这些关联表继续用整型外键；业务身份仍是 <see cref="Guid"/>。</para>
/// </summary>
public sealed class PlayerCharacter(CoreCharacterRecord record, CoreCharacter character)
{
    public CoreCharacterRecord Record { get; } = record;

    public CoreCharacter Core { get; } = character;

    public long Id => Record.LocalId;

    public string Guid => Record.Guid;

    public string CatalogId => Record.CatalogId;

    public string Name
    {
        get => Core.Name;
        set => Core.Name = value;
    }

    public int Level
    {
        get => Core.Level;
        set => Core.Level = value;
    }

    public long Exp
    {
        get => (long)Core.EXP;
        set => Core.EXP = value;
    }

    public int BreakLevel
    {
        get => Core.LevelBreak;
        set => Core.LevelBreak = value;
    }

    /// <summary>是否主战：由 <c>CorePlayers.MainCharacterGuid</c> 决定，不再单独存布尔列。</summary>
    public bool IsMain { get; set; }
}

/// <summary>
/// 角色的 Core 快照读写。全部作用于调用方传入的 <see cref="BotDbContext"/>，
/// 因此可以和玩法事务共用同一个上下文（<see cref="CoreEntityStore"/> 自带 scope，不适合事务内使用）。
/// </summary>
public static class CharacterStore
{
    private static JsonSerializerOptions Options => JsonTool.JsonSerializerOptions;

    public static async Task<List<PlayerCharacter>> ListAsync(
        BotDbContext db, long userId, string? mainGuid, CancellationToken ct)
    {
        List<CoreCharacterRecord> rows = await db.CoreCharacters
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.LocalId)
            .ToListAsync(ct);

        List<PlayerCharacter> result = [];
        foreach (CoreCharacterRecord row in rows)
        {
            CoreCharacter? character = Restore(row);
            if (character is null) continue;
            result.Add(new PlayerCharacter(row, character) { IsMain = row.Guid == mainGuid });
        }
        return result;
    }

    /// <summary>按列表序号取角色（1 基，顺序与 <c>/我的角色</c> 一致）。</summary>
    public static async Task<PlayerCharacter?> ByIndexAsync(
        BotDbContext db, long userId, int index, string? mainGuid, CancellationToken ct)
    {
        if (index <= 0) return null;
        List<PlayerCharacter> list = await ListAsync(db, userId, mainGuid, ct);
        return index > list.Count ? null : list[index - 1];
    }

    /// <summary>取主战角色；没有设定时退回第一个。</summary>
    public static async Task<PlayerCharacter?> MainAsync(
        BotDbContext db, long userId, string? mainGuid, CancellationToken ct)
    {
        List<PlayerCharacter> list = await ListAsync(db, userId, mainGuid, ct);
        return list.FirstOrDefault(x => x.IsMain) ?? list.FirstOrDefault();
    }

    /// <summary>用内容包模板造一个新角色（回满血、1 级、0 突破），并加入待保存集合。</summary>
    public static PlayerCharacter Create(BotDbContext db, long userId, CoreCharacter template, string catalogId)
    {
        CoreCharacter character = template.Copy();
        character.Guid = Guid.NewGuid();     // Copy 会复制 Guid
        character.Level = 1;
        character.EXP = 0;
        character.LevelBreak = -1;           // Core 语义：-1 表示还没突破过，第一次突破是 -1 → 0
        character.HP = character.MaxHP;      // 旧版抽到角色时也是 AddCharacterSkills(character, 1, 0, 0)：
        AttachSignatureSkills(character);    // 被动 1 级、绝招 0 级（未学），靠 /技能升级 往上点

        CoreCharacterRecord record = new()
        {
            Guid = character.Guid.ToString(),
            UserId = userId,
            CatalogId = catalogId,
            Level = character.Level,
            Exp = character.EXP,
            ExLevel = character.ExLevel,
            MaxLevel = character.MaxLevel,
            LevelBreak = character.LevelBreak,
            Promotion = character.Promotion,
            UpdatedAtUtc = DateTime.UtcNow
        };
        record.SnapshotJson = JsonSerializer.Serialize(character, Options);
        db.CoreCharacters.Add(record);
        return new PlayerCharacter(record, character);
    }

    /// <summary>把内存中的角色写回记录（索引列 + 快照）。不调用 SaveChanges，由外层事务统一提交。</summary>
    public static void Sync(PlayerCharacter character)
    {
        CoreCharacterRecord record = character.Record;
        record.Level = character.Core.Level;
        record.Exp = character.Core.EXP;
        record.ExLevel = character.Core.ExLevel;
        record.MaxLevel = character.Core.MaxLevel;
        record.LevelBreak = character.Core.LevelBreak;
        record.Promotion = character.Core.Promotion;
        record.SnapshotJson = JsonSerializer.Serialize(character.Core, Options);
        record.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static CoreCharacter? Restore(CoreCharacterRecord row)
    {
        try
        {
            CoreCharacter? character = JsonSerializer.Deserialize<CoreCharacter>(row.SnapshotJson, Options);
            if (character is null) return null;
            character.User = new FunGame.Core.Entity.User(row.UserId);
            // 懒补签：老存档（本切片之前建的）没挂签名技能，读出来时补上，下一次 Sync 就会落库。
            AttachSignatureSkills(character);
            return character;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 挂上角色的签名技能（内容包 <c>AddCharacterSkills</c>：1 个被动 + 1 个绝招），只在完全没有技能时补，
    /// 避免每次都把同一批技能重复塞进去。
    /// </summary>
    private static void AttachSignatureSkills(CoreCharacter character)
    {
        if (character.Skills.Any(skill => skill.SkillType is SkillType.Skill or SkillType.SuperSkill))
        {
            return;
        }
        OshimaBot.Game.Services.FunGameService.AddCharacterSkills(character, 1, 0, 0);
    }
}
