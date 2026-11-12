namespace OshimaBot.Content.Regions;

/// <summary>
/// 探索结果类型（照搬旧版 <c>Oshima.Core.Constant.ExploreResult</c>）。
/// <para>探索剧情里每条 <see cref="QuestExploration"/> 都带一个结果类型，决定探索结算时走「无事发生 / 战斗 / 拾得 / 事件」哪一支。</para>
/// </summary>
public enum ExploreResult
{
    /// <summary>普通：什么都不发生，只有剧情文本。</summary>
    General,
    /// <summary>空手而归。</summary>
    Nothing,
    /// <summary>遭遇战斗。</summary>
    Fight,
    /// <summary>获得物品。</summary>
    Earned,
    /// <summary>触发事件。</summary>
    Event
}

/// <summary>
/// 地区探索剧情（旧版 <c>QuestExploration</c>）。
/// <para>旧版把三种任务按类型分开存（Continuous 连续 / Immediate 即时 / Progressive 渐进），
/// 这里保持同样的三段划分，只把结果类型搬进来。</para>
/// </summary>
public sealed class QuestExploration(
    string description, string character = "", string item = "", ExploreResult result = ExploreResult.General)
{
    public ExploreResult ExploreResult { get; set; } = result;
    public string Description { get; set; } = description;

    /// <summary>关联的角色名（空串表示不涉及角色）。</summary>
    public string Character { get; set; } = character;

    /// <summary>关联的物品名（渐进任务用来标记要收集什么）。</summary>
    public string Item { get; set; } = item;

    public override string ToString() => Description;
}
