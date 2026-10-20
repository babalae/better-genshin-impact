using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.SoloTask;

/// <summary>
/// 管理当前 BetterGI 进程中唯一允许存在的独立任务运行。
/// </summary>
public sealed class SoloTaskExecutionService
{
    /// <summary>
    /// 实际运行一个任务请求的入口，测试可替换为可控执行器。
    /// </summary>
    private readonly Func<SoloTaskStartRequest, CancellationToken, Action, Task<SoloTaskRunResult>> _taskExecutor;

    /// <summary>
    /// 运行协调器日志。
    /// </summary>
    private readonly ILogger<SoloTaskExecutionService> _logger;

    /// <summary>
    /// 保护活动运行与最近运行标识。
    /// </summary>
    private readonly object _syncRoot = new();

    /// <summary>
    /// 当前实例占用的独立任务运行。
    /// </summary>
    private ActiveRun? _activeRun;

    /// <summary>
    /// 最近完成的运行标识，用于对迟到的重复停止请求保持幂等。
    /// </summary>
    private Guid? _lastCompletedRunId;

    /// <summary>
    /// 空闲时也保留最近状态版本和可选错误，确保查询结果不会倒退。
    /// </summary>
    private SoloTaskStatusSnapshot _idleStatus = SoloTaskStatusSnapshot.Idle();

    /// <summary>
    /// 本进程独立任务状态的单调版本号。
    /// </summary>
    private long _statusVersion;

    /// <summary>
    /// 当前实例独立任务状态发生变化时触发。
    /// </summary>
    public event EventHandler<SoloTaskStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// 一个运行在执行层完成清理后触发，并区分正常完成、取消、失败和拒绝。
    /// </summary>
    public event EventHandler<SoloTaskRunCompletedEventArgs>? RunCompleted;

    /// <summary>
    /// 使用真实任务注册表创建进程内独立任务协调器。
    /// </summary>
    public SoloTaskExecutionService(
        SoloTaskRegistry registry,
        ILogger<SoloTaskExecutionService> logger)
    {
        _logger = logger;
        _taskExecutor = (request, cancellationToken, markRunning) =>
            new TaskRunner().RunSoloTaskWithResultAsync(
                runtime =>
                {
                    markRunning();
                    return registry.CreateTask(request, runtime);
                },
                SoloTaskRegistry.ShouldWaitForMainUi(request.TaskType),
                cancellationToken);
    }

    /// <summary>
    /// 使用可控任务执行器创建协调器，供状态机单元测试使用。
    /// </summary>
    internal SoloTaskExecutionService(
        Func<SoloTaskStartRequest, CancellationToken, Action, Task<SoloTaskRunResult>> taskExecutor,
        ILogger<SoloTaskExecutionService> logger)
    {
        _taskExecutor = taskExecutor;
        _logger = logger;
    }

    /// <summary>
    /// 返回当前实例一致的独立任务状态快照。
    /// </summary>
    public SoloTaskStatusSnapshot GetStatus()
    {
        lock (_syncRoot)
        {
            return _activeRun?.Status ?? _idleStatus;
        }
    }

    /// <summary>
    /// 原子占用当前实例并在后台启动独立任务。
    /// </summary>
    public Task<SoloTaskOperationResponse> StartAsync(SoloTaskStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveRun run;
        SoloTaskStatusSnapshot status;
        lock (_syncRoot)
        {
            if (_activeRun is { } active)
            {
                if (active.Request.RunId == request.RunId)
                {
                    return Task.FromResult(Accept(active.Status));
                }

                return Task.FromResult(Reject(
                    active.Status,
                    $"当前实例正在运行“{SoloTaskRegistry.GetDisplayName(active.Request.TaskType)}”"));
            }

            if (request.RunId == Guid.Empty)
            {
                return Task.FromResult(Reject(_idleStatus, "独立任务运行标识不能为空"));
            }

            status = CreateStatus(request, SoloTaskRunState.Starting);
            run = new ActiveRun(request, status);
            _activeRun = run;
        }

        RaiseStatusChanged(status);
        _ = ExecuteAsync(run);
        return Task.FromResult(Accept(status));
    }

    /// <summary>
    /// 只停止与运行标识匹配的当前独立任务。
    /// </summary>
    public Task<SoloTaskOperationResponse> StopAsync(SoloTaskStopRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CancellationTokenSource? cancellation = null;
        SoloTaskStatusSnapshot status;
        lock (_syncRoot)
        {
            if (_activeRun is null)
            {
                return Task.FromResult(request.RunId == _lastCompletedRunId
                    ? Accept(_idleStatus)
                    : Reject(_idleStatus, "目标运行已经结束或不存在"));
            }

            if (_activeRun.Request.RunId != request.RunId)
            {
                return Task.FromResult(Reject(_activeRun.Status, "目标运行已经发生变化，请刷新实例状态"));
            }

            if (_activeRun.Status.State == SoloTaskRunState.Stopping)
            {
                return Task.FromResult(Accept(_activeRun.Status));
            }

            status = CreateStatus(_activeRun.Request, SoloTaskRunState.Stopping);
            _activeRun.Status = status;
            cancellation = _activeRun.Cancellation;
        }

        RaiseStatusChanged(status);
        TryCancel(cancellation);
        return Task.FromResult(Accept(status));
    }

    /// <summary>
    /// 执行已经成功占用实例的任务，并在所有清理完成后释放实例。
    /// </summary>
    private async Task ExecuteAsync(ActiveRun run)
    {
        SoloTaskRunResult result;
        try
        {
            result = await _taskExecutor(
                run.Request,
                run.Cancellation.Token,
                () => MarkRunning(run));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "多实例独立任务运行协调器发生未处理异常");
            result = new SoloTaskRunResult
            {
                Outcome = SoloTaskRunOutcome.Failed,
                ErrorMessage = exception.GetBaseException().Message
            };
        }

        Complete(run, result);
    }

    /// <summary>
    /// 游戏运行环境准备完成后把启动中状态切换为运行中。
    /// </summary>
    private void MarkRunning(ActiveRun run)
    {
        SoloTaskStatusSnapshot? changedStatus = null;
        lock (_syncRoot)
        {
            if (ReferenceEquals(_activeRun, run)
                && run.Status.State == SoloTaskRunState.Starting)
            {
                changedStatus = CreateStatus(run.Request, SoloTaskRunState.Running);
                run.Status = changedStatus;
            }
        }

        if (changedStatus is not null)
        {
            RaiseStatusChanged(changedStatus);
        }
    }

    /// <summary>
    /// 发布终态并释放本轮取消令牌和实例占用。
    /// </summary>
    private void Complete(ActiveRun run, SoloTaskRunResult result)
    {
        SoloTaskStatusSnapshot? idleStatus = null;
        lock (_syncRoot)
        {
            if (ReferenceEquals(_activeRun, run))
            {
                _activeRun = null;
                _lastCompletedRunId = run.Request.RunId;
                idleStatus = SoloTaskStatusSnapshot.Idle(
                    result.Outcome is SoloTaskRunOutcome.Failed or SoloTaskRunOutcome.Rejected
                        ? result.ErrorMessage
                        : null,
                    ++_statusVersion);
                _idleStatus = idleStatus;
            }
        }

        run.Cancellation.Dispose();
        if (idleStatus is not null)
        {
            RaiseStatusChanged(idleStatus);
            RaiseRunCompleted(run.Request, result);
        }
    }

    /// <summary>
    /// 创建仍占用实例的任务状态快照。
    /// </summary>
    private SoloTaskStatusSnapshot CreateStatus(
        SoloTaskStartRequest request,
        SoloTaskRunState state)
    {
        return new SoloTaskStatusSnapshot
        {
            Version = ++_statusVersion,
            RunId = request.RunId,
            TaskType = request.TaskType,
            State = state
        };
    }

    /// <summary>
    /// 创建接受操作的统一响应。
    /// </summary>
    private static SoloTaskOperationResponse Accept(SoloTaskStatusSnapshot status)
    {
        return new SoloTaskOperationResponse { Accepted = true, Status = status };
    }

    /// <summary>
    /// 创建拒绝操作的统一响应。
    /// </summary>
    private static SoloTaskOperationResponse Reject(SoloTaskStatusSnapshot status, string message)
    {
        return new SoloTaskOperationResponse
        {
            Accepted = false,
            Status = status,
            ErrorMessage = message
        };
    }

    /// <summary>
    /// 尽力取消运行，允许任务恰好结束并释放 CTS 的并发情况。
    /// </summary>
    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 运行已经完成并释放资源，无需再次取消。
        }
    }

    /// <summary>
    /// 安全发布状态变化，避免 UI 订阅方异常破坏任务清理。
    /// </summary>
    private void RaiseStatusChanged(SoloTaskStatusSnapshot status)
    {
        try
        {
            StatusChanged?.Invoke(this, new SoloTaskStatusChangedEventArgs(status));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "发布独立任务状态变化失败");
        }
    }

    /// <summary>
    /// 安全发布运行结束事件，订阅方异常不能破坏后台执行线程。
    /// </summary>
    private void RaiseRunCompleted(SoloTaskStartRequest request, SoloTaskRunResult result)
    {
        try
        {
            RunCompleted?.Invoke(
                this,
                new SoloTaskRunCompletedEventArgs(request.RunId, request.TaskType, result));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "发布独立任务结束事件失败");
        }
    }

    /// <summary>
    /// 当前实例中一个活动独立任务的可变运行上下文。
    /// </summary>
    private sealed class ActiveRun(SoloTaskStartRequest request, SoloTaskStatusSnapshot status)
    {
        /// <summary>
        /// 本轮不可变启动请求。
        /// </summary>
        public SoloTaskStartRequest Request { get; } = request;

        /// <summary>
        /// 只属于本轮运行的取消令牌源。
        /// </summary>
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>
        /// 当前对外发布的状态快照。
        /// </summary>
        public SoloTaskStatusSnapshot Status { get; set; } = status;
    }
}
