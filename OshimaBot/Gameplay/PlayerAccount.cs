using OshimaBot.Data.Core;

namespace OshimaBot.Gameplay;

/// <summary>
/// 玩家账户：一张 <c>CorePlayers</c> 行同时承载钱包与玩法进度（签到）。
/// <para>钱包按 Core 语义用 <see cref="double"/> 存 <c>Inventory.Credits</c> / <c>Inventory.Materials</c>，
/// 不再用 <c>long</c> 截断。原 <c>PlayerStates</c> 表已在 M1.5 切片③并入本表——
/// 它剩下的字段里只有签到是真在用的，其余（午餐/晚餐/探索次数/赛马/协作/锻造点数）从来没有业务代码读写。</para>
/// <para>暴露同名转发属性，是为了让既有的 <c>(db, state, ct)</c> 业务 lambda 不需要改写——
/// 变量名叫 <c>state</c>，但类型是账户聚合。</para>
/// </summary>
public sealed class PlayerAccount(CorePlayerRecord wallet)
{
    public CorePlayerRecord Wallet { get; } = wallet;

    public long UserId => Wallet.UserId;

    /// <summary>对应 <c>Inventory.Credits</c>（金币）。</summary>
    public double Credits
    {
        get => Wallet.Credits;
        set => Wallet.Credits = value;
    }

    /// <summary>对应 <c>Inventory.Materials</c>（第二资源；Oshima 内容包显示为「钻石」）。</summary>
    public double Materials
    {
        get => Wallet.Materials;
        set => Wallet.Materials = value;
    }

    public int SignInStreak
    {
        get => Wallet.SignInStreak;
        set => Wallet.SignInStreak = value;
    }

    public string SignedOn
    {
        get => Wallet.SignedOn;
        set => Wallet.SignedOn = value;
    }

    public DateTime UpdatedAtUtc
    {
        get => Wallet.UpdatedAtUtc;
        set => Wallet.UpdatedAtUtc = value;
    }
}
