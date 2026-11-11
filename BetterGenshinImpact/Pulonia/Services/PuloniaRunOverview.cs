using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 执行记录的跨实例运行视图（统筹设计 6.5）：合并本实例内存中的运行（实时）、其他实例 <c>state.json</c> 中的排队、活动与待归档运行（只读），
/// 以及所有实例共用的历史目录；同一运行 ID 以历史为准。共享历史与各实例的运行状态按修改标记缓存，标记不变时复用上次读取的结果。
/// 不改变 <see cref="IPuloniaTaskService.ListRunsAsync"/> 只看本实例的语义；执行记录页、新一条龙页与主实例标题栏使用本类。
/// </summary>
/// <remarks>
/// 其他实例崩溃后留下的活动运行在它下次启动前仍按 state.json 显示；在线判断（online.lock）属于后续阶段。
/// </remarks>
public sealed class PuloniaRunOverview
{
    /// <summary>本实例的任务服务，提供本实例内存中的实时运行。</summary>
    private readonly IPuloniaTaskService _taskService;

    /// <summary>共享的 Pulonia 存储，只读读取其他实例的运行状态与共享历史。</summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>串行化缓存的读取与替换；多个页面可能同时刷新。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>上次读取共享历史时的修改标记；为空表示尚未读取。</summary>
    private string? _historyStamp;

    /// <summary>上次读取的共享历史视图；记录去掉了快照正文，快照在首次访问时从历史文件读取。</summary>
    private IReadOnlyList<PuloniaTaskRunView> _history = [];

    /// <summary>其他实例运行状态的缓存，键为实例目录名。</summary>
    private readonly Dictionary<string, CachedState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>本实例的实例键；它的运行状态不从文件读取。</summary>
    public string CurrentInstanceKey { get; }

    /// <summary>
    /// 建立跨实例运行视图；构造时不读取文件。
    /// </summary>
    /// <param name="taskService">本实例的任务服务</param>
    /// <param name="store">共享的 Pulonia 存储</param>
    /// <param name="currentInstanceKey">本实例的实例键</param>
    public PuloniaRunOverview(IPuloniaTaskService taskService, PuloniaTaskStore store, string currentInstanceKey)
    {
        PuloniaInstanceKey.Validate(currentInstanceKey);
        _taskService = taskService;
        _store = store;
        CurrentInstanceKey = currentInstanceKey;
    }

    /// <summary>
    /// 其他实例运行状态与共享历史的修改标记，只读取文件和目录元数据；与上次结果的
    /// <see cref="PuloniaRunOverviewResult.ChangeStamp"/> 不同时才需要再次读取。本实例的变化由 <see cref="IPuloniaTaskService.RunChanged"/> 通知，不计入标记。
    /// </summary>
    /// <returns>修改标记</returns>
    public string GetChangeStamp()
        => CombineStamp(_store.ListOtherInstanceStateFiles(CurrentInstanceKey), _store.GetHistoryStamp());

    /// <summary>
    /// 读取合并后的全部运行：本实例的运行取自内存（实时），其他实例的未归档运行取自它们的 state.json，已归档的取自共享历史。
    /// 标记不变的共享历史与运行状态复用缓存；读不出的运行状态文件跳过并在结果中说明。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>按提交时间倒序的运行、读不出的运行状态文件和本次所依据的修改标记</returns>
    public async Task<PuloniaRunOverviewResult> ListAsync(CancellationToken ct = default)
    {
        // 1. 本实例的运行：内存中的当前运行是实时状态
        var local = await _taskService.ListRunsAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // 2. 先取标记再读取：读取期间其他实例的写入会让下一次比较发现变化，不会被漏掉
            var stateFiles = _store.ListOtherInstanceStateFiles(CurrentInstanceKey);
            var historyStamp = _store.GetHistoryStamp();
            if (historyStamp != _historyStamp)
            {
                _history = await LoadHistoryAsync(ct).ConfigureAwait(false);
                _historyStamp = historyStamp;
            }

            // 3. 其他实例的未归档运行，逐个文件按标记复用
            var (others, failures) = await ReadOtherStatesAsync(stateFiles, ct).ConfigureAwait(false);
            return new PuloniaRunOverviewResult(Merge(local, others), failures, CombineStamp(stateFiles, historyStamp));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 其他实例正在执行（运行中或停止中）的运行，取自它们 state.json 的活动运行；键为执行实例键。主实例标题栏据此显示子实例正在运行的计划。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>执行实例键 → 活动运行</returns>
    public async Task<IReadOnlyDictionary<string, PuloniaTaskRunView>> ListOtherActiveRunsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (others, _) = await ReadOtherStatesAsync(_store.ListOtherInstanceStateFiles(CurrentInstanceKey), ct)
                .ConfigureAwait(false);
            var active = new Dictionary<string, PuloniaTaskRunView>(StringComparer.Ordinal);
            foreach (var run in others.Where(run => run.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling))
                active.TryAdd(run.Instance, run);
            return active;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 其他实例 state.json 中的定时触发游标，键为实例键（统筹设计 9.5）：触发设置页据此显示其他实例负责的部分“已处理到 04:30”。
    /// 与运行共用按修改标记的缓存；读不出的文件没有游标。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>实例键 → 游标</returns>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<PuloniaTaskTriggerState>>> ListOtherTriggerCursorsAsync(
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ReadOtherStatesAsync(_store.ListOtherInstanceStateFiles(CurrentInstanceKey), ct).ConfigureAwait(false);
            var cursors = new Dictionary<string, IReadOnlyList<PuloniaTaskTriggerState>>(StringComparer.Ordinal);
            foreach (var (directoryName, cached) in _states)
            {
                if (PuloniaInstanceKey.TryFromDirectoryName(directoryName) is { } instanceKey)
                    cursors[instanceKey] = cached.Cursors;
            }
            return cursors;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 合并三部分运行：共享历史优先（运行一旦归档，历史就是最终结果）；本实例的运行补充历史中还没有的（含刚归档、尚未重读的）；
    /// 最后是其他实例的未归档运行。
    /// </summary>
    /// <param name="local">本实例的运行（内存中的当前运行与本实例已知的历史）</param>
    /// <param name="others">其他实例 state.json 中的运行</param>
    /// <returns>按提交时间倒序、运行 ID 不重复的运行</returns>
    private IReadOnlyList<PuloniaTaskRunView> Merge(IReadOnlyList<PuloniaTaskRunView> local, IReadOnlyList<PuloniaTaskRunView> others)
    {
        var runs = new Dictionary<string, PuloniaTaskRunView>(StringComparer.Ordinal);
        foreach (var run in _history)
            runs[run.RunId] = run;
        foreach (var run in local)
            runs.TryAdd(run.RunId, run);
        foreach (var run in others)
            runs.TryAdd(run.RunId, run);
        return runs.Values.OrderByDescending(run => run.SubmittedAt).ToArray();
    }

    /// <summary>
    /// 重新读取共享历史；坏文件由存储跳过（启动时的恢复提示已说明），常驻的记录去掉快照正文，快照按需读取。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>历史视图</returns>
    private async Task<IReadOnlyList<PuloniaTaskRunView>> LoadHistoryAsync(CancellationToken ct)
    {
        var history = await _store.ListHistoryWithFailuresAsync(ct).ConfigureAwait(false);
        return history.Items.Select(record =>
        {
            var planId = record.Request.PlanId;
            var runId = record.RunId;
            return new PuloniaTaskRunView(record.WithSnapshotJson(string.Empty), isHistorical: true,
                () => LoadHistorySnapshotJson(planId, runId));
        }).ToArray();
    }

    /// <summary>
    /// 按需读取一份历史的快照 JSON；文件已不可读时返回空字符串，页面按“快照不可用”展示。
    /// </summary>
    /// <param name="planId">计划 ID</param>
    /// <param name="runId">运行 ID</param>
    private string LoadHistorySnapshotJson(string planId, string runId)
    {
        try
        {
            return _store.ReadHistoryRecord(planId, runId)?.SnapshotJson ?? string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return string.Empty;
        }
    }

    /// <summary>
    /// 读取其他实例的运行状态：标记与缓存相同的文件直接复用，变化的文件只读重读；读不出的文件按同一标记记住失败，文件变化后再试。
    /// 已不存在的实例目录从缓存中移除。调用方必须持有 <see cref="_gate"/>。
    /// </summary>
    /// <param name="files">其他实例的运行状态文件</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>其他实例的运行与读不出的文件</returns>
    private async Task<(IReadOnlyList<PuloniaTaskRunView> Runs, IReadOnlyList<PuloniaTaskFileLoadFailure> Failures)>
        ReadOtherStatesAsync(IReadOnlyList<PuloniaInstanceStateFile> files, CancellationToken ct)
    {
        var runs = new List<PuloniaTaskRunView>();
        var failures = new List<PuloniaTaskFileLoadFailure>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            seen.Add(file.DirectoryName);
            if (!_states.TryGetValue(file.DirectoryName, out var cached) || cached.Stamp != file.Stamp)
            {
                cached = await ReadStateAsync(file, ct).ConfigureAwait(false);
                _states[file.DirectoryName] = cached;
            }
            runs.AddRange(cached.Runs);
            if (cached.Failure is { } failure)
                failures.Add(failure);
        }
        foreach (var removed in _states.Keys.Where(key => !seen.Contains(key)).ToArray())
            _states.Remove(removed);
        return (runs, failures);
    }

    /// <summary>
    /// 只读读取一个实例的运行状态，转为不可变视图：排队请求、活动运行与待归档运行。
    /// </summary>
    /// <param name="file">运行状态文件</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>缓存项；读不出时只记录失败原因</returns>
    private static async Task<CachedState> ReadStateAsync(PuloniaInstanceStateFile file, CancellationToken ct)
    {
        try
        {
            var state = await PuloniaTaskStore.ReadInstanceStateAsync(file.Path, ct).ConfigureAwait(false);
            if (state is null)
                return new CachedState(file.Stamp, [], [], null);
            var records = new List<PuloniaTaskRunRecord>(state.PendingRequests);
            if (state.ActiveRun is { } active)
                records.Add(active);
            records.AddRange(state.PendingArchives);
            // 记录读出后不再修改，视图直接包装；快照正文随 state.json 一起读出
            return new CachedState(file.Stamp,
                records.Select(record => new PuloniaTaskRunView(record, isHistorical: false)).ToArray(),
                state.TriggerStates.Select(cursor => cursor.Clone()).ToArray(), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CachedState(file.Stamp, [], [],
                new PuloniaTaskFileLoadFailure(file.Path, null, ex.Message));
        }
    }

    /// <summary>
    /// 组合修改标记：各实例运行状态文件的目录名与标记，加上共享历史标记。
    /// </summary>
    /// <param name="files">其他实例的运行状态文件</param>
    /// <param name="historyStamp">共享历史标记</param>
    private static string CombineStamp(IEnumerable<PuloniaInstanceStateFile> files, string historyStamp)
        => string.Join("|", files.Select(file => file.DirectoryName + "=" + file.Stamp)) + "#" + historyStamp;

    /// <summary>
    /// 一个实例运行状态的缓存项。
    /// </summary>
    /// <param name="Stamp">读取时的文件修改标记</param>
    /// <param name="Runs">文件中的运行</param>
    /// <param name="Cursors">文件中的定时触发游标（该实例负责的定时触发器处理到哪个时刻）</param>
    /// <param name="Failure">读不出时的原因；读取成功时为空</param>
    private sealed record CachedState(string Stamp, IReadOnlyList<PuloniaTaskRunView> Runs,
        IReadOnlyList<PuloniaTaskTriggerState> Cursors, PuloniaTaskFileLoadFailure? Failure);
}
