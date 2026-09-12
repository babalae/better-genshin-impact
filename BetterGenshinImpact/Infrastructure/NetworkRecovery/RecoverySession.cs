using System;
using System.Threading;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class RecoverySession : IRecoverySession
{
    private readonly object _sync = new();
    private readonly AsyncLocal<bool> _recoveryExecution = new();
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

    public bool IsCurrentRecoveryExecution => _recoveryExecution.Value;

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
            _recoveryExecution.Value = true;
            return new RecoveryLease(this, _generation);
        }
    }

    public void Clear()
    {
        _recoveryExecution.Value = false;
        lock (_sync)
        {
            _currentTask = null;
            _generation++;
            _isRecovering = false;
        }
    }

    /// <summary>只有持有当前代次的租约才能清单飞标志，否则会误清后来者的标志</summary>
    private void EndRecovery(long generation)
    {
        _recoveryExecution.Value = false;
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
