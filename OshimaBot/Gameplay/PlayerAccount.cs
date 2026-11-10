using OshimaBot.Data.Core;
using OshimaBot.Data.Entities;

namespace OshimaBot.Gameplay;

/// <summary>
/// 玩家账户聚合：进度（<see cref="PlayerState"/>）+ 钱包（<see cref="CorePlayerRecord"/>）。
/// <para>钱包按 Core 语义用 <see cref="double"/> 存 <c>Inventory.Credits</c> / <c>Inventory.Materials</c>，
/// 不再用 <c>long</c> 截断；进度字段暂时仍留在 <c>player_states</c>，随 M1.5 后续切片继续迁移。</para>
/// <para>暴露同名转发属性，是为了让既有的 <c>(db, state, ct)</c> 业务 lambda 不需要改写——
/// 变量名叫 <c>state</c>，但类型已经是账户聚合。</para>
/// </summary>
public sealed class PlayerAccount(PlayerState state, CorePlayerRecord wallet)
{
    public PlayerState State { get; } = state;

    public CorePlayerRecord Wallet { get; } = wallet;

    public long UserId => State.UserId;

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

    public long Exp
    {
        get => State.Exp;
        set => State.Exp = value;
    }

    public int Level
    {
        get => State.Level;
        set => State.Level = value;
    }

    public int SignInStreak
    {
        get => State.SignInStreak;
        set => State.SignInStreak = value;
    }

    public string SignedOn
    {
        get => State.SignedOn;
        set => State.SignedOn = value;
    }

    /// <summary>同时刷新进度行与钱包行的更新时间。</summary>
    public DateTime UpdatedAtUtc
    {
        get => State.UpdatedAtUtc;
        set
        {
            State.UpdatedAtUtc = value;
            Wallet.UpdatedAtUtc = value;
        }
    }
}
