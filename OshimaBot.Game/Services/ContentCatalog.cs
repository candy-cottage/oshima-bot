using System.Security.Cryptography;
using System.Text;
using FunGame.Core.Entity;

namespace OshimaBot.Game.Services;

/// <summary>
/// 内容目录版本与指纹（计划 §10 第 7 条「内容目录版本可追踪」，2026-11-15 补）。
/// <para>问题：内容包（物品 / 技能 / 角色 / 地区）是编译进 <c>OshimaBot.Content</c> 的，
/// 换一版内容不会改任何配置；而玩家的背包与角色快照里存的是 <c>CatalogId</c> 与 JSON 快照，
/// **内容删改后这些引用会静默失效**——没人拦，也没有任何地方能看出「这次启动换过内容」。</para>
/// <para>做法：把当前加载到的内容折成一个指纹（Id + 名字 + 品质，排序后哈希），
/// 启动时与库里的上一份比对，变了就打警告（不是拦停：内容变更是正常发版，但必须留痕）。</para>
/// <para>指纹刻意只吃**稳定字段**（Id / 名字 / 品质），不吃描述与文案——
/// 改一段说明文字不该让存量快照「看起来过期」，但改 Id、改名、改品质必须被看见。</para>
/// </summary>
public static class ContentCatalog
{
    private static readonly Lazy<string> LazyFingerprint = new(Compute, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>内容指纹（小写十六进制 SHA-256）。首次访问时按当前内容计算并缓存。</summary>
    public static string Fingerprint => LazyFingerprint.Value;

    /// <summary>给人看的短指纹（前 12 位），日志与状态页用。</summary>
    public static string ShortFingerprint => Fingerprint[..12];

    /// <summary>内容规模，用于日志里说明「变了多少」。</summary>
    public static ContentCatalogSummary Summary => new(
        Items: FunGameService.AllItems.Count,
        Skills: FunGameService.AllSkills.Count,
        Characters: FunGameService.Characters.Count,
        Regions: FunGameService.Regions.Count);

    /// <summary>
    /// 指纹的原料：按 Id 排序后的 <c>类型:Id:名字:品质</c> 行，逐行 UTF-8 后整体 SHA-256。
    /// 排序是为了不受注册顺序影响——内容文件换个顺序不该算「换了内容」。
    /// </summary>
    private static string Compute()
    {
        // 自己保证初始化：指纹是 lazy 缓存的，谁先碰到就定终身；
        // 若因为调用方忘了初始化而得出一份「空目录」的指纹（SHA-256 空串 e3b0c442…），会一直错下去。
        // EnsureInitialized 本身幂等且很便宜，在这里兜住比要求每个调用点都记得更可靠。
        FunGameRuntime.EnsureInitialized();
        if (FunGameService.AllItems.Count == 0)
        {
            throw new InvalidOperationException(
                "内容目录还是空的：先调用 FunGameRuntime.EnsureInitialized() 再取 ContentCatalog.Fingerprint");
        }

        StringBuilder builder = new();
        foreach (FunGame.Core.Entity.Item item in FunGameService.AllItems.OrderBy(x => x.Id).ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            builder.Append("item:").Append(item.Id).Append(':').Append(item.Name).Append(':')
                .Append((int)item.QualityType).Append('\n');
        }
        foreach (Skill skill in FunGameService.AllSkills.OrderBy(x => x.Id).ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            builder.Append("skill:").Append(skill.Id).Append(':').Append(skill.Name).Append('\n');
        }
        foreach (Character character in FunGameService.Characters.OrderBy(x => x.Id).ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            builder.Append("character:").Append(character.Id).Append(':').Append(character.Name).Append('\n');
        }
        foreach (Region region in FunGameService.Regions.OrderBy(x => x.Id).ThenBy(x => x.Name, StringComparer.Ordinal))
        {
            builder.Append("region:").Append(region.Id).Append(':').Append(region.Name).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}

/// <param name="Items">物品数。</param>
/// <param name="Skills">技能数（含魔法）。</param>
/// <param name="Characters">角色数。</param>
/// <param name="Regions">地区数。</param>
public sealed record ContentCatalogSummary(int Items, int Skills, int Characters, int Regions)
{
    /// <summary><c>物品 198 / 技能 118 / 角色 18 / 地区 9</c> 这种一行摘要。</summary>
    public override string ToString()
        => $"物品 {Items} / 技能 {Skills} / 角色 {Characters} / 地区 {Regions}";

    /// <summary>两份摘要是否一致（用于判断「只是改了文案」还是「真的动了目录」）。</summary>
    public bool SameShape(ContentCatalogSummary other)
        => Items == other.Items && Skills == other.Skills && Characters == other.Characters && Regions == other.Regions;
}
