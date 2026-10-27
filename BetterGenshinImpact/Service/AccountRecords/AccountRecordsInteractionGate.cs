using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Service.AccountRecords;

/// <summary>统一协调任务执行与人工写入；自动上报不经过此门禁。</summary>
public sealed class AccountRecordsInteractionGate : ObservableObject
{
    /// <summary>所有任务及账号界面共用同一人工操作开关。</summary>
    public static AccountRecordsInteractionGate Instance { get; } = new();
    /// <summary>保护任务进入和人工提交，避免检查后任务开始的竞态。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>仍在持有人工操作锁的任务数量。</summary>
    private int _taskCount;
    /// <summary>任务运行期间禁止人工更改账号数据。</summary>
    public bool IsLocked => Volatile.Read(ref _taskCount) > 0;
    /// <summary>统一向人工操作说明拒绝原因。</summary>
    public const string LockedMessage = "任务正在运行，暂不能人工修改狛荷屋数据；任务上报不受影响。";

    /// <summary>任务启动时持有，结束、取消或异常时释放；支持多个调用者重叠。</summary>
    public async Task<IDisposable> LockForTaskAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { Interlocked.Increment(ref _taskCount); }
        finally { _gate.Release(); }
        OnPropertyChanged(nameof(IsLocked));
        return new Lease(() =>
        {
            Interlocked.Decrement(ref _taskCount);
            OnPropertyChanged(nameof(IsLocked));
        });
    }

    /// <summary>人工提交前检查；持有至写入结束，任务启动会等待已开始的提交完成。</summary>
    public async Task<IDisposable> EnterManualAsync()
    {
        await _gate.WaitAsync();
        if (IsLocked)
        {
            _gate.Release();
            throw new InvalidOperationException(LockedMessage);
        }
        return new Lease(() => _gate.Release());
    }

    /// <summary>人工写入统一入口；锁只覆盖提交本身，不覆盖后续刷新、提示或弹窗。</summary>
    public async Task ExecuteManualAsync(Func<Task> action)
    {
        using var access = await EnterManualAsync();
        await action();
    }

    /// <summary>执行有返回值的人工提交，异常时同样自动释放。</summary>
    public async Task<T> ExecuteManualAsync<T>(Func<Task<T>> action)
    {
        using var access = await EnterManualAsync();
        return await action();
    }

    /// <summary>可重复释放的操作令牌。</summary>
    private sealed class Lease(Action release) : IDisposable
    {
        /// <summary>原子取走回调，避免重复解锁。</summary>
        private Action? _release = release;
        /// <summary>释放本次持有的锁。</summary>
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
