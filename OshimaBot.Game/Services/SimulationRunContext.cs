using FunGame.Core.Model.Framework;

namespace OshimaBot.Game.Services;

/// <summary>
/// 单次模拟的运行态。S8 前置改造：原先挂在 FunGameSimulation 上的进程级 static 可变字段
/// 全部搬到这里，每次运行一个实例，避免多用户并发/串局时互相污染。
/// 内容目录（FunGameService.Characters/Skills/Items）是只读模板，仍保持进程级共享。
/// </summary>
public sealed class SimulationRunContext
{
    public bool IsRunning { get; set; }
    public bool IsWeb { get; set; }
    public bool IsDebug { get; set; }
    public bool PrintOut { get; set; }
    public bool DeathMatchRoundDetail { get; set; }
    public string Msg { get; set; } = "";
    public int? SeedOverride { get; set; }
    public long FixedWeaponId { get; set; }
    public int Seed { get; set; }
    public Random Random { get; set; } = new();
    public GameMap Map { get; set; } = new FastAutoMap();

    /// <summary>S9：本局是否额外写 rounds_archive.zip（默认关闭，结果统一入库）。</summary>
    public bool ArchiveRounds { get; set; }

    /// <summary>绑定到本实例的日志回调：不依赖 AsyncLocal，跨线程回调也不会串局。</summary>
    public void WriteLine(string text)
    {
        Msg += text + "\r\n";
        if (PrintOut) Console.WriteLine(text);
    }
}
