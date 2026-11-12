using System.Text;
using FunGame.Core.Entity;
using FunGame.Core.Interface.Entity;
using OshimaBot.Content.Items;
using OshimaBot.Content.Units;

namespace OshimaBot.Content.Regions;

/// <summary>
/// 地区（旧版 <c>OshimaRegion</c>）：在 Core <see cref="Region"/> 之上加一层「探索剧情表」。
/// <para>旧版还在这里挂了商店读写（<c>VisitStore</c> / <c>SaveGlobalStore</c> 等），
/// v3 的商店已经独立成 <c>StoreCatalog</c> + <c>BotDbContext</c>，那部分**不再移植**——
/// 地区只负责「有什么敌人、掉什么、有什么剧情」。</para>
/// </summary>
public class OshimaRegion : Region
{
    /// <summary>连续任务（可长期重复的剧情线）。</summary>
    public Dictionary<string, QuestExploration> ContinuousQuestList { get; } = [];

    /// <summary>即时任务（一次性的紧急事件）。</summary>
    public Dictionary<string, QuestExploration> ImmediateQuestList { get; } = [];

    /// <summary>渐进任务（按收集数量推进）。</summary>
    public Dictionary<string, QuestExploration> ProgressiveQuestList { get; } = [];

    /// <summary>本地区的全部剧情，按「连续 → 即时 → 渐进」拼接。</summary>
    public IEnumerable<KeyValuePair<string, QuestExploration>> AllQuests
        => ContinuousQuestList.Concat(ImmediateQuestList).Concat(ProgressiveQuestList);

    public override bool Equals(IBaseEntity? other)
        => other is OshimaRegion && other.GetIdName() == GetIdName();

    /// <summary>地区名 + 基础信息，用于 <c>/查地区</c>；<see cref="Region.ToString"/> 已经给了敌人/掉落，这里补剧情与难度。</summary>
    public override string ToString() => ToString(showQuests: true);

    public string ToString(bool showQuests)
    {
        StringBuilder builder = new();
        builder.AppendLine(base.ToString());
        if (showQuests && AllQuests.Any())
        {
            builder.AppendLine("== 探索剧情 ==");
            builder.AppendLine(string.Join("、", AllQuests.Select(q => $"{q.Key}：{q.Value.Description}")));
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>给渲染层用的角色/单位/掉落名字快照（不暴露实体集合本身）。</summary>
    public IEnumerable<string> BossNames => Characters.Select(c => c.Name);

    public IEnumerable<string> UnitNames => Units.Select(u => u.Name);

    public IEnumerable<RegionItem> RegionCrops => Crops.OfType<RegionItem>();

    public IEnumerable<RegionUnit> RegionUnits => Units.OfType<RegionUnit>();

    public IEnumerable<RegionCharacter> RegionCharacters => Characters.OfType<RegionCharacter>();
}
