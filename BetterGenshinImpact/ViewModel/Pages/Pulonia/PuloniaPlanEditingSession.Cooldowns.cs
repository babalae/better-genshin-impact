using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 一份计划会用到的路线 CD 记录范围：直接添加的路线文件资源键，以及目录引用的位置；含引用计划中的路线。
/// </summary>
/// <param name="EffectKeys">路线文件的资源键。</param>
/// <param name="Directories">目录引用在账本中的位置。</param>
internal sealed record PuloniaCooldownFileSet(IReadOnlySet<string> EffectKeys, IReadOnlyList<PuloniaCooldownDirectory> Directories)
{
    /// <summary>资源键是否属于这份计划。</summary>
    public bool Contains(string effectKey)
        => EffectKeys.Contains(effectKey) || Directories.Any(directory =>
            effectKey.StartsWith(directory.EffectKeyPrefix, StringComparison.Ordinal)
            && (directory.Recursive || effectKey.IndexOf('/', directory.EffectKeyPrefix.Length) < 0));
}

/// <summary>
/// 任务计划页的路线 CD 标记：按账本与节点当前规则计算任务树标记与计划摘要，提供清除记录入口。
/// 判定统一调用 <see cref="PuloniaCollectionCooldown.GetStatus"/>；运行时仍在每个节点重新判定。
/// </summary>
public partial class PuloniaPlanEditingSession
{
    /// <summary>规则变化后重新计算标记前的防抖时间。</summary>
    private const int CooldownRefreshDelayMilliseconds = 500;

    /// <summary>最近一次读取的账本索引。</summary>
    private PuloniaCollectionCooldownIndex _cooldownIndex = PuloniaCollectionCooldownIndex.Empty;

    /// <summary>账号资料 ID → UID，用于计算一条龙绑定账号的作用域。</summary>
    private IReadOnlyDictionary<string, string> _accountUidsById = new Dictionary<string, string>();

    /// <summary>UID → 账号资料名称，用于显示作用域。</summary>
    private IReadOnlyDictionary<string, string> _accountNamesByUid = new Dictionary<string, string>();

    /// <summary>是否已订阅账本变化；会话是单例，订阅一次即可。</summary>
    private bool _isLedgerSubscribed;

    /// <summary>当前可见并需要路线 CD 标记的页面；全部离开后停止定时刷新。</summary>
    private readonly HashSet<object> _cooldownMonitorOwners = [];

    /// <summary>定时刷新的取消源。</summary>
    private CancellationTokenSource? _cooldownMonitorCancellation;

    /// <summary>规则变化后的防抖刷新取消源。</summary>
    private CancellationTokenSource? _cooldownRefreshDelay;

    /// <summary>最近一次读取的账本索引，供 CD 总览与预览窗口复用。</summary>
    internal PuloniaCollectionCooldownIndex CooldownIndex => _cooldownIndex;

    /// <summary>UID → 账号资料名称。</summary>
    internal IReadOnlyDictionary<string, string> AccountNamesByUid => _accountNamesByUid;

    /// <summary>账本重新读取后触发（在 UI 线程上）。</summary>
    internal event EventHandler? CooldownsChanged;

    /// <summary>
    /// 页面可见时登记：订阅账本变化，编辑后防抖重算标记，并每分钟刷新一次剩余时间；首次读取由页面在计划加载后调用
    /// <see cref="ReloadCooldownLedgerAsync"/>。
    /// </summary>
    /// <param name="owner">登记的页面视图模型。</param>
    internal void StartCooldownMonitor(object owner)
    {
        _cooldownMonitorOwners.Add(owner);
        EnsureLedgerSubscription();
        if (_cooldownMonitorCancellation is not null || Application.Current?.Dispatcher is not { } dispatcher)
            return;
        _cooldownMonitorCancellation = new CancellationTokenSource();
        _ = MonitorCooldownsAsync(dispatcher, _cooldownMonitorCancellation.Token);
    }

    /// <summary>页面离开时注销；最后一个页面离开后停止定时刷新。</summary>
    /// <param name="owner">之前登记的页面视图模型。</param>
    internal void StopCooldownMonitor(object owner)
    {
        if (!_cooldownMonitorOwners.Remove(owner) || _cooldownMonitorOwners.Count > 0
            || _cooldownMonitorCancellation is not { } cancellation)
            return;
        cancellation.Cancel();
        cancellation.Dispose();
        _cooldownMonitorCancellation = null;
    }

    /// <summary>订阅账本变化；新成功记录或清除记录后重新读取。</summary>
    internal void EnsureLedgerSubscription()
    {
        if (_isLedgerSubscribed) return;
        TaskService.LedgerChanged += OnLedgerChanged;
        _isLedgerSubscribed = true;
    }

    /// <summary>重新读取账本与账号目录，然后刷新标记并通知 CD 总览；读取失败时保留上一次结果。</summary>
    internal async Task ReloadCooldownLedgerAsync()
    {
        try
        {
            var ledger = await TaskService.ListLedgerAsync();
            _cooldownIndex = new PuloniaCollectionCooldownIndex(ledger);
            var accounts = await _store.LoadAccountSettingsAsync();
            _accountUidsById = accounts.Accounts.Where(account => !account.IsDefault && !string.IsNullOrWhiteSpace(account.Uid))
                .ToDictionary(account => account.Id, account => account.Uid!, StringComparer.Ordinal);
            _accountNamesByUid = accounts.Accounts.Where(account => !account.IsDefault && !string.IsNullOrWhiteSpace(account.Uid))
                .GroupBy(account => account.Uid!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        RefreshCooldownBadges();
        CooldownsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>计划内容变化后安排一次防抖刷新；没有页面显示标记时不计算。</summary>
    private void ScheduleCooldownRefresh()
    {
        if (_cooldownMonitorOwners.Count == 0) return;
        _cooldownRefreshDelay?.Cancel();
        var cancellation = _cooldownRefreshDelay = new CancellationTokenSource();
        _ = RefreshAfterDelayAsync(cancellation);

        async Task RefreshAfterDelayAsync(CancellationTokenSource source)
        {
            try
            {
                await Task.Delay(CooldownRefreshDelayMilliseconds, source.Token);
                RefreshCooldownBadges();
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                // 新的编辑已重新安排刷新。
            }
            finally
            {
                if (ReferenceEquals(_cooldownRefreshDelay, source)) _cooldownRefreshDelay = null;
                source.Dispose();
            }
        }
    }

    /// <summary>
    /// 计算普通计划任务树的路线 CD 标记与计划摘要：叶子按当前规则判定，分组和计划引用汇总启用的下属路线，
    /// 目录引用只计入账本中仍在 CD 的记录。
    /// </summary>
    internal void RefreshCooldownBadges()
    {
        var now = DateTimeOffset.UtcNow;
        var offset = PuloniaServerTime.GetUtcOffsetMinutes();
        var documents = Documents.ToArray();
        var summaries = new Dictionary<string, PuloniaCooldownSummary>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
            SummarizeDocument(document);

        // 计划汇总带缓存与循环保护：同一计划被多处引用时只计算一次，草稿中的循环引用按空汇总处理。
        PuloniaCooldownSummary SummarizeDocument(PuloniaTaskPlanDocumentViewModel document)
        {
            if (summaries.TryGetValue(document.Id, out var cached)) return cached;
            if (!visiting.Add(document.Id)) return PuloniaCooldownSummary.Empty;
            var scope = GetCooldownScope(document);
            var summary = document.RootNode.Children.Aggregate(PuloniaCooldownSummary.Empty,
                (total, child) => total + SummarizeNode(document, child, scope));
            visiting.Remove(document.Id);
            summaries[document.Id] = summary;
            var (text, toolTip) = DescribeDocumentSummary(summary);
            document.SetCooldownSummary(text, toolTip);
            return summary;
        }

        PuloniaCooldownSummary SummarizeNode(PuloniaTaskPlanDocumentViewModel document, PuloniaTaskNodeViewModel node, string scope)
        {
            // 1. 地图追踪叶子：按节点当前规则判定；停用的叶子仍显示事实，但不计入上层汇总
            if (node.IsLeaf)
            {
                if (document.ResolveCooldownPolicy(node) is not { } policy)
                {
                    node.SetCooldownBadge(null, false, null);
                    return PuloniaCooldownSummary.Empty;
                }
                var status = PuloniaCollectionCooldown.GetStatus(policy,
                    _cooldownIndex.Find(scope, PuloniaCollectionCooldown.GetEffectKey(policy)), now, offset);
                var (text, caution, toolTip) = DescribeLeaf(policy, status, scope, now);
                node.SetCooldownBadge(text, caution, toolTip);
                return node.IsEffectivelyEnabled
                    ? new PuloniaCooldownSummary(1, status.IsSkipped ? 1 : 0,
                        status.State == PuloniaCollectionCooldownState.Unmatched ? 1 : 0,
                        status.IsSkipped ? status.NextEligibleAt : null, false)
                    : PuloniaCooldownSummary.Empty;
            }
            // 2. 计划引用：沿用被引用计划自身的汇总（不含引用节点外层的设置覆盖，逐项结果见运行预览）
            PuloniaCooldownSummary summary;
            if (node.Model.Source is { Kind: "plan", PlanId: { } planId })
                summary = documents.FirstOrDefault(item => item.Id == planId) is { } target
                    ? SummarizeDocument(target) : PuloniaCooldownSummary.Empty;
            // 3. 目录引用：不展开目录，只按该组规则判定账本中目录下的记录
            else if (document.GetCooldownDirectory(node) is { } directory)
                summary = SummarizeDirectory(directory, scope, now, offset);
            // 4. 普通分组：汇总子节点
            else
                summary = node.Children.Aggregate(PuloniaCooldownSummary.Empty,
                    (total, child) => total + SummarizeNode(document, child, scope));
            var group = DescribeGroup(summary);
            node.SetCooldownBadge(group.Text, group.Caution, group.ToolTip);
            return node.IsEffectivelyEnabled ? summary : PuloniaCooldownSummary.Empty;
        }
    }

    /// <summary>
    /// 一份计划的作用域：普通计划与一条龙默认账号为当前登录账号；一条龙绑定指定账号时按账号资料的 UID。
    /// </summary>
    internal string GetCooldownScope(PuloniaTaskPlanDocumentViewModel document)
    {
        if (document.Purpose != PuloniaTaskPlanPurpose.OneDragon)
            return PuloniaCollectionCooldown.BuildScopeKey();
        try
        {
            var accountId = PuloniaTaskService.ResolveOneDragonAccountId(document.Plan);
            return _accountUidsById.TryGetValue(accountId, out var uid)
                ? PuloniaCollectionCooldown.BuildScopeKey(uid) : PuloniaCollectionCooldown.BuildScopeKey();
        }
        catch (InvalidOperationException)
        {
            // 多个账号绑定由运行准备报错，这里按当前登录账号显示。
            return PuloniaCollectionCooldown.BuildScopeKey();
        }
    }

    /// <summary>
    /// 一份计划会用到的路线文件资源键与目录引用位置，含引用计划；供 CD 总览按计划筛选。
    /// </summary>
    internal PuloniaCooldownFileSet GetCooldownFileSet(PuloniaTaskPlanDocumentViewModel document)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var directories = new List<PuloniaCooldownDirectory>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Collect(document);
        return new PuloniaCooldownFileSet(keys, directories);

        void Collect(PuloniaTaskPlanDocumentViewModel current)
        {
            if (!visited.Add(current.Id)) return;
            foreach (var node in current.EnumerateNodes())
            {
                if (current.ResolveCooldownPolicy(node) is { } policy)
                    keys.Add(PuloniaCollectionCooldown.GetEffectKey(policy));
                else if (current.GetCooldownDirectory(node) is { } directory)
                    directories.Add(directory);
                else if (node.Model.Source is { Kind: "plan", PlanId: { } planId }
                         && AllDocuments.FirstOrDefault(item => item.Id == planId) is { } target)
                    Collect(target);
            }
        }
    }

    /// <summary>
    /// 给路线 CD 表单附上本账号的状态、清除入口与跨计划规则差异；只处理地图追踪叶子。
    /// </summary>
    /// <param name="draft">表单草稿。</param>
    /// <param name="node">被编辑的叶子。</param>
    internal void AttachCooldownStatus(PuloniaCollectionCooldownViewModel draft, PuloniaTaskNodeViewModel node)
    {
        if (!node.IsLeaf || node.Document.ResolveCooldownPolicy(node) is not { } policy)
            return;
        var scope = GetCooldownScope(node.Document);
        var key = new PuloniaCollectionCooldownKey(scope, PuloniaCollectionCooldown.GetEffectKey(policy));
        draft.SetStatus(DescribeStatus(), _cooldownIndex.Find(key.ScopeKey, key.EffectKey) is null ? null : ClearAsync);
        draft.SetConflict(FindRuleConflict(node, policy));

        string DescribeStatus()
        {
            var status = PuloniaCollectionCooldown.GetStatus(policy, _cooldownIndex.Find(key.ScopeKey, key.EffectKey),
                DateTimeOffset.UtcNow, PuloniaServerTime.GetUtcOffsetMinutes());
            var lines = new List<string>
            {
                $"{PuloniaCooldownText.Scope(scope, _accountNamesByUid)}：{PuloniaCooldownText.State(status.State)}",
                status.Entry is null ? "上次成功：没有记录" : "上次成功：" + PuloniaCooldownText.Time(status.LastSuccessAt)
            };
            if (status.NextEligibleAt is { } next)
                lines.Add("下次可执行：" + PuloniaCooldownText.Time(next));
            return string.Join("\n", lines);
        }

        async Task<(string Status, bool HasRecord)> ClearAsync()
        {
            await ClearCooldownRecordsAsync([key], $"“{node.Name}”");
            return (DescribeStatus(), _cooldownIndex.Find(key.ScopeKey, key.EffectKey) is not null);
        }
    }

    /// <summary>确认后清除指定的路线 CD 记录，并重新读取账本；返回删除的条目数。</summary>
    /// <param name="keys">要清除的记录。</param>
    /// <param name="description">确认框中对清除对象的描述。</param>
    internal async Task<int> ClearCooldownRecordsAsync(IReadOnlyCollection<PuloniaCollectionCooldownKey> keys, string description)
    {
        if (keys.Count == 0) return 0;
        var answer = await ThemedMessageBox.ShowAsync(
            $"清除{description}的路线 CD 记录（共 {keys.Count} 条）？\n\n清除后下次运行会执行这些路线；运行历史中的成功记录不受影响；此操作不能撤销。",
            "清除路线 CD 记录", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return 0;
        try
        {
            var removed = await TaskService.ClearCollectionCooldownAsync(keys);
            await ReloadCooldownLedgerAsync();
            ReportStatus($"已清除 {removed} 条路线 CD 记录，这些路线下次运行会照常执行。");
            return removed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            var message = "清除路线 CD 记录失败：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法清除路线 CD 记录");
            return 0;
        }
    }

    /// <summary>账本变化来自协调器线程，切换到 UI 线程后重新读取。</summary>
    private void OnLedgerChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        _ = dispatcher.InvokeAsync(ReloadCooldownLedgerAsync);
    }

    /// <summary>页面可见期间每分钟刷新剩余时间与到期状态，退出后不再访问 UI。</summary>
    private async Task MonitorCooldownsAsync(Dispatcher dispatcher, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                await dispatcher.InvokeAsync(RefreshCooldownBadges, DispatcherPriority.Background, ct).Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || dispatcher.HasShutdownStarted) { }
        catch (Exception ex)
        {
            // 刷新失败不得成为未处理异常；下次进入页面会重新计算。
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>目录引用：按该组规则判定账本中目录下的记录，只统计会跳过的条目。</summary>
    private PuloniaCooldownSummary SummarizeDirectory(PuloniaCooldownDirectory directory, string scope, DateTimeOffset now, int offset)
    {
        var skipped = 0;
        DateTimeOffset? earliest = null;
        foreach (var entry in _cooldownIndex.EnumerateUnder(scope, directory.EffectKeyPrefix, directory.Recursive))
        {
            if (!PuloniaCollectionCooldown.TryGetFileIdentity(entry.EffectKey, out var identity)) continue;
            PuloniaCollectionCooldownStatus status;
            try
            {
                status = PuloniaCollectionCooldown.GetStatus(
                    PuloniaCollectionCooldown.ResolveFromFileIdentity(identity, directory.Setting), entry, now, offset);
            }
            catch (PuloniaTaskValidationException)
            {
                // 设置无效时运行准备会报错，这里不计数。
                continue;
            }
            if (!status.IsSkipped) continue;
            skipped++;
            if (status.NextEligibleAt is { } next && (earliest is null || next < earliest)) earliest = next;
        }
        return new PuloniaCooldownSummary(0, skipped, 0, earliest, true);
    }

    /// <summary>叶子标记：CD 中显示剩余时间，一次性显示已完成，未识别用提示色；可执行与不检查不显示。</summary>
    private (string? Text, bool Caution, string? ToolTip) DescribeLeaf(PuloniaCollectionCooldownPolicy policy,
        PuloniaCollectionCooldownStatus status, string scope, DateTimeOffset now)
    {
        var text = status.State switch
        {
            PuloniaCollectionCooldownState.CoolingDown when status.NextEligibleAt is { } next => "CD 中 · " + PuloniaCooldownText.Remaining(next, now),
            PuloniaCollectionCooldownState.Completed => "已完成",
            PuloniaCollectionCooldownState.Unmatched => "未识别 CD",
            _ => null
        };
        if (text is null) return (null, false, null);
        var lines = new List<string>
        {
            $"路线 CD（{PuloniaCooldownText.Scope(scope, _accountNamesByUid)}）：{PuloniaCooldownText.State(status.State)}",
            $"规则：{policy.MaterialName ?? "未识别"} · {policy.Description}"
        };
        if (status.LastSuccessAt is { } last) lines.Add("上次成功：" + PuloniaCooldownText.Time(last));
        if (status.NextEligibleAt is { } eligible) lines.Add("下次可执行：" + PuloniaCooldownText.Time(eligible));
        if (status.State == PuloniaCollectionCooldownState.Unmatched)
            lines.Add("路径中没有已知的材料或类别目录，成功后不会跳过；可在“共享设置 → 路线 CD”中选择自定义周期。");
        else
            lines.Add("本次运行会跳过；可在“共享设置 → 路线 CD”中清除记录。");
        return (text, status.State == PuloniaCollectionCooldownState.Unmatched, string.Join("\n", lines));
    }

    /// <summary>分组、目录引用与计划引用的汇总标记。</summary>
    private static (string? Text, bool Caution, string? ToolTip) DescribeGroup(PuloniaCooldownSummary summary)
    {
        var text = summary.Skipped > 0
            ? summary.HasDirectory ? $"{summary.Skipped} 项 CD 中" : $"{summary.Skipped}/{summary.Total} CD 中"
            : summary.Unmatched > 0 ? $"{summary.Unmatched} 项未识别 CD" : null;
        if (text is null) return (null, false, null);
        var lines = new List<string>
        {
            $"下属启用路线：{(summary.HasDirectory ? "已知 " : "")}{summary.Total} 条，本次会跳过 {summary.Skipped} 条（CD 中或一次性已完成），"
            + $"未识别刷新规则 {summary.Unmatched} 条"
        };
        if (summary.EarliestNext is { } next) lines.Add("最早可执行：" + PuloniaCooldownText.Time(next));
        if (summary.HasDirectory) lines.Add("目录引用不展开，只计入仍在 CD 的记录；逐项结果请使用“预览本次执行”。");
        return (text, summary.Skipped == 0, string.Join("\n", lines));
    }

    /// <summary>任务页签顶部的计划摘要；没有地图追踪路线时隐藏。</summary>
    private static (string? Text, string? ToolTip) DescribeDocumentSummary(PuloniaCooldownSummary summary)
    {
        if (summary.Total == 0 && !summary.HasDirectory) return (null, null);
        var parts = new List<string>();
        if (summary.Skipped > 0) parts.Add($"本次 {summary.Skipped} 项会因 CD 跳过");
        if (summary.Unmatched > 0) parts.Add($"{summary.Unmatched} 项未识别刷新规则");
        var text = "路线 CD：" + (parts.Count == 0 ? "启用的路线均可执行" : string.Join(" · ", parts));
        var toolTip = "按当前登录账号与各节点当前规则计算；运行到每个节点时会重新判定。"
                      + (summary.EarliestNext is { } next ? "\n最早可执行：" + PuloniaCooldownText.Time(next) : string.Empty)
                      + (summary.HasDirectory ? "\n目录引用只计入仍在 CD 的记录；逐项结果请使用“预览本次执行”。" : string.Empty);
        return (text, toolTip);
    }

    /// <summary>同一文件在其他节点使用不同规则时返回提示；判定以运行时各节点自己的规则为准。</summary>
    private string? FindRuleConflict(PuloniaTaskNodeViewModel node, PuloniaCollectionCooldownPolicy policy)
    {
        var key = PuloniaCollectionCooldown.GetEffectKey(policy);
        foreach (var document in AllDocuments)
        foreach (var other in document.EnumerateNodes())
        {
            if (ReferenceEquals(other, node) || document.ResolveCooldownPolicy(other) is not { } otherPolicy
                || PuloniaCollectionCooldown.GetEffectKey(otherPolicy) != key || otherPolicy.Description == policy.Description)
                continue;
            return $"同一路线在“{document.Name} / {other.Name}”中使用不同规则（{otherPolicy.Description}）。"
                   + "两处共用同一条成功记录，各自按自己的规则判定。";
        }
        return null;
    }
}
