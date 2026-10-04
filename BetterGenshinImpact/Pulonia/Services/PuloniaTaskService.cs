using System;
using System.Collections.Generic;
using System.IO;
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
public sealed partial class PuloniaTaskService : IPuloniaTaskService, IAsyncDisposable
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

    /// <inheritdoc />
    public string? RecoveryNotice { get; private set; }

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
    /// 串行化 state.json 候选生成、耐久写入和内存提交。
    /// </summary>
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    /// <summary>
    /// 最近一次成功落盘的当前状态副本。
    /// </summary>
    private PuloniaTaskState _persistentState = new();

    /// <summary>
    /// 已归档历史按运行 ID 建立的只读索引。
    /// </summary>
    private readonly Dictionary<Guid, PuloniaTaskRunRecord> _historyByRunId = [];

    /// <summary>
    /// 状态与历史恢复完成信号；失败时所有公开操作都停止推进。
    /// </summary>
    private readonly Task _initialization;

    /// <summary>
    /// 关键状态写入失败后阻止后续自动动作，避免使用过期账本继续执行。
    /// </summary>
    private Exception? _persistenceFailure;

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
        TaskStopService taskStopService, TimeProvider? timeProvider = null)
    {
        _store = store;
        _builder = builder;
        _gameTaskCoordinator = gameTaskCoordinator;
        _taskStopService = taskStopService;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        foreach (var definition in Definitions)
            PuloniaTaskAvailability.ValidateRules(definition.AvailabilityRules,
                "definition/" + definition.TaskType + "/availability");
        _initialization = InitializePersistentStateAsync();
        _worker = ProcessQueueAsync();
    }

    /// <inheritdoc />
    public Task<Guid> EnqueueAsync(PuloniaTaskRequest request, CancellationToken ct = default)
        => EnqueueCoreAsync(request, null, ct);

    /// <summary>准备完成后将请求与触发游标作为同一耐久状态提交。</summary>
    private async Task<Guid> EnqueueCoreAsync(PuloniaTaskRequest request,
        Action<PuloniaTaskState, Guid>? updateTrigger, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        ThrowIfPersistenceFailed();
        var fixedRequest = CloneAndValidateRequest(request);
        var plan = await _store.LoadPlanAsync(fixedRequest.PlanId, ct).ConfigureAwait(false)
                   ?? throw new PuloniaTaskValidationException(fixedRequest.PlanId, "要运行的计划不存在。");

        // 快照在进入队列前固定；后续编辑计划、预设或调用参数都不会改变已排队运行。
        var snapshot = await _builder.BuildAsync(plan, new PuloniaTaskBuildOptions
        {
            Definitions = _definitions.Values.ToList(),
            BaseDirectory = AppContext.BaseDirectory,
            AccountId = fixedRequest.AccountId,
            TargetTaskId = fixedRequest.TargetTaskId,
            ParameterOverrides = fixedRequest.ParameterOverrides
        }, ct).ConfigureAwait(false);

        var state = new RunState(Guid.NewGuid(), Guid.NewGuid(), fixedRequest, plan.Name, snapshot);
        try
        {
            await PersistCurrentStateAsync(ct, candidate => updateTrigger?.Invoke(candidate, state.RequestId), state).ConfigureAwait(false);
        }
        catch
        {
            lock (_runsGate)
                _runs.Remove(state.RequestId);
            state.Cancellation.Dispose();
            throw;
        }
        if (fixedRequest.TriggerId is not null && fixedRequest.BusyPolicy == PuloniaTaskBusyPolicy.StopCurrent)
            _taskStopService.StopAll(TaskStopReason.UserRequested);
        state.QueueReady = true;
        if (!_queue.Writer.TryWrite(state))
        {
            lock (_runsGate)
                _runs.Remove(state.RequestId);
            await PersistCurrentStateAsync(CancellationToken.None).ConfigureAwait(false);
            state.Cancellation.Dispose();
            throw new InvalidOperationException("Pulonia 执行队列已经关闭。");
        }
        RaiseRunChanged(state.RequestId);
        return state.RequestId;
    }

    /// <inheritdoc />
    public async Task<PuloniaTaskRunView> GetRunAsync(Guid requestId, CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        return GetState(requestId).CreateView();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PuloniaTaskRunView>> ListRunsAsync(CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        RunState[] states;
        lock (_runsGate)
            states = _runs.Values.ToArray();
        IReadOnlyList<PuloniaTaskRunView> result = states
            .Select(state => state.CreateView())
            .OrderByDescending(view => view.SubmittedAt)
            .ToArray();
        return result;
    }

    /// <inheritdoc />
    public async Task<PuloniaTaskRunView> WaitForCompletionAsync(Guid requestId, CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        var state = GetState(requestId);
        return await state.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelAsync(Guid requestId, CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
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
            await CompleteStateAsync(state).ConfigureAwait(false);
        else if (cancelRunning)
            RequestCancellation(state, TaskStopReason.UserRequested);
        else
            RaiseRunChanged(requestId);
        await state.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Guid> ResumeAsync(Guid runId, string? startTaskAddress = null,
        bool allowUncertainReplay = false, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        ThrowIfPersistenceFailed();
        PuloniaTaskRunRecord source;
        lock (_runsGate)
        {
            if (!_historyByRunId.TryGetValue(runId, out source!))
                throw new KeyNotFoundException($"Pulonia 历史运行 {runId:D} 不存在。");
        }

        var snapshot = PuloniaTaskJson.ReadSnapshot(source.SnapshotJson);
        await ValidateSnapshotResourcesAsync(snapshot.RootTask, ct).ConfigureAwait(false);
        var allAddresses = EnumerateLeafTasks(snapshot.RootTask).Select(task => task.TaskAddress)
            .ToHashSet(StringComparer.Ordinal);
        if (startTaskAddress is not null && !allAddresses.Contains(startTaskAddress))
            throw new PuloniaTaskValidationException(startTaskAddress, "所选节点不在历史运行快照中。");

        var hasUnverifiedNode = source.NodeResults
            .GroupBy(item => item.TaskAddress, StringComparer.Ordinal)
            .Select(group => group.Last())
            .Any(item => item.OutcomeKind is PuloniaTaskOutcomeKind.ExecutedUnverified
                or PuloniaTaskOutcomeKind.PartiallySucceeded or PuloniaTaskOutcomeKind.NeedsAttention);
        var hasUncertain = source.OperationIntent is not null || source.Status == PuloniaTaskRunStatus.NeedsAttention
                           || hasUnverifiedNode;
        if (hasUncertain && !allowUncertainReplay)
            throw new PuloniaTaskValidationException(source.RunId.ToString("D"),
                "历史运行包含未核验操作，不能自动重放；请核验游戏状态后明确选择起始节点。");

        var completed = source.NodeResults
            .Where(item => item.Status == PuloniaTaskNodeStatus.Succeeded
                           && item.OutcomeKind == PuloniaTaskOutcomeKind.Succeeded)
            .Select(item => item.TaskAddress)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (startTaskAddress is not null)
        {
            completed.Clear();
            foreach (var task in EnumerateLeafTasks(snapshot.RootTask))
            {
                if (task.TaskAddress == startTaskAddress)
                    break;
                completed.Add(task.TaskAddress);
            }
        }

        // 人工续跑是新授权，不能继承旧触发窗口、旧配置签名或“停止当前”策略。
        var resumeRequest = CloneAndValidateRequest(new PuloniaTaskRequest
        {
            PlanId = source.Request.PlanId, TargetTaskId = source.Request.TargetTaskId,
            AccountId = source.Request.AccountId, WorldOwnerAccountId = source.Request.WorldOwnerAccountId,
            Server = source.Request.Server, ServerUtcOffsetMinutes = source.Request.ServerUtcOffsetMinutes,
            TimeoutSeconds = source.Request.TimeoutSeconds, ParameterOverrides = source.Request.ParameterOverrides,
            Source = "resume"
        });
        var state = new RunState(Guid.NewGuid(), Guid.NewGuid(), resumeRequest,
            source.PlanName, snapshot, completed, startTaskAddress, source.RunId);
        lock (_runsGate)
            _runs.Add(state.RequestId, state);
        try
        {
            if (allowUncertainReplay)
                await PersistResumeRequestAsync(source.RunId, ct).ConfigureAwait(false);
            else
                await PersistCurrentStateAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_runsGate)
                _runs.Remove(state.RequestId);
            state.Cancellation.Dispose();
            throw;
        }
        state.QueueReady = true;
        if (!_queue.Writer.TryWrite(state))
            throw new InvalidOperationException("Pulonia 执行队列已经关闭。");
        RaiseRunChanged(state.RequestId);
        return state.RequestId;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PuloniaTaskLedgerEntry>> ListLedgerAsync(CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return PuloniaTaskJson.ReadState(PuloniaTaskJson.WriteState(_persistentState)).Ledger;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// 恢复当前状态、未完成队列和不可变历史；遗留活动运行不会自动重放。
    /// </summary>
    private async Task InitializePersistentStateAsync()
    {
        var loaded = await _store.LoadStateAsync(_shutdown.Token).ConfigureAwait(false);
        _persistentState = loaded.State;
        if (loaded.RecoveredFromBackup)
            RecoveryNotice = "运行状态已从备份回退；回退时间段内的游戏操作需要核验。";

        var history = await _store.ListHistoryAsync(_shutdown.Token).ConfigureAwait(false);
        lock (_runsGate)
        {
            foreach (var record in history)
            {
                _historyByRunId[record.RunId] = record;
                _runs[record.RequestId] = RunState.FromRecord(record, true);
            }
        }

        // 上次进程留下的活动运行只能转为中断或待处理，绝不按成功或未执行推断。
        if (_persistentState.ActiveRun is { } interrupted)
        {
            interrupted.FinishedAt = DateTimeOffset.UtcNow;
            if (interrupted.OperationIntent is not null)
            {
                interrupted.Status = PuloniaTaskRunStatus.NeedsAttention;
                interrupted.Message = "应用中断时存在已落盘操作意图，必须先核验副作用。";
                _persistentState.UncertainOperations.Add(interrupted.OperationIntent);
                interrupted.NodeResults.Add(new PuloniaTaskNodeResult(
                    interrupted.OperationIntent.TaskAddress, "中断节点", "unknown", 0,
                    PuloniaTaskNodeStatus.NeedsAttention, interrupted.Message,
                    interrupted.OperationIntent.CreatedAt, interrupted.FinishedAt.Value,
                    outcomeKind: PuloniaTaskOutcomeKind.NeedsAttention));
            }
            else
            {
                interrupted.Status = PuloniaTaskRunStatus.Interrupted;
                interrupted.Message = "应用在运行期间中断，可沿用原快照继续未完成节点。";
            }
            _persistentState.ActiveRun = null;
            _persistentState.PendingArchives.Add(interrupted);
            RecoveryNotice = RecoveryNotice is null
                ? "检测到上次未正常结束的运行，已保留为可续跑历史。"
                : RecoveryNotice + " 同时检测到上次未正常结束的运行。";
        }

        foreach (var queuedRecord in _persistentState.PendingRequests.ToArray())
        {
            var queued = RunState.FromRecord(queuedRecord, false);
            lock (_runsGate)
                _runs[queued.RequestId] = queued;
            if (!_queue.Writer.TryWrite(queued))
                throw new InvalidOperationException("恢复 Pulonia 排队请求时队列已经关闭。");
        }

        await FlushPendingArchivesAsync(_shutdown.Token).ConfigureAwait(false);
        if (loaded.RecoveredFromBackup || _persistentState.ActiveRun is not null)
            await PersistCriticalStateAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 重试待归档运行，并在历史文件成功后从当前状态移除。
    /// </summary>
    private async Task FlushPendingArchivesAsync(CancellationToken ct)
    {
        foreach (var record in _persistentState.PendingArchives.ToArray())
        {
            await _store.ArchiveRunAsync(record, ct).ConfigureAwait(false);
            lock (_runsGate)
            {
                _historyByRunId[record.RunId] = record;
                _runs[record.RequestId] = RunState.FromRecord(record, true);
            }
            _persistentState.PendingArchives.RemoveAll(item => item.RunId == record.RunId);
        }
        await PersistCurrentStateAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据当前运行集合生成一份候选状态，成功替换文件后才更新内存状态。
    /// </summary>
    private async Task PersistCurrentStateAsync(CancellationToken ct, Action<PuloniaTaskState>? update = null,
        RunState? addition = null)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var candidate = ClonePersistentState();
            update?.Invoke(candidate);
            if (addition is not null)
                lock (_runsGate) _runs.Add(addition.RequestId, addition);
            candidate.PendingRequests.Clear();
            candidate.ActiveRun = null;
            RunState[] states;
            lock (_runsGate)
                states = _runs.Values.Where(item => !item.IsHistorical).ToArray();
            foreach (var state in states)
            {
                var record = state.ToRecord();
                UpdateTriggerRunStatus(candidate, record);
                if (record.Status == PuloniaTaskRunStatus.Queued)
                    candidate.PendingRequests.Add(record);
                else if (record.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling)
                {
                    if (candidate.ActiveRun is not null && candidate.ActiveRun.RunId != record.RunId)
                        throw new InvalidOperationException("Pulonia 状态中出现多个活动运行。");
                    candidate.ActiveRun = record;
                }
            }
            candidate.Sequence = checked(candidate.Sequence + 1);
            try { await _store.SaveStateAsync(candidate, ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                // 新请求不能被另一次耐久写入复活；失败时在同一状态门内撤销暂存。
                if (addition is not null) lock (_runsGate) _runs.Remove(addition.RequestId);
                if (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    Volatile.Write(ref _persistenceFailure, ex);
                throw;
            }
            _persistentState = candidate;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// 将用户明确释放未决占用与关联续跑请求作为同一次状态替换提交。
    /// </summary>
    private async Task PersistResumeRequestAsync(Guid sourceRunId, CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var candidate = ClonePersistentState();
            candidate.UncertainOperations.RemoveAll(item => item.RunId == sourceRunId);
            candidate.PendingRequests.Clear();
            candidate.ActiveRun = null;
            RunState[] states;
            lock (_runsGate)
                states = _runs.Values.Where(item => !item.IsHistorical).ToArray();
            foreach (var state in states)
            {
                var record = state.ToRecord();
                if (record.Status == PuloniaTaskRunStatus.Queued)
                    candidate.PendingRequests.Add(record);
                else if (record.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling)
                    candidate.ActiveRun = record;
            }
            candidate.Sequence = checked(candidate.Sequence + 1);
            await _store.SaveStateAsync(candidate, ct).ConfigureAwait(false);
            _persistentState = candidate;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// 深复制最近一次耐久状态，防止候选写入失败时污染当前版本。
    /// </summary>
    private PuloniaTaskState ClonePersistentState()
        => PuloniaTaskJson.ReadState(PuloniaTaskJson.WriteState(_persistentState));

    /// <summary>
    /// 串行读取请求；任一运行的失败不会终止后续队列。
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        try
        {
            await _initialization.ConfigureAwait(false);
            await foreach (var wake in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                // 通道只负责通知；从耐久队列挑选仍有效的请求，配置失效或过期时先归档再处理后续请求。
                while (!_shutdown.IsCancellationRequested)
                {
                    RunState[] pending;
                    lock (_runsGate) pending = _runs.Values.Where(item => item.QueueReady && item.GetStatus() == PuloniaTaskRunStatus.Queued)
                        .OrderByDescending(item => item.Request.BusyPolicy == PuloniaTaskBusyPolicy.StopCurrent)
                        .ThenBy(item => item.SubmittedAt).ToArray();
                    if (pending.Length == 0) break;
                    var executed = false;
                    foreach (var state in pending)
                    {
                        if (!await AdmitQueuedRunAsync(state).ConfigureAwait(false)) continue;
                        await ExecuteRunAsync(state).ConfigureAwait(false);
                        executed = true;
                        break;
                    }
                    if (Volatile.Read(ref _persistenceFailure) is not null) return;
                    if (!executed) await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, _shutdown.Token).ConfigureAwait(false);
                }
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
        try
        {
            await PersistCriticalStateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _persistenceFailure, ex);
            lock (state.SyncRoot)
            {
                state.Status = PuloniaTaskRunStatus.NeedsAttention;
                state.Message = "无法持久化活动运行，未启动任务执行器：" + ex.Message;
                state.FinishedAt = DateTimeOffset.UtcNow;
            }
            await CompleteStateAsync(state).ConfigureAwait(false);
            return;
        }
        RaiseRunChanged(state.RequestId);

        using var stopRegistration = _taskStopService.Register(reason => RequestCancellation(state, reason));
        // 未配置总时限时不创建定时取消信号；用户停止、游戏退出和宿主关闭仍正常取消。
        using var timeoutCancellation = state.Request.TimeoutSeconds is { } runTimeout
            ? new CancellationTokenSource(TimeSpan.FromSeconds(runTimeout))
            : new CancellationTokenSource();
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            state.Cancellation.Token, timeoutCancellation.Token, _shutdown.Token);
        try
        {
            var signal = await ExecuteTaskAsync(state, state.Snapshot.RootTask, runCancellation.Token).ConfigureAwait(false);
            runCancellation.Token.ThrowIfCancellationRequested();
            lock (state.SyncRoot)
            {
                state.CurrentTaskAddress = null;
                state.Status = state.RequiresAttention
                    ? PuloniaTaskRunStatus.NeedsAttention
                    : signal == ExecutionSignal.StopPlan || state.HasFinalFailure
                        ? PuloniaTaskRunStatus.Failed
                        : PuloniaTaskRunStatus.Succeeded;
                state.Message = state.Status == PuloniaTaskRunStatus.Succeeded
                    ? "计划运行完成。"
                    : state.Status == PuloniaTaskRunStatus.NeedsAttention
                        ? "计划存在未核验副作用，已停止自动推进。"
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
                state.Status = state.OperationIntent is not null
                    ? PuloniaTaskRunStatus.NeedsAttention
                    : timeoutCancellation.IsCancellationRequested && !state.Cancellation.IsCancellationRequested
                        ? PuloniaTaskRunStatus.TimedOut
                        : PuloniaTaskRunStatus.Cancelled;
                state.RequiresAttention = state.Status == PuloniaTaskRunStatus.NeedsAttention;
                state.Message = state.Status switch
                {
                    PuloniaTaskRunStatus.NeedsAttention => "取消或超时时仍有未确认操作，必须先核验副作用。",
                    PuloniaTaskRunStatus.TimedOut => $"计划超过总运行时限 {state.Request.TimeoutSeconds:0.###} 秒，当前执行器已退出。",
                    _ => "计划已取消，当前执行器已退出并释放资源。"
                };
                state.FinishedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            lock (state.SyncRoot)
            {
                state.CurrentTaskAddress = null;
                state.Status = state.OperationIntent is null
                    ? PuloniaTaskRunStatus.Failed
                    : PuloniaTaskRunStatus.NeedsAttention;
                state.RequiresAttention = state.Status == PuloniaTaskRunStatus.NeedsAttention;
                state.Message = state.RequiresAttention
                    ? "执行异常且存在未确认操作，必须先核验副作用：" + ex.Message
                    : "执行基础设施失败：" + ex.Message;
                state.FinishedAt = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            await CompleteStateAsync(state).ConfigureAwait(false);
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
    /// 关键状态写入失败后拒绝新增或续跑请求，保留现场等待人工处理。
    /// </summary>
    private void ThrowIfPersistenceFailed()
    {
        if (Volatile.Read(ref _persistenceFailure) is { } failure)
            throw new InvalidOperationException("Pulonia 关键状态写入失败，已停止自动运行。", failure);
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
            await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, 0,
                PuloniaTaskNodeStatus.Skipped, "节点自身或祖先已关闭。", now, now)).ConfigureAwait(false);
            return ExecutionSignal.Continue;
        }

        if (state.CompletedTaskAddresses.Contains(task.TaskAddress))
        {
            var now = DateTimeOffset.UtcNow;
            await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, 0,
                PuloniaTaskNodeStatus.Skipped, "续跑沿用原快照，已确认完成或位于显式起点之前。", now, now))
                .ConfigureAwait(false);
            return ExecutionSignal.Continue;
        }

        if (!_executors.TryGetValue(task.TaskType, out var executor))
        {
            var now = DateTimeOffset.UtcNow;
            await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, 1,
                PuloniaTaskNodeStatus.Failed, $"没有注册 {task.TaskType} 执行器。", now, now)).ConfigureAwait(false);
            return MarkFinalFailure(state, task, $"没有注册 {task.TaskType} 执行器。");
        }


        var rules = PuloniaTaskAvailability.GetApplicableRules(_definitions[task.TaskType], task);
        var availability = await EvaluateAvailabilityAsync(state.Request, rules, runToken).ConfigureAwait(false);
        if (!availability.IsAllowed)
        {
            var now = DateTimeOffset.UtcNow;
            var nodeStatus = availability.NeedsAttention
                ? PuloniaTaskNodeStatus.NeedsAttention
                : PuloniaTaskNodeStatus.Skipped;
            await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, 0,
                nodeStatus, availability.Message, now, now,
                outcomeKind: availability.NeedsAttention
                    ? PuloniaTaskOutcomeKind.NeedsAttention
                    : PuloniaTaskOutcomeKind.Skipped)).ConfigureAwait(false);
            if (!availability.NeedsAttention)
                return ExecutionSignal.Continue;
            lock (state.SyncRoot)
            {
                state.RequiresAttention = true;
                state.HasFinalFailure = true;
                state.Message = availability.Message;
            }
            return ExecutionSignal.StopPlan;
        }

        var policy = task.Policy;
        var maxAttempts = checked((policy.MaxRetries ?? 0) + 1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            runToken.ThrowIfCancellationRequested();
            await SetCurrentTaskAsync(state, task.TaskAddress,
                $"正在执行“{task.Name}”（第 {attempt}/{maxAttempts} 次）。").ConfigureAwait(false);
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
                await PersistOperationIntentAsync(state, task, rules, nodeCancellation.Token).ConfigureAwait(false);
                var context = new PuloniaTaskExecutionContext(state.RequestId, state.RunId, state.Snapshot, attempt,
                    task.TaskAddress, (completionEvent, token) =>
                        ReportCompletionEventAsync(state, task, attempt, rules, completionEvent, token));
                using var gameTaskLease = _definitions[task.TaskType].RequiresGameSession
                    ? await _gameTaskCoordinator.AcquireAsync(nodeCancellation.Token).ConfigureAwait(false)
                    : null;
                outcome = await executor.ExecuteAsync(task, context, nodeCancellation.Token).ConfigureAwait(false);
                // 兼容暂时未主动观察令牌的旧执行器：只有它真正返回后才确认取消或超时完成。
                nodeCancellation.Token.ThrowIfCancellationRequested();
                await ClearOperationIntentAsync(state, nodeCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested)
            {
                var finishedAt = DateTimeOffset.UtcNow;
                var needsAttention = state.OperationIntent is not null;
                await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, attempt,
                    needsAttention ? PuloniaTaskNodeStatus.NeedsAttention : PuloniaTaskNodeStatus.Cancelled,
                    needsAttention ? "节点随计划取消，但仍有未确认操作。" : "节点随计划取消，执行器已退出。",
                    startedAt, finishedAt, outcomeKind: needsAttention
                        ? PuloniaTaskOutcomeKind.NeedsAttention
                        : PuloniaTaskOutcomeKind.Cancelled))
                    .ConfigureAwait(false);
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
                    var needsAttention = state.OperationIntent is not null;
                    await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, attempt,
                        needsAttention ? PuloniaTaskNodeStatus.NeedsAttention : PuloniaTaskNodeStatus.Cancelled,
                        needsAttention ? "节点返回时计划已取消，但仍有未确认操作。" : "节点返回时计划已取消，执行器已退出。",
                        startedAt, finishedAt, outcomeKind: needsAttention
                            ? PuloniaTaskOutcomeKind.NeedsAttention
                            : PuloniaTaskOutcomeKind.Cancelled))
                        .ConfigureAwait(false);
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
            if (state.OperationIntent is not null)
            {
                lock (state.SyncRoot)
                {
                    state.RequiresAttention = true;
                    state.HasFinalFailure = true;
                }
                await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType,
                    attempt, PuloniaTaskNodeStatus.NeedsAttention,
                    "执行异常或超时后仍有未确认操作，已停止自动重试。", startedAt, finished,
                    outcomeKind: PuloniaTaskOutcomeKind.NeedsAttention)).ConfigureAwait(false);
                return ExecutionSignal.StopPlan;
            }
            var succeeded = outcome?.IsSuccess == true;
            // 配置明确禁用的 Shell 等节点记为跳过，不触发失败重试或停止后续任务。
            var skipped = outcome?.Kind == PuloniaTaskOutcomeKind.Skipped;
            var message = outcome?.Message ?? failure?.Message ?? "执行器未返回结果。";
            var status = nodeTimedOut
                ? PuloniaTaskNodeStatus.TimedOut
                : skipped ? PuloniaTaskNodeStatus.Skipped
                : succeeded ? PuloniaTaskNodeStatus.Succeeded : PuloniaTaskNodeStatus.Failed;
            await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, attempt,
                status, message, startedAt, finished, outcome?.Data, outcome?.Kind, outcome?.Evidence))
                .ConfigureAwait(false);
            if (succeeded || skipped)
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
    /// 从最近一次耐久状态读取账本和未决操作并检查节点可用性。
    /// </summary>
    private async Task<PuloniaTaskAvailabilityDecision> EvaluateAvailabilityAsync(PuloniaTaskRequest request,
        IReadOnlyList<PuloniaTaskAvailabilityRule> rules, CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return PuloniaTaskAvailability.Evaluate(request, rules, _persistentState.Ledger,
                _persistentState.UncertainOperations, DateTimeOffset.UtcNow);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <summary>
    /// 在执行可能产生副作用的动作前保存操作意图和规则占用。
    /// </summary>
    private async Task PersistOperationIntentAsync(RunState state, PuloniaTaskPreparedTask task,
        IReadOnlyList<PuloniaTaskAvailabilityRule> rules, CancellationToken ct)
    {
        var reservations = PuloniaTaskAvailability.CreateReservations(state.Request, rules);
        if (reservations.Count == 0)
            return;
        var intent = new PuloniaTaskOperationIntent
        {
            RunId = state.RunId,
            TaskAddress = task.TaskAddress,
            RuleIds = reservations.Select(item => item.RuleId).ToList(),
            Reservations = reservations,
            CreatedAt = DateTimeOffset.UtcNow
        };
        lock (state.SyncRoot)
            state.OperationIntent = intent;
        try
        {
            await PersistCurrentStateAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            lock (state.SyncRoot)
                state.OperationIntent = null;
            throw;
        }
    }

    /// <summary>
    /// 将执行器确认事件、证据、额度变化和节点意图结算为一次状态文件更新。
    /// </summary>
    private async Task ReportCompletionEventAsync(RunState state, PuloniaTaskPreparedTask task, int attempt,
        IReadOnlyList<PuloniaTaskAvailabilityRule> rules, PuloniaTaskCompletionEvent completionEvent,
        CancellationToken ct)
    {
        if (completionEvent.Units <= 0)
            throw new PuloniaTaskValidationException(task.TaskAddress, "完成事件单位数必须大于 0。");
        var rule = rules.SingleOrDefault(item => item.RuleId == completionEvent.RuleId)
                   ?? throw new PuloniaTaskValidationException(task.TaskAddress,
                       $"完成事件引用了当前节点未声明的规则 {completionEvent.RuleId}。");
        var eventKey = string.IsNullOrWhiteSpace(completionEvent.EventKey)
            ? $"{state.RunId:N}:{task.TaskAddress}:{rule.RuleId}"
            : completionEvent.EventKey.Trim();
        if (eventKey.Length > 256)
            throw new PuloniaTaskValidationException(task.TaskAddress, "完成事件键不能超过 256 个字符。");
        // 执行器仍持有证据对象；提交前固定副本，防止后续修改污染已经确认的事实。
        var entry = PuloniaTaskJson.Read<PuloniaTaskLedgerEntry>(PuloniaTaskJson.Write(
            PuloniaTaskAvailability.CreateLedgerEntry(state.Request, rule, completionEvent,
                eventKey, state.RunId, task.TaskAddress)));

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _stateGate.WaitAsync(cleanup.Token).ConfigureAwait(false);
        try
        {
            var candidate = ClonePersistentState();
            var existing = candidate.Ledger.FirstOrDefault(item => item.EventKey == eventKey);
            if (existing is not null && (existing.ScopeKey != entry.ScopeKey || existing.EffectKey != entry.EffectKey))
                throw new PuloniaTaskValidationException(task.TaskAddress, "完成事件键与已有其他资源事件冲突。");
            if (existing is not null)
                return;
            PuloniaTaskOperationIntent intent;
            lock (state.SyncRoot)
                intent = state.OperationIntent
                         ?? throw new PuloniaTaskValidationException(task.TaskAddress, "完成事件缺少已落盘操作意图。");
            if (!intent.RuleIds.Contains(rule.RuleId, StringComparer.Ordinal))
                throw new PuloniaTaskValidationException(task.TaskAddress, "完成事件对应规则已经结算。");
            if (rule.Kind == PuloniaTaskAvailabilityKind.ResetQuota)
            {
                var used = candidate.Ledger.Where(item => item.ScopeKey == entry.ScopeKey
                                                          && item.EffectKey == entry.EffectKey
                                                          && item.WindowKey == entry.WindowKey)
                    .Sum(item => item.Units);
                if (used + entry.Units > rule.Quota)
                    throw new PuloniaTaskValidationException(task.TaskAddress,
                        $"完成事件会使周期额度超过上限 {rule.Quota}。");
            }
            candidate.Ledger.Add(entry);
            var active = candidate.ActiveRun;
            if (active is null || active.RunId != state.RunId || active.OperationIntent is null)
                throw new PuloniaTaskValidationException(task.TaskAddress, "耐久状态中的活动运行或操作意图已变化。");
            // 与账本在同一次原子写盘中留存运行内事实，异常退出也不会丢失已确认的副作用。
            var confirmedEffect = new PuloniaTaskConfirmedEffect
            {
                TaskAddress = task.TaskAddress,
                Attempt = attempt,
                Entry = entry
            };
            active.ConfirmedEffects.Add(confirmedEffect);
            active.OperationIntent.RuleIds.RemoveAll(item => item == rule.RuleId);
            active.OperationIntent.Reservations.RemoveAll(item => item.RuleId == rule.RuleId);
            if (active.OperationIntent.RuleIds.Count == 0)
                active.OperationIntent = null;
            candidate.Sequence = checked(candidate.Sequence + 1);
            await _store.SaveStateAsync(candidate, cleanup.Token).ConfigureAwait(false);
            _persistentState = candidate;
            lock (state.SyncRoot)
            {
                state.ConfirmedEffects.Add(confirmedEffect);
                state.OperationIntent = active.OperationIntent;
            }
        }
        finally
        {
            _stateGate.Release();
        }
        RaiseRunChanged(state.RequestId);
    }

    /// <summary>
    /// 执行器明确返回后清除没有对应确认事件的操作意图，不据此写入 CD。
    /// </summary>
    private async Task ClearOperationIntentAsync(RunState state, CancellationToken ct)
    {
        PuloniaTaskOperationIntent? previous;
        lock (state.SyncRoot)
        {
            previous = state.OperationIntent;
            state.OperationIntent = null;
        }
        if (previous is null)
            return;
        try
        {
            await PersistCriticalStateAsync().ConfigureAwait(false);
        }
        catch
        {
            lock (state.SyncRoot)
                state.OperationIntent = previous;
            throw;
        }
    }

    /// <summary>
    /// 续跑前确认快照引用的旧资源仍存在且内容指纹未变化。
    /// </summary>
    private static async Task ValidateSnapshotResourcesAsync(PuloniaTaskPreparedTask task, CancellationToken ct)
    {
        if (task.IsEnabled && task.Path is not null && task.ResourceVersion is not null
            && task.TaskType is "pathing" or "keymouse" or "javascript")
        {
            string actual;
            if (task.TaskType == "javascript")
            {
                if (!Directory.Exists(task.Path))
                    throw new PuloniaTaskValidationException(task.TaskAddress, "续跑所需的旧 JS 资源目录已不存在。");
                // 续跑与创建、准备、更新确认都只检查根目录 manifest.json 和 main.js，保留重解析点保护。
                actual = await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(task.Path, ct)
                    .ConfigureAwait(false);
                // 不回退到旧全目录算法，也不改写旧快照；旧范围指纹不匹配时需要确认后新执行。
            }
            else
            {
                if (!File.Exists(task.Path))
                    throw new PuloniaTaskValidationException(task.TaskAddress, "续跑所需的旧资源文件已不存在。");
                actual = await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(task.Path, ct)
                    .ConfigureAwait(false);
            }
            if (!string.Equals(actual, task.ResourceVersion, StringComparison.Ordinal))
                throw new PuloniaTaskValidationException(task.TaskAddress,
                    "资源指纹已经变化，不能假装按旧快照精确续跑；请按新版本重新运行。");
        }

        foreach (var child in task.Children)
            await ValidateSnapshotResourcesAsync(child, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 按快照执行顺序枚举全部叶子节点。
    /// </summary>
    private static IEnumerable<PuloniaTaskPreparedTask> EnumerateLeafTasks(PuloniaTaskPreparedTask task)
    {
        if (task.TaskType != "group")
        {
            yield return task;
            yield break;
        }
        foreach (var child in task.Children)
        foreach (var leaf in EnumerateLeafTasks(child))
            yield return leaf;
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
    private async Task SetCurrentTaskAsync(RunState state, string address, string message)
    {
        lock (state.SyncRoot)
        {
            state.CurrentTaskAddress = address;
            state.Message = message;
        }
        await PersistCriticalStateAsync().ConfigureAwait(false);
        RaiseRunChanged(state.RequestId);
    }

    /// <summary>
    /// 追加一次节点尝试结果并通知观察者。
    /// </summary>
    private async Task AddNodeResultAsync(RunState state, PuloniaTaskNodeResult result)
    {
        lock (state.SyncRoot)
            state.NodeResults.Add(result);
        await PersistCriticalStateAsync().ConfigureAwait(false);
        RaiseRunChanged(state.RequestId);
    }

    /// <summary>
    /// 使用独立有限时限保存关键进度，不复用已经取消的业务令牌。
    /// </summary>
    private async Task PersistCriticalStateAsync()
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await PersistCurrentStateAsync(cleanup.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 完成等待者并发布最终状态；调用多次仍只完成一次。
    /// </summary>
    private async Task CompleteStateAsync(RunState state)
    {
        PuloniaTaskRunView view;
        lock (state.SyncRoot)
            view = state.CreateViewWithoutLock();
        var record = state.ToRecord();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await _stateGate.WaitAsync(cleanup.Token).ConfigureAwait(false);
            try
            {
                var candidate = ClonePersistentState();
                candidate.PendingRequests.RemoveAll(item => item.RequestId == state.RequestId);
                candidate.ActiveRun = candidate.ActiveRun?.RequestId == state.RequestId
                    ? null
                    : candidate.ActiveRun;
                if (candidate.PendingArchives.All(item => item.RunId != record.RunId))
                    candidate.PendingArchives.Add(record);
                UpdateTriggerRunStatus(candidate, record);
                if (record.OperationIntent is not null
                    && candidate.UncertainOperations.All(item => item.RunId != record.RunId))
                    candidate.UncertainOperations.Add(record.OperationIntent);
                candidate.Sequence = checked(candidate.Sequence + 1);
                await _store.SaveStateAsync(candidate, cleanup.Token).ConfigureAwait(false);
                _persistentState = candidate;
            }
            finally
            {
                _stateGate.Release();
            }

            await _store.ArchiveRunAsync(record, cleanup.Token).ConfigureAwait(false);

            await _stateGate.WaitAsync(cleanup.Token).ConfigureAwait(false);
            try
            {
                var candidate = ClonePersistentState();
                candidate.PendingArchives.RemoveAll(item => item.RunId == record.RunId);
                candidate.Sequence = checked(candidate.Sequence + 1);
                await _store.SaveStateAsync(candidate, cleanup.Token).ConfigureAwait(false);
                _persistentState = candidate;
            }
            finally
            {
                _stateGate.Release();
            }

            lock (state.SyncRoot)
            {
                state.IsHistorical = true;
                view = state.CreateViewWithoutLock();
            }
            lock (_runsGate)
                _historyByRunId[record.RunId] = record;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _persistenceFailure, ex);
            lock (state.SyncRoot)
            {
                state.Status = PuloniaTaskRunStatus.NeedsAttention;
                state.Message = "运行已经结束，但状态或历史持久化失败：" + ex.Message;
                view = state.CreateViewWithoutLock();
            }
        }
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
        if (request.TimeoutSeconds is { } timeoutSeconds && (!double.IsFinite(timeoutSeconds)
            || timeoutSeconds <= 0
            || timeoutSeconds > MaxRunTimeoutSeconds))
            throw new PuloniaTaskValidationException("request/timeout_seconds",
                $"计划总时限未设置时不限时；显式设置必须大于 0 且不超过 {MaxRunTimeoutSeconds} 秒。");
        if (request.ParameterOverrides is null)
            throw new PuloniaTaskValidationException("request/parameter_overrides", "调用参数覆盖不能为空。");
        if (string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 64)
            throw new PuloniaTaskValidationException("request/source", "请求来源必须是 1—64 个字符。");
        if (request.AccountId is not null)
            PuloniaTaskValidator.ValidateId(request.AccountId, "request/account_id");
        if (request.WorldOwnerAccountId is not null)
            PuloniaTaskValidator.ValidateId(request.WorldOwnerAccountId, "request/world_owner_account_id");
        if (request.TargetTaskId is not null)
            PuloniaTaskValidator.ValidateId(request.TargetTaskId, "request/target_task_id");
        if (!Enum.IsDefined(request.BusyPolicy))
            throw new PuloniaTaskValidationException("request", "忙碌策略无效。");
        if (request.TriggerId is not null)
        {
            PuloniaTaskValidator.ValidateId(request.TriggerId, "request/trigger_id");
            if (string.IsNullOrWhiteSpace(request.TriggerSignature) || request.TriggerSignature.Length != 64
                || request.OccurrenceUtc is null || request.DeadlineUtc is null || request.DeadlineUtc <= request.OccurrenceUtc)
                throw new PuloniaTaskValidationException("request", "自动请求必须带有配置签名、发生时间和有效截止时间。");
        }
        if (string.IsNullOrWhiteSpace(request.Server) || request.Server.Length > 32)
            throw new PuloniaTaskValidationException("request/server", "服务器标识必须是 1—32 个字符。");
        var server = request.Server.Trim().ToLowerInvariant();
        if (server.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw new PuloniaTaskValidationException("request/server",
                "服务器标识只能包含小写字母、数字、下划线或短横线。");
        if (request.ServerUtcOffsetMinutes is < -12 * 60 or > 14 * 60)
            throw new PuloniaTaskValidationException("request/server_utc_offset_minutes",
                "服务器时区偏移必须在 UTC-12 到 UTC+14 之间。");

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
            WorldOwnerAccountId = request.WorldOwnerAccountId,
            Server = server,
            ServerUtcOffsetMinutes = request.ServerUtcOffsetMinutes,
            TimeoutSeconds = request.TimeoutSeconds,
            ParameterOverrides = overrides,
            Source = request.Source,
            TargetTaskId = request.TargetTaskId,
            TriggerId = request.TriggerId,
            TriggerSignature = request.TriggerSignature,
            OccurrenceUtc = request.OccurrenceUtc,
            DeadlineUtc = request.DeadlineUtc,
            BusyPolicy = request.BusyPolicy
        };
    }

    /// <summary>
    /// 判断状态是否已经确认结束。
    /// </summary>
    private static bool IsTerminal(PuloniaTaskRunStatus status)
        => status is PuloniaTaskRunStatus.Succeeded or PuloniaTaskRunStatus.Failed
            or PuloniaTaskRunStatus.Cancelled or PuloniaTaskRunStatus.TimedOut
            or PuloniaTaskRunStatus.Interrupted or PuloniaTaskRunStatus.NeedsAttention or PuloniaTaskRunStatus.Expired;

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
                if (state.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling)
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
        catch when (_initialization.IsFaulted)
        {
            // 初始化错误已由公开 API 报告；释放阶段不能覆盖原始状态文件。
        }

        if (_initialization.IsCompletedSuccessfully)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await PersistCurrentStateAsync(cleanup.Token).ConfigureAwait(false);
            }
            catch
            {
                // 关闭阶段保留先前耐久版本；下次启动会按遗留活动运行进入核验。
            }
        }
        foreach (var state in states)
            state.Cancellation.Dispose();
        _shutdown.Dispose();
        _stateGate.Dispose();
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
        /// <summary>请求耐久写入并完成停止策略后才可被消费者挑选。</summary>
        public volatile bool QueueReady;
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
        /// 每次节点尝试已落盘的确认事件，随运行写入不可变历史。
        /// </summary>
        public List<PuloniaTaskConfirmedEffect> ConfirmedEffects { get; } = [];

        /// <summary>
        /// 续跑时无需再次执行的节点地址。
        /// </summary>
        public HashSet<string> CompletedTaskAddresses { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 显式续跑起点。
        /// </summary>
        public string? ResumeFromTaskAddress { get; }

        /// <summary>
        /// 本次运行关联的历史运行 ID。
        /// </summary>
        public Guid? ResumedFromRunId { get; }

        /// <summary>
        /// 已落盘、尚未结清的副作用操作意图。
        /// </summary>
        public PuloniaTaskOperationIntent? OperationIntent { get; set; }

        /// <summary>
        /// 是否因未确认副作用而必须停止自动推进。
        /// </summary>
        public bool RequiresAttention { get; set; }

        /// <summary>
        /// 是否来自不可变历史，不参与当前 state.json 队列重建。
        /// </summary>
        public bool IsHistorical { get; set; }

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
        public DateTimeOffset SubmittedAt { get; private set; } = DateTimeOffset.UtcNow;

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
            PuloniaTaskSnapshot snapshot, IEnumerable<string>? completedTaskAddresses = null,
            string? resumeFromTaskAddress = null, Guid? resumedFromRunId = null)
        {
            RequestId = requestId;
            RunId = runId;
            Request = request;
            PlanName = planName;
            Snapshot = snapshot;
            SnapshotJson = snapshot.ToJson();
            foreach (var address in completedTaskAddresses ?? [])
                CompletedTaskAddresses.Add(address);
            ResumeFromTaskAddress = resumeFromTaskAddress;
            ResumedFromRunId = resumedFromRunId;
        }

        /// <summary>
        /// 从状态或历史文件恢复运行对象。
        /// </summary>
        public static RunState FromRecord(PuloniaTaskRunRecord record, bool isHistorical)
        {
            var state = new RunState(record.RequestId, record.RunId,
                CloneAndValidateRequest(record.Request), record.PlanName,
                PuloniaTaskJson.ReadSnapshot(record.SnapshotJson), record.CompletedTaskAddresses,
                record.ResumeFromTaskAddress, record.ResumedFromRunId)
            {
                Status = record.Status,
                CurrentTaskAddress = record.CurrentTaskAddress,
                Message = record.Message,
                SubmittedAt = record.SubmittedAt,
                StartedAt = record.StartedAt,
                FinishedAt = record.FinishedAt,
                OperationIntent = record.OperationIntent,
                RequiresAttention = record.Status == PuloniaTaskRunStatus.NeedsAttention,
                IsHistorical = isHistorical,
                QueueReady = true
            };
            state.NodeResults.AddRange(record.NodeResults);
            state.ConfirmedEffects.AddRange(record.ConfirmedEffects);
            state.HasFinalFailure = state.NodeResults.Any(item => item.Status is PuloniaTaskNodeStatus.Failed
                or PuloniaTaskNodeStatus.TimedOut or PuloniaTaskNodeStatus.NeedsAttention);
            if (isHistorical)
                state.Completion.TrySetResult(state.CreateView());
            return state;
        }

        /// <summary>
        /// 建立可写入当前状态或不可变历史的完整记录副本。
        /// </summary>
        public PuloniaTaskRunRecord ToRecord()
        {
            lock (SyncRoot)
            {
                return new PuloniaTaskRunRecord
                {
                    RequestId = RequestId,
                    RunId = RunId,
                    Request = CloneAndValidateRequest(Request),
                    PlanName = PlanName,
                    SnapshotJson = SnapshotJson,
                    Status = Status,
                    CurrentTaskAddress = CurrentTaskAddress,
                    Message = Message,
                    SubmittedAt = SubmittedAt,
                    StartedAt = StartedAt,
                    FinishedAt = FinishedAt,
                    NodeResults = NodeResults.ToList(),
                    ConfirmedEffects = ConfirmedEffects.ToList(),
                    CompletedTaskAddresses = CompletedTaskAddresses.ToList(),
                    ResumeFromTaskAddress = ResumeFromTaskAddress,
                    ResumedFromRunId = ResumedFromRunId,
                    OperationIntent = OperationIntent
                };
            }
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
                Message, SubmittedAt, StartedAt, FinishedAt, SnapshotJson, NodeResults,
                Request.AccountId, Request.WorldOwnerAccountId, ResumedFromRunId, ResumeFromTaskAddress,
                IsHistorical, OperationIntent is not null, ConfirmedEffects, Request);

        /// <summary>
        /// 判断本运行是否已经进入最终状态。
        /// </summary>
        public bool IsTerminal()
        {
            lock (SyncRoot)
                return PuloniaTaskService.IsTerminal(Status);
        }

        /// <summary>仅读取状态，队列挑选不深复制结果与证据。</summary>
        public PuloniaTaskRunStatus GetStatus()
        {
            lock (SyncRoot) return Status;
        }
    }
}
