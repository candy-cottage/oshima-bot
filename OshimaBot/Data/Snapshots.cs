namespace OshimaBot.Data;

/// <summary>
/// 人物存档快照。
/// <para>旧版的「还原存档」其实是个空壳：<c>FunGameController.RestoreSaved</c> 整个方法体被注释掉，
/// 只返回「此功能维护中」。新版把它做成真的：每次备份把钱包、角色、背包、小队与日常进度
/// 整体序列化进 <see cref="PayloadJson"/>，还原时整块替换回去。</para>
/// <para>刻意**不**包含：市场挂单、报价单、私信、社团、在途探索/锻造。前四者是与其他玩家共享的状态，
/// 回滚会让同一件物品同时出现在两个人手里；后两者是计时器，回滚等于把奖励再领一次。</para>
/// </summary>
public sealed class SaveSnapshot
{
    public long Id { get; set; }
    public long UserId { get; set; }

    /// <summary><c>manual</c> = 玩家主动备份；<c>pre-restore</c> = 还原前自动留底（可用来反悔）。</summary>
    public string Kind { get; set; } = "manual";

    /// <summary>快照所属游戏日（Asia/Shanghai，4:00 起算），用于「每天最多还原一次」。</summary>
    public string GameDay { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>玩家写的备注，例如「打 Boss 前」。</summary>
    public string Note { get; set; } = "";

    // 下面几个是给人看的摘要，还原时以 PayloadJson 为准。
    public double Credits { get; set; }
    public double Materials { get; set; }
    public int CharacterCount { get; set; }
    public int ItemCount { get; set; }

    public string PayloadJson { get; set; } = "";
}
