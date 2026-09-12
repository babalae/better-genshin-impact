using System;
using System.Threading;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class RecoverySession : IRecoverySession
{
    private readonly object _sync = new();
    private readonly AsyncLocal<long> _recoveryExecutionGeneration = new();
    private TaskExecutionContext? _currentTask;
    private bool _isRecovering;
    private long _generation;

    public TaskExecutionContext? CurrentTask
    {
        get
        {
            lock (_sync)
            {
                return _currentTask;
            }
        }
    }

    public bool IsRecovering
    {
        get
        {
            lock (_sync)
            {
                return _isRecovering;
            }
        }
    }

    public bool IsCurrentRecoveryExecution => _recoveryExecutionGeneration.Value != 0;

    public void BeginTask(TaskExecutionContext context)
    {
        lock (_sync)
        {
            _currentTask = context;
        }
    }

    public void CompleteTask(string taskId)
    {
        lock (_sync)
        {
            if (_currentTask is { TaskId: var currentTaskId } && currentTaskId == taskId)
            {
                _currentTask = null;
            }
        }
    }

    public IDisposable? BeginRecovery()
    {
        lock (_sync)
        {
            if (_isRecovering)
            {
                return null;
            }

            _isRecovering = true;
            _generation++;
            _recoveryExecutionGeneration.Value = _generation;
            return new RecoveryLease(this, _generation);
        }
    }

    public void Clear()
    {
        _recoveryExecutionGeneration.Value = 0;
        lock (_sync)
        {
            _currentTask = null;
            _generation++;
            _isRecovering = false;
        }
    }

    /// <summary>
    /// 只有持有当前代次的租约才能清单飞标志；执行栈标记也按代次比对，
    /// 老租约收尾时不得把新一轮恢复的执行栈标记清掉。
    /// </summary>
    private void EndRecovery(long generation)
    {
        if (_recoveryExecutionGeneration.Value == generation)
        {
            _recoveryExecutionGeneration.Value = 0;
        }

        lock (_sync)
        {
            if (generation == _generation)
            {
                _isRecovering = false;
            }
        }
    }

    private sealed class RecoveryLease(RecoverySession owner, long generation) : IDisposable
    {
        private RecoverySession? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndRecovery(generation);
        }
    }
}
