using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// Pulonia 的单主协调器：提交时固定快照，随后串行执行、更新状态并协调取消。
/// </summary>
public sealed class PuloniaTaskService : IPuloniaTaskService, IAsyncDisposable
{
    /// <summary>
    /// 防止异常调用给协调器建立过长的总运行计时器。
    /// </summary>
    private const double MaxRunTimeoutSeconds = 7 * 24 * 60 * 60;

    /// <summary>
    /// 读取已保存计划的存储入口。
    /// </summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>
    /// 固定引用、参数和资源版本的准备入口。
    /// </summary>
    private readonly PuloniaTaskBuilder _builder;

    /// <summary>
    /// 按任务类型查找的唯一执行器。
    /// </summary>
    private readonly IReadOnlyDictionary<string, IPuloniaTaskExecutor> _executors;

    /// <summary>
    /// 按任务类型查找的能力定义，用于判断是否需要游戏会话。
    /// </summary>
    private readonly IReadOnlyDictionary<string, PuloniaTaskDefinition> _definitions;

    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; }

    /// <summary>
    /// 游戏型节点的会话与输入所有权协调器。
    /// </summary>
    private readonly PuloniaGameTaskCoordinator _gameTaskCoordinator;

    /// <summary>
    /// 把运行期间收到的外部停止信号转发到本次 Pulonia 运行。
    /// </summary>
    private readonly TaskStopService _taskStopService;

    /// <summary>
    /// 单写入、多调用方提交但只有一个读取者的运行队列。
    /// </summary>
    private readonly Channel<RunState> _queue = Channel.CreateUnbounded<RunState>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    /// <summary>
    /// 当前进程内可查询的全部运行状态。
    /// </summary>
    private readonly Dictionary<Guid, RunState> _runs = [];

    /// <summary>
    /// 保护运行字典本身；每个运行的字段另由其 SyncRoot 保护。
    /// </summary>
    private readonly object _runsGate = new();

    /// <summary>
    /// 应用关闭时终止队列读取和当前运行。
    /// </summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// 唯一队列消费循环。
    /// </summary>
    private readonly Task _worker;

    /// <summary>
    /// 防止重复异步释放。
    /// </summary>
    private int _disposeStarted;

    /// <inheritdoc />
    public event EventHandler<PuloniaTaskRunChangedEventArgs>? RunChanged;

    /// <summary>
    /// 建立协调器，校验执行器注册并启动不触碰游戏环境的串行消费循环。
    /// </summary>
    public PuloniaTaskService(PuloniaTaskStore store, PuloniaTaskBuilder builder,
        IEnumerable<IPuloniaTaskExecutor> executors, PuloniaGameTaskCoordinator gameTaskCoordinator,
        TaskStopService taskStopService)
    {
        _store = store;
        _builder = builder;
        _gameTaskCoordinator = gameTaskCoordinator;
        _taskStopService = taskStopService;
        var executorMap = new Dictionary<string, IPuloniaTaskExecutor>(StringComparer.Ordinal);
        var definitionMap = new Dictionary<string, PuloniaTaskDefinition>(StringComparer.Ordinal);
        foreach (var executor in executors)
        {
            foreach (var definition in executor.Definitions)
            {
                if (!executorMap.TryAdd(definition.TaskType, executor)
                    || !definitionMap.TryAdd(definition.TaskType, definition))
                    throw new InvalidOperationException($"Pulonia 任务类型 {definition.TaskType} 重复注册执行器。");
            }
        }
        _executors = executorMap;
        _definitions = definitionMap;
        Definitions = definitionMap.Values.ToList().AsReadOnly();
        _worker = ProcessQueueAsync();
    }

    /// <inheritdoc />
    public async Task<Guid> EnqueueAsync(PuloniaTaskRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        var fixedRequest = CloneAndValidateRequest(request);
        var plan = await _store.LoadPlanAsync(fixedRequest.PlanId, ct).ConfigureAwait(false)
                   ?? throw new PuloniaTaskValidationException(fixedRequest.PlanId, "要运行的计划不存在。");

        // 快照在进入队列前固定；后续编辑计划、预设或调用参数都不会改变已排队运行。
        var snapshot = await _builder.BuildAsync(plan, new PuloniaTaskBuildOptions
        {
            Definitions = _definitions.Values.ToList(),
            BaseDirectory = AppContext.BaseDirectory,
            AccountId = fixedRequest.AccountId,
            ParameterOverrides = fixedRequest.ParameterOverrides
        }, ct).ConfigureAwait(false);

        var state = new RunState(Guid.NewGuid(), Guid.NewGuid(), fixedRequest, plan.Name, snapshot);
        lock (_runsGate)
            _runs.Add(state.RequestId, state);
        if (!_queue.Writer.TryWrite(state))
        {
            lock (_runsGate)
                _runs.Remove(state.RequestId);
            state.Cancellation.Dispose();
            throw new InvalidOperationException("Pulonia 执行队列已经关闭。");
        }
        RaiseRunChanged(state.RequestId);
        return state.RequestId;
    }

    /// <inheritdoc />
    public Task<PuloniaTaskRunView> GetRunAsync(Guid requestId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GetState(requestId).CreateView());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PuloniaTaskRunView>> ListRunsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        RunState[] states;
        lock (_runsGate)
            states = _runs.Values.ToArray();
        IReadOnlyList<PuloniaTaskRunView> result = states
            .Select(state => state.CreateView())
            .OrderByDescending(view => view.SubmittedAt)
            .ToArray();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<PuloniaTaskRunView> WaitForCompletionAsync(Guid requestId, CancellationToken ct = default)
    {
        var state = GetState(requestId);
        return await state.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelAsync(Guid requestId, CancellationToken ct = default)
    {
        var state = GetState(requestId);
        var completeQueuedCancellation = false;
        var cancelRunning = false;
        lock (state.SyncRoot)
        {
            if (IsTerminal(state.Status))
                return;
            if (state.Status == PuloniaTaskRunStatus.Queued)
            {
                state.Status = PuloniaTaskRunStatus.Cancelled;
                state.Message = "排队请求已取消，未启动任何执行器。";
                state.FinishedAt = DateTimeOffset.UtcNow;
                completeQueuedCancellation = true;
            }
            else
            {
                cancelRunning = true;
            }
        }

        if (completeQueuedCancellation)
            CompleteState(state);
        else if (cancelRunning)
            RequestCancellation(state, TaskStopReason.UserRequested);
        else
            RaiseRunChanged(requestId);
        await state.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 串行读取请求；任一运行的失败不会终止后续队列。
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var state in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                if (state.IsTerminal())
                    continue;
                await ExecuteRunAsync(state).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // 应用关闭由 DisposeAsync 统一把尚未完成的请求收敛到最终状态。
        }
    }

    /// <summary>
    /// 在请求总时限内顺序执行快照树，并在执行器完成清理后设置最终状态。
    /// </summary>
    private async Task ExecuteRunAsync(RunState state)
    {
        lock (state.SyncRoot)
        {
            if (IsTerminal(state.Status))
                return;
            state.Status = PuloniaTaskRunStatus.Running;
            state.StartedAt = DateTimeOffset.UtcNow;
            state.Message = "正在串行执行运行快照。";
        }
        RaiseRunChanged(state.RequestId);

        using var stopRegistration = _taskStopService.Register(reason => RequestCancellation(state, reason));
        using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(state.Request.TimeoutSeconds));
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            state.Cancellation.Token, timeoutCancellation.Token, _shutdown.Token);
        try
        {
            var signal = await ExecuteTaskAsync(state, state.Snapshot.RootTask, runCancellation.Token).ConfigureAwait(false);
            lock (state.SyncRoot)
            {
                state.CurrentTaskAddress = null;
                state.Status = signal == ExecutionSignal.StopPlan || state.HasFinalFailure
                    ? PuloniaTaskRunStatus.Failed
                    : PuloniaTaskRunStatus.Succeeded;
                state.Message = state.Status == PuloniaTaskRunStatus.Succeeded
                    ? "计划运行完成。"
                    : state.Message == "正在串行执行运行快照。"
                        ? "计划因节点失败而停止。"
                        : state.Message;
                state.FinishedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            lock (state.SyncRoot)
            {
                state.CurrentTaskAddress = null;
                state.Status = timeoutCancellation.IsCancellationRequested && !state.Cancellation.IsCancellationRequested
                    ? PuloniaTaskRunStatus.TimedOut
                    : PuloniaTaskRunStatus.Cancelled;
                state.Message = state.Status == PuloniaTaskRunStatus.TimedOut
                    ? $"计划超过总运行时限 {state.Request.TimeoutSeconds:0.###} 秒，当前执行器已退出。"
                    : "计划已取消，当前执行器已退出并释放资源。";
                state.FinishedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            lock (state.SyncRoot)
            {
                state.CurrentTaskAddress = null;
                state.Status = PuloniaTaskRunStatus.Failed;
                state.Message = "执行基础设施失败：" + ex.Message;
                state.FinishedAt = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            CompleteState(state);
        }
    }

    /// <summary>
    /// 将外部停止来源记录到运行状态，再取消本次运行拥有的令牌源。
    /// </summary>
    private void RequestCancellation(RunState state, TaskStopReason reason)
    {
        lock (state.SyncRoot)
        {
            if (IsTerminal(state.Status))
                return;

            state.Status = PuloniaTaskRunStatus.Cancelling;
            state.Message = reason switch
            {
                TaskStopReason.ApplicationShutdown => "应用正在关闭，正在等待当前执行器退出并释放资源。",
                TaskStopReason.RuntimeStopped => "游戏运行环境已停止，正在等待当前执行器退出并释放资源。",
                _ => "已请求取消，正在等待当前执行器退出并释放资源。"
            };
        }

        try
        {
            state.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 运行完成与停止信号并发时，令牌源可能已由关闭流程释放。
        }

        RaiseRunChanged(state.RequestId);
    }

    /// <summary>
    /// 深度优先执行准备树；分组只负责编排，不另外创建执行器。
    /// </summary>
    private async Task<ExecutionSignal> ExecuteTaskAsync(RunState state,
        PuloniaTaskPreparedTask task, CancellationToken runToken)
    {
        runToken.ThrowIfCancellationRequested();
        if (task.TaskType == "group")
        {
            foreach (var child in task.Children)
            {
                var childSignal = await ExecuteTaskAsync(state, child, runToken).ConfigureAwait(false);
                if (childSignal == ExecutionSignal.StopPlan)
                    return childSignal;
                if (childSignal == ExecutionSignal.SkipGroup)
                    break;
            }
            return ExecutionSignal.Continue;
        }

        if (!task.IsEnabled)
        {
            var now = DateTimeOffset.UtcNow;
            AddNodeResult(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, 0,
                PuloniaTaskNodeStatus.Skipped, "节点自身或祖先已关闭。", now, now));
            return ExecutionSignal.Continue;
        }

        if (!_executors.TryGetValue(task.TaskType, out var executor))
        {
            var now = DateTimeOffset.UtcNow;
            AddNodeResult(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, 1,
                PuloniaTaskNodeStatus.Failed, $"没有注册 {task.TaskType} 执行器。", now, now));
            return MarkFinalFailure(state, task, $"没有注册 {task.TaskType} 执行器。");
        }

        var policy = task.Policy;
        var maxAttempts = checked((policy.MaxRetries ?? 0) + 1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            runToken.ThrowIfCancellationRequested();
            SetCurrentTask(state, task.TaskAddress, $"正在执行“{task.Name}”（第 {attempt}/{maxAttempts} 次）。");
            var startedAt = DateTimeOffset.UtcNow;
            PuloniaTaskOutcome? outcome = null;
            Exception? failure = null;
            var nodeTimedOut = false;

            using var nodeTimeout = policy.TimeoutSeconds is { } timeoutSeconds
                ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
                : null;
            using var nodeCancellation = nodeTimeout is null
                ? CancellationTokenSource.CreateLinkedTokenSource(runToken)
                : CancellationTokenSource.CreateLinkedTokenSource(runToken, nodeTimeout.Token);
            try
            {
                var context = new PuloniaTaskExecutionContext(state.RequestId, state.RunId, state.Snapshot, attempt);
                using var gameTaskLease = _definitions[task.TaskType].RequiresGameSession
                    ? await _gameTaskCoordinator.AcquireAsync(nodeCancellation.Token).ConfigureAwait(false)
                    : null;
                outcome = await executor.ExecuteAsync(task, context, nodeCancellation.Token).ConfigureAwait(false);
                // 兼容暂时未主动观察令牌的旧执行器：只有它真正返回后才确认取消或超时完成。
                nodeCancellation.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested)
            {
                var finishedAt = DateTimeOffset.UtcNow;
                AddNodeResult(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, attempt,
                    PuloniaTaskNodeStatus.Cancelled, "节点随计划取消，执行器已退出。", startedAt, finishedAt));
                throw;
            }
            catch (OperationCanceledException) when (nodeTimeout?.IsCancellationRequested == true)
            {
                nodeTimedOut = true;
                failure = new TimeoutException($"节点超过时限 {policy.TimeoutSeconds:0.###} 秒。");
            }
            catch (Exception ex)
            {
                if (runToken.IsCancellationRequested)
                {
                    var finishedAt = DateTimeOffset.UtcNow;
                    AddNodeResult(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, attempt,
                        PuloniaTaskNodeStatus.Cancelled, "节点返回时计划已取消，执行器已退出。", startedAt, finishedAt));
                    throw new OperationCanceledException(runToken);
                }
                if (nodeTimeout?.IsCancellationRequested == true)
                {
                    nodeTimedOut = true;
                    failure = new TimeoutException($"节点超过时限 {policy.TimeoutSeconds:0.###} 秒。", ex);
                }
                else
                {
                    failure = ex;
                }
            }

            var finished = DateTimeOffset.UtcNow;
            var succeeded = outcome?.IsSuccess == true;
            var message = outcome?.Message ?? failure?.Message ?? "执行器未返回结果。";
            var status = nodeTimedOut
                ? PuloniaTaskNodeStatus.TimedOut
                : succeeded ? PuloniaTaskNodeStatus.Succeeded : PuloniaTaskNodeStatus.Failed;
            AddNodeResult(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, attempt,
                status, message, startedAt, finished, outcome?.Data));
            if (succeeded)
                return ExecutionSignal.Continue;

            if (attempt < maxAttempts)
            {
                var retryDelaySeconds = policy.RetryDelaySeconds ?? 0;
                if (retryDelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), runToken).ConfigureAwait(false);
                continue;
            }
            return MarkFinalFailure(state, task, message);
        }

        throw new InvalidOperationException("节点尝试次数计算无效。");
    }

    /// <summary>
    /// 记录最终节点失败，并把失败行为转换为编排信号。
    /// </summary>
    private static ExecutionSignal MarkFinalFailure(RunState state, PuloniaTaskPreparedTask task, string message)
    {
        lock (state.SyncRoot)
        {
            state.HasFinalFailure = true;
            state.Message = $"节点“{task.Name}”失败：{message}";
        }
        return task.Policy.FailureBehavior switch
        {
            "continue" => ExecutionSignal.Continue,
            "skip_group" => ExecutionSignal.SkipGroup,
            _ => ExecutionSignal.StopPlan
        };
    }

    /// <summary>
    /// 更新当前执行节点并通知观察者。
    /// </summary>
    private void SetCurrentTask(RunState state, string address, string message)
    {
        lock (state.SyncRoot)
        {
            state.CurrentTaskAddress = address;
            state.Message = message;
        }
        RaiseRunChanged(state.RequestId);
    }

    /// <summary>
    /// 追加一次节点尝试结果并通知观察者。
    /// </summary>
    private void AddNodeResult(RunState state, PuloniaTaskNodeResult result)
    {
        lock (state.SyncRoot)
            state.NodeResults.Add(result);
        RaiseRunChanged(state.RequestId);
    }

    /// <summary>
    /// 完成等待者并发布最终状态；调用多次仍只完成一次。
    /// </summary>
    private void CompleteState(RunState state)
    {
        PuloniaTaskRunView view;
        lock (state.SyncRoot)
            view = state.CreateViewWithoutLock();
        state.Completion.TrySetResult(view);
        RaiseRunChanged(state.RequestId);
    }

    /// <summary>
    /// 查找请求状态，未知请求使用明确异常。
    /// </summary>
    private RunState GetState(Guid requestId)
    {
        lock (_runsGate)
            return _runs.TryGetValue(requestId, out var state)
                ? state
                : throw new KeyNotFoundException($"Pulonia 请求 {requestId:D} 不存在。");
    }

    /// <summary>
    /// 深复制并校验调用方可填写的请求字段。
    /// </summary>
    private static PuloniaTaskRequest CloneAndValidateRequest(PuloniaTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PuloniaTaskValidator.ValidateId(request.PlanId, "request/plan_id");
        if (!double.IsFinite(request.TimeoutSeconds)
            || request.TimeoutSeconds <= 0
            || request.TimeoutSeconds > MaxRunTimeoutSeconds)
            throw new PuloniaTaskValidationException("request/timeout_seconds",
                $"计划总时限必须在 0—{MaxRunTimeoutSeconds} 秒之间。");
        if (request.ParameterOverrides is null)
            throw new PuloniaTaskValidationException("request/parameter_overrides", "调用参数覆盖不能为空。");
        if (string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 64)
            throw new PuloniaTaskValidationException("request/source", "请求来源必须是 1—64 个字符。");

        var overrides = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (var item in request.ParameterOverrides)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || item.Value is null)
                throw new PuloniaTaskValidationException("request/parameter_overrides", "节点地址和覆盖对象不能为空。");
            overrides.Add(item.Key, (JObject)item.Value.DeepClone());
        }
        return new PuloniaTaskRequest
        {
            PlanId = request.PlanId,
            AccountId = request.AccountId,
            TimeoutSeconds = request.TimeoutSeconds,
            ParameterOverrides = overrides,
            Source = request.Source
        };
    }

    /// <summary>
    /// 判断状态是否已经确认结束。
    /// </summary>
    private static bool IsTerminal(PuloniaTaskRunStatus status)
        => status is PuloniaTaskRunStatus.Succeeded or PuloniaTaskRunStatus.Failed
            or PuloniaTaskRunStatus.Cancelled or PuloniaTaskRunStatus.TimedOut;

    /// <summary>
    /// 在协调器线程之外通知订阅方，订阅方负责切换到自己的 UI 线程。
    /// </summary>
    private void RaiseRunChanged(Guid requestId)
        => RunChanged?.Invoke(this, new PuloniaTaskRunChangedEventArgs(requestId));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        RunState[] states;
        lock (_runsGate)
            states = _runs.Values.ToArray();
        foreach (var state in states)
        {
            lock (state.SyncRoot)
            {
                if (!IsTerminal(state.Status))
                    state.Cancellation.Cancel();
            }
        }
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // 消费循环已经响应应用关闭。
        }

        foreach (var state in states)
        {
            lock (state.SyncRoot)
            {
                if (!IsTerminal(state.Status))
                {
                    state.Status = PuloniaTaskRunStatus.Cancelled;
                    state.Message = "应用关闭，运行已取消。";
                    state.FinishedAt = DateTimeOffset.UtcNow;
                }
            }
            CompleteState(state);
            state.Cancellation.Dispose();
        }
        _shutdown.Dispose();
    }

    /// <summary>
    /// 子节点向父分组返回的最小编排控制信号。
    /// </summary>
    private enum ExecutionSignal
    {
        /// <summary>
        /// 继续执行下一个兄弟节点。
        /// </summary>
        Continue,

        /// <summary>
        /// 跳过当前父分组的剩余节点，再由其父级继续。
        /// </summary>
        SkipGroup,

        /// <summary>
        /// 终止整个计划。
        /// </summary>
        StopPlan
    }

    /// <summary>
    /// 仅在协调器内部可变的一次运行状态。
    /// </summary>
    private sealed class RunState
    {
        /// <summary>
        /// 保护本运行的全部可变字段。
        /// </summary>
        public object SyncRoot { get; } = new();

        /// <summary>
        /// 请求 ID。
        /// </summary>
        public Guid RequestId { get; }

        /// <summary>
        /// 运行 ID。
        /// </summary>
        public Guid RunId { get; }

        /// <summary>
        /// 固定后的调用请求。
        /// </summary>
        public PuloniaTaskRequest Request { get; }

        /// <summary>
        /// 提交时的计划名称。
        /// </summary>
        public string PlanName { get; }

        /// <summary>
        /// 提交时固定的运行快照。
        /// </summary>
        public PuloniaTaskSnapshot Snapshot { get; }

        /// <summary>
        /// 已序列化的快照，查询时无需重复序列化。
        /// </summary>
        public string SnapshotJson { get; }

        /// <summary>
        /// 显式取消当前运行的令牌源。
        /// </summary>
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>
        /// 最终状态等待源，异步 continuation 不在状态锁内执行。
        /// </summary>
        public TaskCompletionSource<PuloniaTaskRunView> Completion { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 已完成的节点尝试结果。
        /// </summary>
        public List<PuloniaTaskNodeResult> NodeResults { get; } = [];

        /// <summary>
        /// 当前运行状态。
        /// </summary>
        public PuloniaTaskRunStatus Status { get; set; } = PuloniaTaskRunStatus.Queued;

        /// <summary>
        /// 当前节点地址。
        /// </summary>
        public string? CurrentTaskAddress { get; set; }

        /// <summary>
        /// 状态摘要。
        /// </summary>
        public string Message { get; set; } = "运行快照已固定，正在等待串行执行。";

        /// <summary>
        /// 提交时间。
        /// </summary>
        public DateTimeOffset SubmittedAt { get; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// 开始时间。
        /// </summary>
        public DateTimeOffset? StartedAt { get; set; }

        /// <summary>
        /// 完成时间。
        /// </summary>
        public DateTimeOffset? FinishedAt { get; set; }

        /// <summary>
        /// 是否至少有一个节点在重试耗尽后失败。
        /// </summary>
        public bool HasFinalFailure { get; set; }

        /// <summary>
        /// 建立排队状态并固定快照 JSON。
        /// </summary>
        public RunState(Guid requestId, Guid runId, PuloniaTaskRequest request, string planName,
            PuloniaTaskSnapshot snapshot)
        {
            RequestId = requestId;
            RunId = runId;
            Request = request;
            PlanName = planName;
            Snapshot = snapshot;
            SnapshotJson = snapshot.ToJson();
        }

        /// <summary>
        /// 在线程安全范围内建立不可变视图。
        /// </summary>
        public PuloniaTaskRunView CreateView()
        {
            lock (SyncRoot)
                return CreateViewWithoutLock();
        }

        /// <summary>
        /// 在调用方已持有状态锁时建立不可变视图。
        /// </summary>
        public PuloniaTaskRunView CreateViewWithoutLock()
            => new(RequestId, RunId, Request.PlanId, PlanName, Request.Source, Status, CurrentTaskAddress,
                Message, SubmittedAt, StartedAt, FinishedAt, SnapshotJson, NodeResults);

        /// <summary>
        /// 判断本运行是否已经进入最终状态。
        /// </summary>
        public bool IsTerminal()
        {
            lock (SyncRoot)
                return PuloniaTaskService.IsTerminal(Status);
        }
    }
}
