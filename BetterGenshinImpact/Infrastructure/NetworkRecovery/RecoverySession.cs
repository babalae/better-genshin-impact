using System;
using System.Threading;

namespace BetterGenshinImpact.Infrastructure.NetworkRecovery;

public sealed class RecoverySession : IRecoverySession
{
    private readonly object _sync = new();
    private readonly AsyncLocal<bool> _recoveryExecution = new();
    private TaskExecutionContext? _currentTask;
    private bool _isRecovering;

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
            _recoveryExecution.Value = true;
            return new RecoveryLease(this);
        }
    }

    public void Clear()
    {
        _recoveryExecution.Value = false;
        lock (_sync)
        {
            _currentTask = null;
            _isRecovering = false;
        }
    }

    private void EndRecovery()
    {
        _recoveryExecution.Value = false;
        lock (_sync)
        {
            _isRecovering = false;
        }
    }

    private sealed class RecoveryLease(RecoverySession owner) : IDisposable
    {
        private RecoverySession? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndRecovery();
        }
    }
}
