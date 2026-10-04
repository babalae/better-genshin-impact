using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// 记录一次 JS 执行启动的宿主操作，脚本退出前等待其 finally 清理，不依赖 JS Promise 是否还能完成。
/// </summary>
public sealed class ScriptHostOperations : IDisposable
{
    /// <summary>随宿主异步调用链流转的当前脚本作用域，不影响其他脚本或独立任务。</summary>
    private static readonly AsyncLocal<ScriptHostOperations?> Current = new();

    /// <summary>本次脚本独占的取消源，脚本提前返回时也取消尚未结束的宿主操作。</summary>
    private readonly CancellationTokenSource _cancellation;

    /// <summary>缓存令牌，使晚到的调用无需访问已经释放的取消源。</summary>
    private readonly CancellationToken _token;

    /// <summary>保护宿主操作计数与关闭状态。</summary>
    private readonly object _gate = new();

    /// <summary>当前尚未退出的宿主操作数量，包含嵌套调用。</summary>
    private int _operationCount;

    /// <summary>关闭后拒绝新增宿主操作，防止停止时脚本继续启动路线。</summary>
    private bool _stopping;

    /// <summary>关闭时等待所有宿主操作释放其登记。</summary>
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>传递给本轮脚本全部宿主能力的取消令牌。</summary>
    public CancellationToken Token => _token;

    /// <summary>为本轮脚本建立独立的宿主取消与清理边界。</summary>
    internal ScriptHostOperations(CancellationToken ct)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _token = _cancellation.Token;
    }

    /// <summary>在 V8 入口建立局部异步上下文，宿主返回后恢复原调用方上下文。</summary>
    internal IDisposable Activate()
    {
        var previous = Current.Value;
        Current.Value = this;
        return new ContextRegistration(previous);
    }

    /// <summary>
    /// 登记当前宿主方法；非 JS 调用没有作用域时不改变原行为，取消后的脚本调用则明确中断。
    /// </summary>
    public static IDisposable? Enter()
    {
        var scope = Current.Value;
        if (scope is null)
            return null;
        lock (scope._gate)
        {
            scope._token.ThrowIfCancellationRequested();
            if (scope._stopping)
                throw new OperationCanceledException("JS 脚本已经停止。", scope._token);
            scope._operationCount++;
        }
        return new OperationRegistration(scope);
    }

    /// <summary>停止接收新调用，取消已有操作，并等到它们真正退出后才允许释放游戏所有权。</summary>
    internal async Task StopAndDrainAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            if (_operationCount == 0)
                _drained.TrySetResult();
        }
        try
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            // 清理不能复用已取消的业务令牌，否则会在路线 finally 尚未结束时提前放行。
            await _drained.Task.ConfigureAwait(false);
        }
    }

    /// <summary>归还一次宿主登记，并在最后一个操作退出后完成关闭等待。</summary>
    private void Leave()
    {
        lock (_gate)
        {
            _operationCount--;
            if (_stopping && _operationCount == 0)
                _drained.TrySetResult();
        }
    }

    /// <summary>宿主操作全部清理后释放本轮取消源。</summary>
    public void Dispose() => _cancellation.Dispose();

    /// <summary>单个宿主操作的登记，重复释放不会破坏计数。</summary>
    private sealed class OperationRegistration(ScriptHostOperations owner) : IDisposable
    {
        /// <summary>仍需归还的宿主操作作用域，释放时原子置空。</summary>
        private ScriptHostOperations? _owner = owner;

        /// <summary>宿主方法退出时归还登记。</summary>
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Leave();
    }

    /// <summary>仅恢复当前入口的异步上下文，不改变已经流入宿主操作的上下文。</summary>
    private sealed class ContextRegistration(ScriptHostOperations? previous) : IDisposable
    {
        /// <summary>进入脚本前的外层上下文。</summary>
        private readonly ScriptHostOperations? _previous = previous;

        /// <summary>恢复调用方的脚本上下文。</summary>
        public void Dispose() => Current.Value = _previous;
    }
}
