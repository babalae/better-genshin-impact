using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 本实例的 Pulonia 协调器：每个实例（主实例、桌面分身、云原神实例）各有一个，提交时固定快照，
/// 本实例的运行进入本实例的串行队列，一次只执行一个运行，更新本实例的运行状态并协调取消；不同实例之间并行。
/// 运行状态存在 <c>instances/&lt;实例目录名&gt;/state.json</c>，只由本实例写入；历史目录由所有实例共用。
/// 运行总在提交它的实例执行；主实例经“运行于”整次下发（<c>pulonia.run.submit</c>）的运行由子实例的协调器按本地提交入队，
/// 来源记为 <see cref="PuloniaTaskRequest.DispatchSource"/>，之后的编排与落盘都在子实例完成。
/// </summary>
public sealed partial class PuloniaTaskService : IPuloniaTaskService, IAsyncDisposable
{
    /// <summary>
    /// 防止异常调用给协调器建立过长的总运行计时器。
    /// </summary>
    private const double MaxRunTimeoutSeconds = 7 * 24 * 60 * 60;

    /// <summary>
    /// 恢复提示中最多列出的损坏历史文件数。
    /// </summary>
    private const int MaxListedHistoryFailures = 3;

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
    /// 本进程游戏中的节点执行、账号准备与 UID 读取；所有运行都在本实例的游戏中执行。
    /// </summary>
    private readonly PuloniaLocalNodeRunner _localNodes;

    /// <summary>
    /// 把运行期间收到的外部停止信号转发到本次 Pulonia 运行。
    /// </summary>
    private readonly TaskStopService _taskStopService;

    /// <summary>
    /// 记录跳过的损坏历史、无法归档的运行等不阻断运行的问题。
    /// </summary>
    private readonly ILogger<PuloniaTaskService> _logger;

    /// <summary>
    /// 唤醒唯一消费循环的信号：提交只负责通知，消费者每次醒来都从 <see cref="_runs"/> 挑选仍有效的排队请求。
    /// 不释放：释放后迟到的通知会抛出，而它没有需要释放的等待句柄。
    /// </summary>
    private readonly SemaphoreSlim _queueSignal = new(0);

    /// <summary>
    /// 当前进程内尚未归档的运行：排队、执行中，以及结束后未能写入历史的运行。
    /// </summary>
    private readonly Dictionary<string, RunState> _runs = new(StringComparer.Ordinal);

    /// <summary>
    /// 已归档历史按运行 ID 建立的只读索引；记录去掉了快照正文，快照按需从历史文件读取。
    /// </summary>
    private readonly Dictionary<string, PuloniaTaskRunRecord> _history = new(StringComparer.Ordinal);

    /// <summary>
    /// 保护 <see cref="_runs"/>、<see cref="_history"/> 以及每个运行的可变字段；运行数量只有个位数，一把锁即可，不嵌套其他锁。
    /// </summary>
    private readonly object _runsGate = new();

    /// <summary>
    /// 本实例 state.json 的内存副本与唯一写入入口，含写入失败熔断。
    /// </summary>
    private readonly PuloniaTaskRunStateStore _runState;

    /// <summary>
    /// 状态与历史恢复完成信号；失败时所有公开操作都停止推进。
    /// </summary>
    private readonly Task _initialization;

    /// <summary>
    /// 应用关闭时终止队列读取和当前运行。
    /// </summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// 本实例队列的唯一消费循环。
    /// </summary>
    private readonly Task _worker;

    /// <summary>
    /// 防止重复异步释放。
    /// </summary>
    private int _disposeStarted;

    /// <inheritdoc />
    public event EventHandler<PuloniaTaskRunChangedEventArgs>? RunChanged;

    /// <summary>
    /// 建立本实例的协调器，校验执行器注册，加载本实例的运行状态与共享历史，并启动不触碰游戏环境的消费循环。
    /// </summary>
    /// <param name="store">计划与状态存储</param>
    /// <param name="builder">运行快照准备</param>
    /// <param name="executors">本进程执行器</param>
    /// <param name="gameTaskCoordinator">游戏节点协调器</param>
    /// <param name="taskStopService">本地停止信号</param>
    /// <param name="timeProvider">时钟</param>
    /// <param name="accountSwitcher">账号切换与核验</param>
    /// <param name="logger">日志；为空时不记录</param>
    /// <param name="accountService">账户目录与实例当前账户，其实例键决定读写哪个实例的运行状态；为空时按主实例使用同一存储建立</param>
    public PuloniaTaskService(PuloniaTaskStore store, PuloniaTaskBuilder builder,
        IEnumerable<IPuloniaTaskExecutor> executors, PuloniaGameTaskCoordinator gameTaskCoordinator,
        TaskStopService taskStopService, TimeProvider? timeProvider = null,
        IPuloniaGenshinAccountSwitcher? accountSwitcher = null,
        ILogger<PuloniaTaskService>? logger = null, IPuloniaAccountService? accountService = null)
    {
        _store = store;
        _builder = builder;
        _accounts = accountService ?? new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        // 每个实例只读写自己的 instances/<实例目录名>/state.json
        _runState = new PuloniaTaskRunStateStore(store, _accounts.CurrentInstanceKey);
        _localNodes = new PuloniaLocalNodeRunner(gameTaskCoordinator, accountSwitcher);
        _taskStopService = taskStopService;
        _logger = logger ?? NullLogger<PuloniaTaskService>.Instance;
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
        // 所有实例都是协调器：恢复本实例的运行状态和共享历史，再消费本实例的队列
        _initialization = InitializePersistentStateAsync();
        _worker = ProcessQueueAsync();
    }

    /// <inheritdoc />
    public Task<string> EnqueueAsync(PuloniaTaskRequest request, CancellationToken ct = default)
        => EnqueueCoreAsync(request, null, ct);

    /// <inheritdoc />
    public Task<string> EnqueueDispatchedAsync(string planId, long planRevision, string? targetTaskId,
        CancellationToken ct = default)
        // 与本地提交走同一入口：修订号在读取计划、固定快照的同一步核对，核对与快照使用同一份计划
        => EnqueueCoreAsync(new PuloniaTaskRequest
        {
            PlanId = planId,
            TargetTaskId = targetTaskId,
            Source = PuloniaTaskRequest.DispatchSource
        }, null, ct, planRevision);

    /// <summary>准备完成后将请求与触发游标作为同一耐久状态提交。</summary>
    /// <param name="request">调用方请求</param>
    /// <param name="updateTrigger">与请求同一次写入的触发游标修改；人工提交为空</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="expectedPlanRevision">主实例下发时要求的计划修订号；为空时不核对</param>
    private async Task<string> EnqueueCoreAsync(PuloniaTaskRequest request,
        Action<PuloniaTaskState, string>? updateTrigger, CancellationToken ct, long? expectedPlanRevision = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        _runState.ThrowIfFailed();
        // 服务器偏移入队时先按通用配置填写（UI、定时、热键、命令行、主实例下发都经过这里），调用方的取值被覆盖；
        // 运行开始确定运行账户后改为该账户的服务器时区，不含游戏节点、没有运行账户的运行保留这个值。续跑沿用原请求。
        var validatedRequest = CloneAndValidateRequest(request, serverUtcOffsetMinutes: PuloniaServerTime.GetUtcOffsetMinutes());
        var (plan, fixedRequest, snapshot) = await PrepareRunAsync(validatedRequest, ct, expectedPlanRevision).ConfigureAwait(false);

        // 执行实例总是本实例：运行只在提交它的实例执行
        var state = new RunState(CreateRunId(fixedRequest.PlanId), fixedRequest, plan.Name, snapshot)
        {
            Instance = _accounts.CurrentInstanceKey
        };
        var stopCurrent = fixedRequest.TriggerId is not null && fixedRequest.BusyPolicy == PuloniaTaskBusyPolicy.StopCurrent;
        try
        {
            // 请求只在耐久写入成功后才加入运行集合，失败时不会被其他写入带进 state.json。
            await PersistStateAsync(ct, candidate => updateTrigger?.Invoke(candidate, state.RunId), state, () =>
            {
                // 先停止当前运行，再让新请求对消费者可见：新请求不会在 StopAll 之前被挑中而被自己的停止信号取消。
                // 此时仍持有状态门，被停止的运行要等本次提交结束才能收尾，消费者因此一定先看到新请求。
                if (stopCurrent)
                    _taskStopService.StopAll(TaskStopReason.UserRequested);
                lock (_runsGate)
                    _runs.Add(state.RunId, state);
            }).ConfigureAwait(false);
        }
        catch
        {
            state.Cancellation.Dispose();
            throw;
        }
        _queueSignal.Release();
        RaiseRunChanged(state.RunId);
        return state.RunId;
    }

    /// <summary>
    /// 读取已保存计划并固定运行快照；提交与运行预览共用，保证预览与实际运行使用同一份快照规则。
    /// 计划指定了账户时，快照在准备时固定该账户（普通计划和一条龙相同）；跟随当前账户时运行账户在运行开始时确定。
    /// </summary>
    /// <param name="fixedRequest">已校验并固定服务器时区的请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="expectedPlanRevision">主实例下发时要求的计划修订号；为空时不核对。</param>
    /// <returns>计划、请求与快照。</returns>
    /// <exception cref="InvalidOperationException">计划修订号与下发时不一致。</exception>
    private async Task<(PuloniaTaskPlan Plan, PuloniaTaskRequest Request, PuloniaTaskSnapshot Snapshot)> PrepareRunAsync(
        PuloniaTaskRequest fixedRequest, CancellationToken ct, long? expectedPlanRevision = null)
    {
        var plan = await _store.LoadPlanAsync(fixedRequest.PlanId, ct).ConfigureAwait(false)
                   ?? throw new PuloniaTaskValidationException(fixedRequest.PlanId, "要运行的计划不存在。");
        // 主实例下发：共享计划在主实例保存后又被修改时拒绝，避免按用户没有看到的内容运行
        if (expectedPlanRevision is { } revision && plan.Revision != revision)
            throw new InvalidOperationException("计划已变更，请重新运行。");

        // 快照在进入队列前固定；后续编辑计划、设置方案、账户目录或调用参数都不会改变已排队运行。
        var snapshot = await _builder.BuildAsync(plan, new PuloniaTaskBuildOptions
        {
            Definitions = _definitions.Values.ToList(),
            BaseDirectory = AppContext.BaseDirectory,
            TargetTaskId = fixedRequest.TargetTaskId,
            ParameterOverrides = fixedRequest.ParameterOverrides
        }, ct).ConfigureAwait(false);
        return (plan, fixedRequest, snapshot);
    }

    /// <summary>
    /// 生成候选运行 ID 的入口；默认按本地时间与随机后缀生成，单元测试可替换为固定序列以验证查重。
    /// </summary>
    internal Func<DateTimeOffset, string> RunIdGenerator { get; init; } = PuloniaId.NewRunId;

    /// <summary>
    /// 生成未使用的运行 ID：与本实例的运行、已加载的历史比对，并检查共享历史目录中是否已有该计划的同名历史文件
    /// （其他实例在本实例启动后写入的历史不在内存中）；任一重复时重新生成。
    /// </summary>
    /// <param name="planId">新运行所属计划 ID，历史文件按计划分目录存放。</param>
    private string CreateRunId(string planId)
    {
        while (true)
        {
            string runId;
            lock (_runsGate)
            {
                runId = RunIdGenerator(_timeProvider.GetLocalNow());
                if (_runs.ContainsKey(runId) || _history.ContainsKey(runId))
                    continue;
            }
            // 文件检查在锁外进行，不在运行集合锁内做磁盘访问
            if (!_store.HistoryExists(planId, runId))
                return runId;
        }
    }

    /// <inheritdoc />
    public async Task<PuloniaTaskRunView> GetRunAsync(string runId, CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        lock (_runsGate)
        {
            if (_runs.TryGetValue(runId, out var state))
                return state.CreateView();
            if (_history.TryGetValue(runId, out var record))
                return CreateHistoryView(record);
        }
        throw new KeyNotFoundException($"Pulonia 运行 {runId} 不存在。");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PuloniaTaskRunView>> ListRunsAsync(CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        var views = new List<PuloniaTaskRunView>();
        lock (_runsGate)
        {
            views.AddRange(_runs.Values.Select(state => state.CreateView()));
            views.AddRange(_history.Values.Select(CreateHistoryView));
        }
        return views.OrderByDescending(view => view.SubmittedAt).ToArray();
    }

    /// <inheritdoc />
    public async Task<PuloniaTaskRunView> WaitForCompletionAsync(string runId, CancellationToken ct = default)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        RunState? state;
        PuloniaTaskRunRecord? record;
        lock (_runsGate)
        {
            _runs.TryGetValue(runId, out state);
            _history.TryGetValue(runId, out record);
        }
        if (state is not null)
            return await state.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
        return record is not null
            ? CreateHistoryView(record)
            : throw new KeyNotFoundException($"Pulonia 运行 {runId} 不存在。");
    }

    /// <inheritdoc />
    public async Task CancelAsync(string runId, CancellationToken ct = default)
    {
        if (await RequestCancelCoreAsync(runId, ct).ConfigureAwait(false) is { } state)
            await state.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RequestCancelAsync(string runId, CancellationToken ct = default)
        // 只发出取消：排队请求在这里转为已取消并归档，执行中的运行由执行器随后退出，不等待它结束
        => await RequestCancelCoreAsync(runId, ct).ConfigureAwait(false);

    /// <summary>
    /// 发出取消：排队请求直接转为已取消并归档；执行中的运行取消令牌，由执行器随后退出。
    /// </summary>
    /// <param name="runId">运行 ID</param>
    /// <param name="ct">等待初始化的取消令牌</param>
    /// <returns>仍需等待结束的运行；已结束或已归档时为 null</returns>
    /// <exception cref="KeyNotFoundException">运行不存在</exception>
    private async Task<RunState?> RequestCancelCoreAsync(string runId, CancellationToken ct)
    {
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        RunState? state;
        var completeQueuedCancellation = false;
        lock (_runsGate)
        {
            if (!_runs.TryGetValue(runId, out state))
            {
                // 已归档的历史早已结束，无需取消。
                if (_history.ContainsKey(runId))
                    return null;
                throw new KeyNotFoundException($"Pulonia 运行 {runId} 不存在。");
            }
            if (IsTerminal(state.Status))
                return null;
            if (state.Status == PuloniaTaskRunStatus.Queued)
            {
                state.Status = PuloniaTaskRunStatus.Cancelled;
                state.Message = "排队请求已取消，未启动任何执行器。";
                state.FinishedAt = DateTimeOffset.UtcNow;
                completeQueuedCancellation = true;
            }
        }

        if (completeQueuedCancellation)
            await CompleteStateAsync(state).ConfigureAwait(false);
        else
            RequestCancellation(state, TaskStopReason.UserRequested);
        return state;
    }

    /// <inheritdoc />
    public async Task<string> ResumeAsync(string runId, string? startTaskAddress = null,
        bool allowUncertainReplay = false, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
        await _initialization.WaitAsync(ct).ConfigureAwait(false);
        _runState.ThrowIfFailed();
        PuloniaTaskRunRecord? summary;
        lock (_runsGate)
            _history.TryGetValue(runId, out summary);
        // 常驻历史不含快照，续跑按需读取完整历史文件。
        // 历史目录由所有实例共用：其他实例在本实例启动后才归档的运行不在常驻索引中，按运行 ID 在共享历史中查找。
        var source = summary is not null
            ? await _store.LoadHistoryRecordAsync(summary.Request.PlanId, runId, ct).ConfigureAwait(false)
              ?? throw new KeyNotFoundException($"Pulonia 历史运行 {runId} 的历史文件已不存在。")
            : await _store.FindHistoryRecordAsync(runId, ct).ConfigureAwait(false)
              ?? throw new KeyNotFoundException($"Pulonia 历史运行 {runId} 不存在。");

        var snapshot = PuloniaTaskJson.ReadSnapshot(source.SnapshotJson);
        await ValidateSnapshotResourcesAsync(snapshot.RootTask, ct).ConfigureAwait(false);
        var allAddresses = EnumerateLeafTasks(snapshot.RootTask).Select(task => task.TaskAddress)
            .ToHashSet(StringComparer.Ordinal);
        if (startTaskAddress is not null && !allAddresses.Contains(startTaskAddress))
            throw new PuloniaTaskValidationException(startTaskAddress, "所选节点不在历史运行快照中。");

        // 与历史页的确认提示共用同一判定：运行待处理，或任一节点最后一次尝试的结果不确定
        if (PuloniaTaskRunClassifier.RequiresReplayConfirmation(source.Status, source.NodeResults) && !allowUncertainReplay)
            throw new PuloniaTaskValidationException(source.RunId,
                "历史运行包含未核验结果，不能自动重放；请核验游戏状态后明确选择起始节点。");

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

        // 人工续跑是新授权，不能继承旧触发窗口、配置版本或“停止当前”策略。
        var resumeRequest = CloneAndValidateRequest(new PuloniaTaskRequest
        {
            PlanId = source.Request.PlanId, TargetTaskId = source.Request.TargetTaskId,
            // 续跑沿用原运行的服务器偏移（原运行账户的服务器时区），CD 周期与原运行一致
            ServerUtcOffsetMinutes = source.Request.ServerUtcOffsetMinutes,
            TimeoutSeconds = source.Request.TimeoutSeconds, ParameterOverrides = source.Request.ParameterOverrides,
            Source = "resume"
        });
        // 续跑在发起续跑的实例执行（原运行可能来自任何实例），执行实例记为本实例；
        // 续跑沿用原运行的运行账户：指定账户按原核验 UID 再核验；跟随当前账户时开始运行前核对实例当前账户与原运行账户相同。
        var state = new RunState(CreateRunId(resumeRequest.PlanId), resumeRequest, source.PlanName, snapshot, source.SnapshotJson,
            completed, startTaskAddress, source.RunId)
        {
            Instance = _accounts.CurrentInstanceKey,
            VerifiedUid = IsSwitchedAccount(snapshot) ? source.VerifiedUid : null,
            AccountId = source.AccountId
        };
        try
        {
            await PersistStateAsync(ct, addition: state, committed: () =>
            {
                lock (_runsGate)
                    _runs.Add(state.RunId, state);
            }).ConfigureAwait(false);
        }
        catch
        {
            state.Cancellation.Dispose();
            throw;
        }
        _queueSignal.Release();
        RaiseRunChanged(state.RunId);
        return state.RunId;
    }

    /// <summary>
    /// 恢复本实例的运行状态、未完成队列和共享的不可变历史；遗留活动运行不会自动重放。
    /// 损坏的历史文件与无法归档的运行只跳过并提示，不阻止启动。
    /// </summary>
    private async Task InitializePersistentStateAsync()
    {
        var ct = _shutdown.Token;
        var notices = new List<string>();
        // 每条提示立即生效：页面可能在初始化完成前读取恢复提示。
        void Notify(string notice)
        {
            notices.Add(notice);
            RecoveryNotice = string.Join(" ", notices);
        }

        // 1. 当前状态：正式文件损坏时从备份回退
        if (await _runState.LoadAsync(ct).ConfigureAwait(false))
            Notify("运行状态已从备份回退；回退时间段内的游戏操作需要核验。");

        // 2. 历史：逐个读取，坏文件跳过并提示；常驻内存的记录去掉快照正文，快照按需读取
        var history = await _store.ListHistoryWithFailuresAsync(ct).ConfigureAwait(false);
        lock (_runsGate)
        {
            foreach (var record in history.Items)
                _history[record.RunId] = record.WithSnapshotJson(string.Empty);
        }
        if (history.Failures.Count > 0)
        {
            foreach (var failure in history.Failures)
                _logger.LogWarning("跳过无法读取的 Pulonia 运行历史 {Path}：{Message}", failure.Path, failure.Message);
            Notify($"有 {history.Failures.Count} 份运行历史无法读取，已跳过（文件仍保留，修复后重启即可查看）："
                        + string.Join("；", history.Failures.Take(MaxListedHistoryFailures))
                        + (history.Failures.Count > MaxListedHistoryFailures ? "……" : "。"));
        }

        // 3. 排队请求恢复到运行集合；之后的写入都按运行集合重建队列
        var loaded = _runState.Current;
        lock (_runsGate)
        {
            foreach (var queuedRecord in loaded.PendingRequests)
                _runs[queuedRecord.RunId] = RunState.FromRecord(queuedRecord);
        }

        // 4. 本实例上次进程留下的活动运行只能转为中断，绝不按成功或未执行推断。
        // 先作为待归档运行落盘，归档失败或再次崩溃后重试时内容不变，不会与已写入的历史冲突。
        if (loaded.ActiveRun is { } active)
        {
            var interrupted = active.WithSnapshotJson(active.SnapshotJson);
            interrupted.Status = PuloniaTaskRunStatus.Interrupted;
            interrupted.Message = "应用在运行期间中断，可沿用原快照继续未完成节点。";
            interrupted.FinishedAt = DateTimeOffset.UtcNow;
            // 当前地址只在内存中更新，落盘的值可能已过时，不作为中断位置展示。
            interrupted.CurrentTaskAddress = null;
            await PersistStateAsync(ct, candidate => candidate.PendingArchives.Add(interrupted)).ConfigureAwait(false);
        }
        // 之前未能归档的中断运行仍在待归档中，这里一并提示。
        if (_runState.Current.PendingArchives.Any(item => item.Status == PuloniaTaskRunStatus.Interrupted))
            Notify("检测到上次未正常结束的运行，已保留为可续跑历史。");

        // 5. 归档待归档运行；单个失败保留在待归档中，下次启动重试
        var archived = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in _runState.Current.PendingArchives)
        {
            try
            {
                await _store.ArchiveRunAsync(record, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Pulonia 运行 {RunId} 归档失败，保留在待归档状态", record.RunId);
                Notify($"运行 {record.RunId} 无法写入历史，已保留在运行状态中，下次启动重试：{ex.Message}");
                continue;
            }
            archived.Add(record.RunId);
            lock (_runsGate)
                _history[record.RunId] = record.WithSnapshotJson(string.Empty);
        }

        // 6. 一次写入：移出已归档项；从备份回退时同时把有效状态写回正式文件
        await PersistStateAsync(ct, candidate => candidate.PendingArchives.RemoveAll(item => archived.Contains(item.RunId)))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 把本实例的运行集合连同可选修改写入本实例的 state.json；服务内所有状态写入都经过这里，由运行状态存储串行提交。
    /// </summary>
    /// <param name="ct">等待与保存令牌</param>
    /// <param name="update">对候选状态的额外修改（游标、待归档）</param>
    /// <param name="addition">尚未加入运行集合、要随本次写入一起落盘的新请求</param>
    /// <param name="committed">写入成功后在状态门内执行的回调</param>
    private Task PersistStateAsync(CancellationToken ct, Action<PuloniaTaskState>? update = null,
        RunState? addition = null, Action? committed = null)
        => _runState.MutateAsync(candidate =>
        {
            update?.Invoke(candidate);
            ProjectRuns(candidate, addition);
        }, ct, committed);

    /// <summary>
    /// 按运行集合重建候选状态中的排队请求与唯一活动运行；结束的运行由待归档列表单独管理。
    /// </summary>
    /// <param name="candidate">候选状态，其集合是本次写入的新副本</param>
    /// <param name="addition">随本次写入新增、尚未加入运行集合的请求</param>
    private void ProjectRuns(PuloniaTaskState candidate, RunState? addition)
    {
        candidate.PendingRequests.Clear();
        candidate.ActiveRun = null;
        lock (_runsGate)
        {
            var states = addition is null ? _runs.Values : _runs.Values.Append(addition);
            foreach (var state in states.OrderBy(item => item.SubmittedAt))
            {
                if (state.Status == PuloniaTaskRunStatus.Queued)
                    candidate.PendingRequests.Add(state.ToRecord());
                // 本实例的串行队列同一时刻只有一个运行处于执行或停止中
                else if (state.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling)
                    candidate.ActiveRun = state.ToRecord();
            }
        }
    }

    /// <summary>
    /// 使用独立有限时限保存关键进度，不复用已经取消的业务令牌。
    /// </summary>
    private async Task PersistCriticalStateAsync()
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await PersistStateAsync(cleanup.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 本实例队列的唯一消费循环：每次被唤醒都按优先级依次执行当前可执行的排队请求；任一运行的失败不会终止后续队列。
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        try
        {
            await _initialization.ConfigureAwait(false);
            while (true)
            {
                // 本实例的队列：一次只执行一个运行，结束后再挑下一个；其他实例有各自的队列，与本实例并行。
                // 主实例下发到本实例的运行与本地提交的运行在同一个队列中排队。
                // 配置失效或过期的请求在准入时先归档，再处理后续请求。
                while (!_shutdown.IsCancellationRequested && PickNextQueuedRun() is { } state)
                {
                    if (await AdmitQueuedRunAsync(state).ConfigureAwait(false))
                        await ExecuteRunAsync(state).ConfigureAwait(false);
                    if (_runState.Failure is not null) return;
                }
                await _queueSignal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // 应用关闭由 DisposeAsync 统一把尚未完成的请求收敛到最终状态。
        }
    }

    /// <summary>
    /// 选出下一个可执行的排队请求：“停止当前”触发的请求优先，其余按提交时间先后。
    /// 准入复核会把失效请求转为结束状态，因此循环不会反复挑中同一个请求。
    /// </summary>
    /// <returns>下一个排队请求；没有时为 null。</returns>
    private RunState? PickNextQueuedRun()
    {
        lock (_runsGate)
            return _runs.Values.Where(item => item.Status == PuloniaTaskRunStatus.Queued)
                .OrderByDescending(item => item.Request.BusyPolicy == PuloniaTaskBusyPolicy.StopCurrent)
                .ThenBy(item => item.SubmittedAt).FirstOrDefault();
    }

    /// <summary>
    /// 在请求总时限内顺序执行快照树，并在执行器完成清理后统一判定最终状态。
    /// </summary>
    private async Task ExecuteRunAsync(RunState state)
    {
        lock (_runsGate)
        {
            if (IsTerminal(state.Status))
                return;
            state.Status = PuloniaTaskRunStatus.Running;
            state.StartedAt = DateTimeOffset.UtcNow;
            state.Message = "正在开始执行。";
        }
        try
        {
            await PersistCriticalStateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _runState.ReportFailure(ex);
            lock (_runsGate)
            {
                state.Status = PuloniaTaskRunStatus.NeedsAttention;
                state.Message = "无法持久化活动运行，未启动任务执行器：" + ex.Message;
                state.FinishedAt = DateTimeOffset.UtcNow;
            }
            await CompleteStateAsync(state).ConfigureAwait(false);
            return;
        }
        RaiseRunChanged(state.RunId);

        // 本实例的停止热键、截图器停止与应用关闭作用于本实例的全部运行（含主实例下发的运行）
        using var stopRegistration = _taskStopService.Register(reason => RequestCancellation(state, reason));
        // 未配置总时限时不创建定时取消信号；用户停止、游戏退出和宿主关闭仍正常取消。
        using var timeoutCancellation = state.Request.TimeoutSeconds is { } runTimeout
            ? new CancellationTokenSource(TimeSpan.FromSeconds(runTimeout))
            : new CancellationTokenSource();
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            state.Cancellation.Token, timeoutCancellation.Token, _shutdown.Token);
        Exception? error = null;
        try
        {
            // 含游戏节点的运行在执行第一个节点前确定运行账户（跟随当前账户或指定账户）
            await PrepareRunAccountAsync(state, runCancellation.Token).ConfigureAwait(false);
            // 节点失败的结论记录在运行状态中，根节点返回的编排信号不再参与最终判定。
            await ExecuteTaskAsync(state, state.Snapshot.RootTask, runCancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            // 执行器都已退出：释放运行账户的占用，同一账户的下一个运行（任一实例）可以开始
            state.AccountOccupancy?.Dispose();
            state.AccountOccupancy = null;
        }

        var (status, message) = ResolveFinalStatus(state, error, runCancellation.IsCancellationRequested,
            timeoutCancellation.IsCancellationRequested);
        lock (_runsGate)
        {
            state.CurrentTaskAddress = null;
            state.Status = status;
            state.Message = message;
            state.FinishedAt = DateTimeOffset.UtcNow;
        }
        await CompleteStateAsync(state).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行器全部退出后，由执行结果一次性判定运行的最终状态与原因；不读取进度文字做判断。
    /// </summary>
    /// <param name="state">本次运行</param>
    /// <param name="error">执行流程抛出的异常；正常走完计划时为空</param>
    /// <param name="runCancelled">运行令牌（用户停止、总时限、应用关闭）是否已取消</param>
    /// <param name="timedOut">总时限是否已到</param>
    /// <returns>最终状态与面向用户的原因</returns>
    private (PuloniaTaskRunStatus Status, string Message) ResolveFinalStatus(RunState state, Exception? error,
        bool runCancelled, bool timedOut)
    {
        // 1. 运行令牌已取消，且流程以取消结束（或刚走完就发现取消）：总时限到期且没有显式停止时记为超时
        if (runCancelled && error is null or OperationCanceledException)
            return timedOut && !state.Cancellation.IsCancellationRequested
                ? (PuloniaTaskRunStatus.TimedOut,
                    $"计划超过总运行时限 {state.Request.TimeoutSeconds:0.###} 秒，当前执行器已退出。")
                : (PuloniaTaskRunStatus.Cancelled, "计划已取消，当前执行器已退出并释放资源。");
        // 2. 无法确定运行账户（当前账户未确定、续跑账户不符）：未触碰游戏，原样提示用户处理
        if (error is PuloniaRunAccountException)
            return (PuloniaTaskRunStatus.NeedsAttention, error.Message);
        // 3. 账户准备或核验失败：没有在错误账户上继续业务，需要人工处理
        if (error is PuloniaGenshinAccountException)
            return (PuloniaTaskRunStatus.NeedsAttention, "账户准备或核验失败，未继续执行：" + error.Message);
        // 4. 其余异常：关键状态写入失败后停止自动运行、等待人工处理，否则按执行基础设施失败收尾
        if (error is not null)
            return _runState.Failure is not null
                ? (PuloniaTaskRunStatus.NeedsAttention, "执行状态持久化失败，已停止自动运行：" + error.Message)
                : (PuloniaTaskRunStatus.Failed, "执行基础设施失败：" + error.Message);
        // 5. 计划走完：有节点在重试耗尽后失败时，以最近一次失败说明作为原因
        string? failure;
        lock (_runsGate)
            failure = state.FailureMessage;
        return failure is null
            ? (PuloniaTaskRunStatus.Succeeded, "计划运行完成。")
            : (PuloniaTaskRunStatus.Failed, failure);
    }

    /// <summary>
    /// 将外部停止来源记录到运行状态，再取消本次运行拥有的令牌源。
    /// </summary>
    /// <param name="state">要取消的运行</param>
    /// <param name="reason">停止来源</param>
    /// <param name="message">自定义状态摘要；为空时按来源生成</param>
    private void RequestCancellation(RunState state, TaskStopReason reason, string? message = null)
    {
        lock (_runsGate)
        {
            if (IsTerminal(state.Status))
                return;

            state.Status = PuloniaTaskRunStatus.Cancelling;
            state.Message = message ?? reason switch
            {
                TaskStopReason.ApplicationShutdown => "应用正在关闭，正在等待当前执行器退出并释放资源。",
                TaskStopReason.RuntimeStopped => "游戏运行环境已停止，正在等待当前执行器退出并释放资源。",
                _ => "已请求取消，正在等待当前执行器退出并释放资源。"
            };
        }

        // 在锁外取消：令牌回调可能同步续接执行器的收尾代码。
        try
        {
            state.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 运行完成与停止信号并发时，令牌源可能已由关闭流程释放。
        }

        RaiseRunChanged(state.RunId);
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
                PuloniaTaskNodeStatus.Skipped, "节点自身或祖先已关闭。", now, now,
                reasonCode: PuloniaTaskRunClassifier.DisabledReason)).ConfigureAwait(false);
            return ExecutionSignal.Continue;
        }

        bool alreadyCompleted;
        lock (_runsGate)
            alreadyCompleted = state.CompletedTaskAddresses.Contains(task.TaskAddress);
        if (alreadyCompleted)
        {
            var now = DateTimeOffset.UtcNow;
            await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, 0,
                PuloniaTaskNodeStatus.Skipped, "续跑沿用原快照，已确认完成或位于显式起点之前。", now, now,
                reasonCode: PuloniaTaskRunClassifier.ResumeSkippedReason)).ConfigureAwait(false);
            return ExecutionSignal.Continue;
        }

        if (!_executors.TryGetValue(task.TaskType, out var executor))
        {
            var now = DateTimeOffset.UtcNow;
            await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, 1,
                PuloniaTaskNodeStatus.Failed, $"没有注册 {task.TaskType} 执行器。", now, now)).ConfigureAwait(false);
            return MarkFinalFailure(state, task, $"没有注册 {task.TaskType} 执行器。");
        }


        var routeRule = task.TaskType == "pathing"
            ? task.RefreshRule ?? PuloniaRouteCooldown.Resolve(task.Path, task.Resource, task.Parameters) : null;
        JObject? routeData = null;
        var requiresGameSession = _definitions[task.TaskType].RequiresGameSession;
        if (routeRule is not null)
        {
            // 路线 CD 是唯一的执行前资源限制：仍在 CD 内或一次性已完成时跳过本节点，继续后续任务。
            // 按运行账户的完成记录判定，判定前按文件修改时间检查是否需要重读（其他实例可能刚写入或清除）。
            var cooldown = await GetRouteCooldownStatusAsync(state, routeRule, runToken).ConfigureAwait(false);
            routeData = PuloniaRouteCooldown.Describe(routeRule, cooldown.LastSuccessAt, cooldown.NextEligibleAt);
            if (cooldown.IsSkipped)
            {
                var reason = PuloniaRouteCooldown.FormatSkipReason(routeRule, cooldown);
                // 跳过也更新当前节点与消息（只在内存），运行中能看到哪条路线因 CD 跳过
                SetCurrentTask(state, task.TaskAddress, $"已跳过“{task.Name}”：{reason}");
                var now = DateTimeOffset.UtcNow;
                await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, 0,
                    PuloniaTaskNodeStatus.Skipped, "已跳过：" + reason, now, now, routeData,
                    outcomeKind: PuloniaTaskOutcomeKind.Skipped,
                    reasonCode: PuloniaRouteCooldown.GetReasonCode(cooldown))).ConfigureAwait(false);
                return ExecutionSignal.Continue;
            }
        }

        var policy = task.Policy;
        var maxAttempts = checked((policy.MaxRetries ?? 0) + 1);
        for (var attempt = 1; ; attempt++)
        {
            runToken.ThrowIfCancellationRequested();
            // 不重试的任务不显示“第 1/1 次”，只在有重试机会时标出当前轮次
            SetCurrentTask(state, task.TaskAddress, maxAttempts > 1
                ? $"正在执行“{task.Name}”（第 {attempt}/{maxAttempts} 次）。"
                : $"正在执行“{task.Name}”。");
            var startedAt = DateTimeOffset.UtcNow;
            PuloniaTaskOutcome? outcome = null;
            Exception? failure = null;
            var nodeTimedOut = false;
            DateTimeOffset? executorFinishedAt = null;

            using var nodeTimeout = policy.TimeoutSeconds is { } timeoutSeconds
                ? new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))
                : null;
            using var nodeCancellation = nodeTimeout is null
                ? CancellationTokenSource.CreateLinkedTokenSource(runToken)
                : CancellationTokenSource.CreateLinkedTokenSource(runToken, nodeTimeout.Token);
            try
            {
                // 节点在本实例的游戏中执行：每次尝试都重新取得租约并核验 UID，
                // 运行环境停止后下一次尝试会重新准备，不会拿着已停止的环境重试。
                outcome = await _localNodes.ExecuteAsync(executor, task, state.RunId, state.Snapshot, attempt,
                    requiresGameSession, requiresGameSession ? state.VerifiedUid : null, state.AccountId,
                    nodeCancellation.Token).ConfigureAwait(false);
                executorFinishedAt = DateTimeOffset.UtcNow;
                // 兼容暂时未主动观察令牌的旧执行器：只有它真正返回后才确认取消或超时完成。
                nodeCancellation.Token.ThrowIfCancellationRequested();
            }
            catch (Exception) when (runToken.IsCancellationRequested)
            {
                // 计划已取消：执行器无论以取消还是其他异常退出，都只记录一次取消结果，并结束整次运行。
                await AddNodeResultAsync(state, new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, attempt,
                    PuloniaTaskNodeStatus.Cancelled, "节点随计划取消，执行器已退出。", startedAt, DateTimeOffset.UtcNow,
                    outcomeKind: PuloniaTaskOutcomeKind.Cancelled)).ConfigureAwait(false);
                throw new OperationCanceledException(runToken);
            }
            catch (OperationCanceledException) when (nodeTimeout?.IsCancellationRequested == true)
            {
                nodeTimedOut = true;
                failure = new TimeoutException($"节点超过时限 {policy.TimeoutSeconds:0.###} 秒。");
            }
            catch (PuloniaGenshinAccountException)
            {
                // 核验账号失败：整次运行转为待处理，不进入节点重试
                throw;
            }
            catch (Exception ex)
            {
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

            var finished = executorFinishedAt ?? DateTimeOffset.UtcNow;
            var succeeded = failure is null && !nodeTimedOut && outcome?.IsSuccess == true;
            if (routeRule is not null)
                // 地图追踪执行器已将本次返回值映射到完成状态，不通过用于展示的 JSON 字段再次判定。
                succeeded &= outcome?.Kind == PuloniaTaskOutcomeKind.Succeeded;
            // 配置明确禁用的 Shell 等节点记为跳过，不触发失败重试或停止后续任务。
            var skipped = outcome?.Kind == PuloniaTaskOutcomeKind.Skipped;
            var message = outcome?.Message ?? failure?.Message ?? "执行器未返回结果。";
            var status = nodeTimedOut
                ? PuloniaTaskNodeStatus.TimedOut
                : skipped ? PuloniaTaskNodeStatus.Skipped
                : succeeded ? PuloniaTaskNodeStatus.Succeeded : PuloniaTaskNodeStatus.Failed;
            var resultData = outcome is null ? new JObject() : (JObject)outcome.Data.DeepClone();
            if (routeData is not null)
                foreach (var property in routeData.Properties()) resultData[property.Name] = property.Value.DeepClone();
            // 成功时只算一次下次可执行时间，写入节点结果；完成记录不保存它，判定时按当前规则重算
            if (succeeded && routeRule is not null)
            {
                var nextEligible = PuloniaRouteCooldown.GetNextEligibleAt(routeRule, finished, state.Request.ServerUtcOffsetMinutes);
                foreach (var property in PuloniaRouteCooldown.Describe(routeRule, finished, nextEligible).Properties())
                    resultData[property.Name] = property.Value.DeepClone();
            }
            if (routeRule is not null && !succeeded && !skipped && failure is null)
                message = "地图追踪文件未完整执行成功，不写入完成记录。";
            var result = new PuloniaTaskNodeResult(task.TaskAddress, task.Name, task.TaskType, attempt,
                status, message, startedAt, finished, resultData,
                routeRule is not null && !succeeded && !skipped ? PuloniaTaskOutcomeKind.Failed : outcome?.Kind, outcome?.Evidence,
                outcome?.ReasonCode);
            if (succeeded && routeRule is not null)
                await CommitRouteSuccessAsync(state, task, routeRule, result, nodeCancellation.Token).ConfigureAwait(false);
            else
                await AddNodeResultAsync(state, result).ConfigureAwait(false);
            if (succeeded || skipped)
                return ExecutionSignal.Continue;
            // 重试耗尽：按失败行为决定后续编排
            if (attempt >= maxAttempts)
                return MarkFinalFailure(state, task, message);

            var retryDelaySeconds = policy.RetryDelaySeconds ?? 0;
            if (retryDelaySeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds), runToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 续跑前确认快照引用的旧资源仍存在且内容指纹未变化。
    /// </summary>
    private static async Task ValidateSnapshotResourcesAsync(PuloniaTaskPreparedTask task, CancellationToken ct)
    {
        if (task.IsEnabled && task.Resource is { } resource && task.ResourceVersion is not null
            && task.TaskType is "pathing" or "keymouse" or "javascript")
        {
            // 仓库资源只能从原已确认版本的保留归档读取，不能改读当前来源；指纹算法与运行准备一致。
            var repositories = BetterGenshinImpact.Core.Script.Repositories.ScriptRepositoryStore.Shared;
            string actual;
            try
            {
                using var snapshot = await repositories.OpenApprovedAsync(resource, ct).ConfigureAwait(false);
                actual = await Task.Run(() => PuloniaTaskResourceFingerprint
                    .ComputeRepositoryVersion(snapshot, resource.RelativePath, task.TaskType), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                throw new PuloniaTaskValidationException(task.TaskAddress, "续跑所需的旧仓库资源已不可用：" + ex.Message, ex);
            }
            if (!string.Equals(actual, task.ResourceVersion, StringComparison.Ordinal))
                throw new PuloniaTaskValidationException(task.TaskAddress,
                    "资源指纹已经变化，不能假装按旧快照精确续跑；请按新版本重新运行。");
        }
        else if (task.IsEnabled && task.Path is not null && task.ResourceVersion is not null
            && task.TaskType is "pathing" or "keymouse" or "javascript")
        {
            var resourcePath = task.Path;
            string actual;
            if (task.TaskType == "javascript")
            {
                if (!Directory.Exists(resourcePath))
                    throw new PuloniaTaskValidationException(task.TaskAddress, "续跑所需的旧 JS 资源目录已不存在。");
                // 续跑与创建、准备、更新确认都只检查根目录 manifest.json 和 main.js，保留重解析点保护。
                actual = await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(resourcePath, ct)
                    .ConfigureAwait(false);
                // 不回退到旧全目录算法，也不改写旧快照；旧范围指纹不匹配时需要确认后新执行。
            }
            else
            {
                if (!File.Exists(resourcePath))
                    throw new PuloniaTaskValidationException(task.TaskAddress, "续跑所需的旧资源文件已不存在。");
                actual = await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(resourcePath, ct)
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
    private ExecutionSignal MarkFinalFailure(RunState state, PuloniaTaskPreparedTask task, string message)
    {
        lock (_runsGate)
        {
            state.FailureMessage = $"任务“{task.Name}”失败：{message}";
            state.Message = state.FailureMessage;
        }
        return task.Policy.FailureBehavior switch
        {
            "continue" => ExecutionSignal.Continue,
            "skip_group" => ExecutionSignal.SkipGroup,
            _ => ExecutionSignal.StopPlan
        };
    }

    /// <summary>
    /// 更新当前执行节点并通知观察者；只改内存，不为当前地址单独落盘（续跑与中断恢复只依赖快照与节点结果）。
    /// </summary>
    private void SetCurrentTask(RunState state, string address, string message)
    {
        lock (_runsGate)
        {
            state.CurrentTaskAddress = address;
            state.Message = message;
        }
        RaiseRunChanged(state.RunId);
    }

    /// <summary>
    /// 追加一次节点尝试结果并通知观察者。
    /// </summary>
    private async Task AddNodeResultAsync(RunState state, PuloniaTaskNodeResult result)
    {
        lock (_runsGate)
            state.NodeResults.Add(result);
        await PersistCriticalStateAsync().ConfigureAwait(false);
        RaiseRunChanged(state.RunId);
    }

    /// <summary>
    /// 结束运行：先作为待归档运行落盘，再写不可变历史，最后移出待归档并转为历史；完成等待者并发布最终状态。
    /// 任一步失败都熔断自动运行，运行保留为待处理。
    /// </summary>
    private async Task CompleteStateAsync(RunState state)
    {
        PuloniaTaskRunRecord record;
        lock (_runsGate)
            record = state.ToRecord();
        PuloniaTaskRunView view;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            // 1. 结束状态进入待归档；同一次替换中按运行集合移出排队请求或活动运行
            await PersistStateAsync(cleanup.Token, candidate =>
            {
                if (candidate.PendingArchives.All(item => item.RunId != record.RunId))
                    candidate.PendingArchives.Add(record);
            }).ConfigureAwait(false);

            // 2. 写不可变历史
            await _store.ArchiveRunAsync(record, cleanup.Token).ConfigureAwait(false);

            // 3. 历史成功后移出待归档，并在同一状态门内把运行转为历史
            await PersistStateAsync(cleanup.Token,
                candidate => candidate.PendingArchives.RemoveAll(item => item.RunId == record.RunId),
                committed: () =>
                {
                    lock (_runsGate)
                    {
                        _runs.Remove(record.RunId);
                        _history[record.RunId] = record.WithSnapshotJson(string.Empty);
                    }
                }).ConfigureAwait(false);
            // 等待者拿到的视图仍带内存中的快照，常驻历史只保留摘要。
            view = new PuloniaTaskRunView(record, isHistorical: true);
        }
        catch (Exception ex)
        {
            _runState.ReportFailure(ex);
            lock (_runsGate)
            {
                state.Status = PuloniaTaskRunStatus.NeedsAttention;
                state.Message = "运行已经结束，但状态或历史持久化失败：" + ex.Message;
                view = state.CreateView();
            }
        }
        state.Completion.TrySetResult(view);
        RaiseRunChanged(state.RunId);
    }

    /// <summary>
    /// 为常驻历史建立视图；快照在首次读取时从历史文件加载。
    /// </summary>
    private PuloniaTaskRunView CreateHistoryView(PuloniaTaskRunRecord record)
        => new(record, isHistorical: true, () => LoadHistorySnapshotJson(record.Request.PlanId, record.RunId));

    /// <summary>
    /// 按需读取一份历史的快照 JSON；文件已不可读时返回空字符串并记日志，页面按“快照不可用”展示。
    /// </summary>
    private string LoadHistorySnapshotJson(string planId, string runId)
    {
        try
        {
            return _store.ReadHistoryRecord(planId, runId)?.SnapshotJson ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取 Pulonia 运行 {RunId} 的历史快照失败", runId);
            return string.Empty;
        }
    }

    /// <summary>
    /// 深复制并校验调用方可填写的请求字段。
    /// </summary>
    /// <param name="request">调用方请求</param>
    /// <param name="serverUtcOffsetMinutes">替换服务器时区偏移；为空时沿用请求</param>
    private static PuloniaTaskRequest CloneAndValidateRequest(PuloniaTaskRequest request,
        int? serverUtcOffsetMinutes = null)
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
        if (request.TargetTaskId is not null)
            PuloniaTaskValidator.ValidateId(request.TargetTaskId, "request/target_task_id");
        if (!Enum.IsDefined(request.BusyPolicy))
            throw new PuloniaTaskValidationException("request", "忙碌策略无效。");
        if (request.TriggerId is not null)
        {
            PuloniaTaskValidator.ValidateId(request.TriggerId, "request/trigger_id");
            if (request.TriggerActivatedAtUtc is null
                || request.OccurrenceUtc is null || request.DeadlineUtc is null || request.DeadlineUtc <= request.OccurrenceUtc)
                throw new PuloniaTaskValidationException("request", "自动请求必须带有配置版本、发生时间和有效截止时间。");
        }
        var offsetMinutes = serverUtcOffsetMinutes ?? request.ServerUtcOffsetMinutes;
        if (offsetMinutes is < -12 * 60 or > 14 * 60)
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
            ServerUtcOffsetMinutes = offsetMinutes,
            TimeoutSeconds = request.TimeoutSeconds,
            ParameterOverrides = overrides,
            Source = request.Source,
            TargetTaskId = request.TargetTaskId,
            TriggerId = request.TriggerId,
            TriggerActivatedAtUtc = request.TriggerActivatedAtUtc,
            OccurrenceUtc = request.OccurrenceUtc,
            DeadlineUtc = request.DeadlineUtc,
            BusyPolicy = request.BusyPolicy,
            // 挤下线的确认只对本实例界面的手动运行有效；自动请求即使带上也忽略
            AllowDisplaceOnlineInstance = request.AllowDisplaceOnlineInstance && request.TriggerId is null && request.Source == "ui"
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
    private void RaiseRunChanged(string runId)
        => RunChanged?.Invoke(this, new PuloniaTaskRunChangedEventArgs(runId));

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;
        _shutdown.Cancel();
        RunState[] states;
        RunState[] running;
        lock (_runsGate)
        {
            states = _runs.Values.ToArray();
            running = states.Where(item => item.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling)
                .ToArray();
        }
        // 在锁外取消，令牌回调可能同步续接执行器的收尾代码。
        foreach (var state in running)
            state.Cancellation.Cancel();
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
        // 当前运行在消费循环内执行，循环退出即表示它已完成收尾和归档。

        // 初始化成功后才写回本实例的状态文件；初始化失败时保留原文件，不用空状态覆盖
        if (_initialization.IsCompletedSuccessfully)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await PersistStateAsync(cleanup.Token).ConfigureAwait(false);
            }
            catch
            {
                // 关闭阶段保留先前耐久版本；下次启动会按遗留活动运行进入核验。
            }
        }
        // 已转为历史的运行不再有令牌回调，这里只释放仍在运行集合中的令牌源。
        foreach (var state in states)
            state.Cancellation.Dispose();
        _shutdown.Dispose();
        _runState.Dispose();
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
}
