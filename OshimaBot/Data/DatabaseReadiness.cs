namespace OshimaBot.Data;

/// <summary>
/// 「数据库已就绪（迁移跑完）」的进程内信号。
/// <para><b>为什么要有这个</b>：<see cref="DatabaseInitializer"/> 与网关客户端都是 <c>BackgroundService</c>，
/// 宿主只保证「都启动了」，<b>不保证谁先跑完</b>。首次启动时迁移要建几十张表（本地约 1–3 秒），
/// 而网关一起来就要读 <c>gateway_sessions</c> 恢复会话——表还不存在，查询直接抛
/// <c>SQLite Error 1: no such table</c>；这个异常冒到宿主后，
/// <c>BackgroundServiceExceptionBehavior=StopHost</c>（默认）会把整个进程停掉。</para>
/// <para>表现就是「第一次启动闪退，第二次就好了」——因为第二次表已经建好了。最容易被当成玄学。</para>
/// <para>用法：写库前的后台任务先 <c>await readiness.WaitAsync(ct)</c>；迁移完成后
/// <see cref="DatabaseInitializer"/> 会 <see cref="MarkReady"/>。迁移失败则 <see cref="MarkFailed"/>，
/// 等它的任务会拿到异常（数据库不可用时本来也做不了事）。</para>
/// </summary>
public sealed class DatabaseReadiness
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>迁移完成时为完成状态；迁移失败时为故障状态。</summary>
    public Task Ready => _ready.Task;

    public void MarkReady() => _ready.TrySetResult();

    public void MarkFailed(Exception exception) => _ready.TrySetException(exception);

    /// <summary>等待就绪，可被取消（宿主停机时不至于卡住）。</summary>
    public Task WaitAsync(CancellationToken cancellationToken) => _ready.Task.WaitAsync(cancellationToken);
}
