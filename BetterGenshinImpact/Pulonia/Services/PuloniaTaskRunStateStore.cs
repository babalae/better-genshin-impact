using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 持有唯一的当前运行状态（state.json）并提供唯一写入入口：克隆 → 修改 → 保存 → 提交。
/// 已提交的状态对象此后不再修改（写时复制），只读查询可以不加锁直接读取 <see cref="Current"/>；
/// 关键状态写入失败时熔断，任务服务据此停止自动运行。
/// </summary>
/// <remarks>
/// 克隆只复制各集合，元素（运行记录、账本条目、触发游标）由新旧状态共用，因此修改候选状态时只能整项增删替换，
/// 不能原地修改已有元素；服务内所有写入都遵守这一约定。
/// </remarks>
internal sealed class PuloniaTaskRunStateStore : IDisposable
{
    /// <summary>
    /// state.json 的文件读写。
    /// </summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>
    /// 串行化候选状态生成、耐久写入和内存提交。
    /// </summary>
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    /// <summary>
    /// 最近一次成功落盘的当前状态；提交后不再修改，替换引用即提交。
    /// </summary>
    private PuloniaTaskState _persistentState = new();

    /// <summary>
    /// 关键状态写入失败后阻止后续自动动作，避免使用过期账本继续执行。
    /// </summary>
    private Exception? _persistenceFailure;

    /// <summary>
    /// 建立运行状态存储；构造不读写文件。
    /// </summary>
    /// <param name="store">state.json 所在的文件存储。</param>
    public PuloniaTaskRunStateStore(PuloniaTaskStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 最近一次成功落盘的状态，只读；调用方不能修改其中任何对象。
    /// </summary>
    public PuloniaTaskState Current => Volatile.Read(ref _persistentState);

    /// <summary>
    /// 首个关键写入失败；为空表示尚未熔断。
    /// </summary>
    public Exception? Failure => Volatile.Read(ref _persistenceFailure);

    /// <summary>
    /// 从文件加载当前状态（含备份回退与旧格式升级），作为后续写入的起点。只在服务初始化时调用一次。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否因正式文件缺失或损坏而从备份恢复。</returns>
    public async Task<bool> LoadAsync(CancellationToken ct)
    {
        var loaded = await _store.LoadStateAsync(ct).ConfigureAwait(false);
        Volatile.Write(ref _persistentState, loaded.State);
        return loaded.RecoveredFromBackup;
    }

    /// <summary>
    /// 唯一写入入口：在状态门内复制当前状态、应用修改、耐久保存，成功后才提交为新的当前状态。
    /// </summary>
    /// <param name="mutate">修改候选状态；抛出时不保存，也不视为写入失败。</param>
    /// <param name="ct">等待状态门与保存使用的令牌；调用方因此令牌取消而中断不视为写入失败。</param>
    /// <param name="committed">保存成功、提交后仍在状态门内执行的同步回调，用于让内存对象与已落盘内容同时生效。</param>
    /// <exception cref="Exception">保存失败时原样抛出，并且（调用方取消除外）记录为熔断原因。</exception>
    public async Task MutateAsync(Action<PuloniaTaskState> mutate, CancellationToken ct, Action? committed = null)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 1. 克隆：只复制集合，元素与已提交状态共用
            var candidate = Clone(_persistentState);
            // 2. 修改：候选状态的集合都是新实例，可直接增删
            mutate(candidate);
            candidate.Sequence = checked(candidate.Sequence + 1);
            // 3. 保存：失败时内存状态保持上一版本
            try
            {
                await _store.SaveStateAsync(candidate, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    ReportFailure(ex);
                throw;
            }
            // 4. 提交：替换引用后，只读查询立即看到新版本
            Volatile.Write(ref _persistentState, candidate);
            committed?.Invoke();
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// 记录关键写入失败（只保留首个原因）；不在本类内完成的写入（如历史归档）失败时由调用方报告。
    /// </summary>
    /// <param name="failure">失败原因。</param>
    public void ReportFailure(Exception failure)
        => Interlocked.CompareExchange(ref _persistenceFailure, failure, null);

    /// <summary>
    /// 关键状态写入失败后拒绝新增或续跑请求，保留现场等待人工处理。
    /// </summary>
    /// <exception cref="InvalidOperationException">已经熔断。</exception>
    public void ThrowIfFailed()
    {
        if (Failure is { } failure)
            throw new InvalidOperationException("Pulonia 关键状态写入失败，已停止自动运行。", failure);
    }

    /// <summary>
    /// 释放状态门。
    /// </summary>
    public void Dispose() => _stateGate.Dispose();

    /// <summary>
    /// 复制状态的各集合，元素沿用同一实例。
    /// </summary>
    private static PuloniaTaskState Clone(PuloniaTaskState state) => new()
    {
        SchemaVersion = state.SchemaVersion,
        Sequence = state.Sequence,
        PendingRequests = [.. state.PendingRequests],
        ActiveRun = state.ActiveRun,
        PendingArchives = [.. state.PendingArchives],
        Ledger = [.. state.Ledger],
        TriggerStates = [.. state.TriggerStates]
    };
}
