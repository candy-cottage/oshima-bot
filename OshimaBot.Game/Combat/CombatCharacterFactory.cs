using System.Globalization;
using OshimaBot.Game.Services;
using CoreCharacter = FunGame.Core.Entity.Character;

namespace OshimaBot.Game.Combat;

/// <summary>
/// 把持久层里的角色（catalog 引用 + 等级/经验/突破）重建为 Core 实体，供战斗使用。
/// <para>角色**不是**把属性存成列，而是保存 <c>CatalogId</c>（Core 模板的 <c>Id</c>），
/// 战斗前从内容包复制模板并套用成长数据——与 Core 的 <c>Character.Copy()</c> 语义一致。</para>
/// </summary>
public static class CombatCharacterFactory
{
    /// <summary>按内容包模板 Id 重建角色；找不到模板返回 null。</summary>
    public static CoreCharacter? FromCatalog(string catalogId, int level, double exp = 0, int levelBreak = 0)
    {
        CoreCharacter? template = FindTemplate(catalogId);
        return template is null ? null : Build(template, level, exp, levelBreak);
    }

    /// <summary>从模板复制并套用成长数据。注意 <c>Copy()</c> 会复制 <c>Guid</c>，必须重赋。</summary>
    public static CoreCharacter Build(CoreCharacter template, int level, double exp = 0, int levelBreak = 0)
    {
        CoreCharacter character = template.Copy();
        character.Guid = Guid.NewGuid();
        character.Level = Math.Max(1, level);
        character.LevelBreak = levelBreak;
        character.EXP = exp;
        character.HP = character.MaxHP;
        return character;
    }

    public static CoreCharacter? FindTemplate(string catalogId)
        => FunGameService.Characters.FirstOrDefault(c =>
            c.Id.ToString(CultureInfo.InvariantCulture) == catalogId);

    /// <summary>挑一个与指定模板不同的角色作为对手（同等级由调用方决定）。</summary>
    public static CoreCharacter? PickOpponent(string excludeCatalogId)
        => FunGameService.Characters.FirstOrDefault(c =>
            c.Id.ToString(CultureInfo.InvariantCulture) != excludeCatalogId);
}
