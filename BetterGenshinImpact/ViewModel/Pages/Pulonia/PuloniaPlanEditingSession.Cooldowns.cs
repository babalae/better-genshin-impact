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
/// 一份计划会用到的路线范围：直接添加的路线身份，以及目录引用的位置；含引用计划中的路线。
/// </summary>
/// <param name="Routes">路线身份。</param>
/// <param name="Directories">目录引用对应的路线身份前缀。</param>
internal sealed record PuloniaCooldownFileSet(IReadOnlySet<string> Routes, IReadOnlyList<PuloniaCooldownDirectory> Directories)
{
    /// <summary>路线是否属于这份计划。</summary>
    public bool Contains(string route)
        => Routes.Contains(route) || Directories.Any(directory =>
            route.StartsWith(directory.RoutePrefix, StringComparison.Ordinal)
            && (directory.Recursive || route.IndexOf('/', directory.RoutePrefix.Length) < 0));
}

/// <summary>
/// 一条可清除的完成记录的身份：账户与路线。每个账户的每份路线只有一条完成记录。
/// </summary>
/// <param name="AccountId">账户 ID。</param>
/// <param name="Route">路线身份。</param>
public sealed record PuloniaCooldownRecordKey(string AccountId, string Route);

/// <summary>
/// 界面显示路线 CD 时选用的账户：名称、日切使用的服务器偏移与该账户的完成记录。
/// </summary>
/// <param name="Id">账户 ID。</param>
/// <param name="Name">账户名称。</param>
/// <param name="ServerUtcOffsetMinutes">账户服务器时区的分钟偏移。</param>
/// <param name="Completions">该账户的完成记录。</param>
internal sealed record PuloniaCooldownAccount(string Id, string Name, int ServerUtcOffsetMinutes, PuloniaRouteCompletionIndex Completions);

/// <summary>
/// 任务计划页的路线 CD 标记：按计划所选账户的完成记录与节点当前规则计算任务树标记与计划摘要，提供清除记录入口。
/// 账户选择（统筹设计 5.6）：计划指定账户时为该账户，跟随当前账户时为本实例当前账户；日切时区取该账户的服务器时区。
/// 判定统一调用 <see cref="PuloniaRouteCooldown.GetStatus"/>；运行时仍在每个节点按运行账户重新判定。
/// </summary>
public partial class PuloniaPlanEditingSession
{
    /// <summary>规则变化后重新计算标记前的防抖时间。</summary>
    private const int CooldownRefreshDelayMilliseconds = 500;

    /// <summary>最近一次读取的账户目录，顺序与账户列表一致。</summary>
    private IReadOnlyList<PuloniaGenshinAccountProfile> _cooldownAccounts = [];

    /// <summary>最近一次读取的本实例当前账户 ID；“未确定”时为空。</summary>
    private string? _currentAccountId;

    /// <summary>账户 ID → 最近一次读取的完成记录。</summary>
    private IReadOnlyDictionary<string, PuloniaRouteCompletionIndex> _completions =
        new Dictionary<string, PuloniaRouteCompletionIndex>(StringComparer.Ordinal);

    /// <summary>界面临时选择查看的账户；为空时按各计划的运行账户设置选择。</summary>
    private string? _cooldownViewAccountId;

    /// <summary>是否已订阅账户与账户进度变化；会话是单例，订阅一次即可。</summary>
    private bool _isCooldownSubscribed;

    /// <summary>当前可见并需要路线 CD 标记的页面；全部离开后停止定时刷新。</summary>
    private readonly HashSet<object> _cooldownMonitorOwners = [];

    /// <summary>定时刷新的取消源。</summary>
    private CancellationTokenSource? _cooldownMonitorCancellation;

    /// <summary>规则变化后的防抖刷新取消源。</summary>
    private CancellationTokenSource? _cooldownRefreshDelay;

    /// <summary>最近一次读取的账户目录与各账户的完成记录，供 CD 总览复用。</summary>
    internal IReadOnlyList<(PuloniaGenshinAccountProfile Account, PuloniaRouteCompletionIndex Completions)> CooldownAccounts
        => _cooldownAccounts.Select(account => (account, _completions.GetValueOrDefault(account.Id) ?? PuloniaRouteCompletionIndex.Empty))
            .ToArray();

    /// <summary>最近一次读取的账户目录（不含完成记录），供“CD 按账户”选择器列出选项；尚未读取时为空。</summary>
    internal IReadOnlyList<PuloniaGenshinAccountProfile> CooldownAccountProfiles => _cooldownAccounts;

    /// <summary>界面临时选择查看的账户 ID；为空表示按各计划的运行账户设置。</summary>
    internal string? CooldownViewAccountId => _cooldownViewAccountId;

    /// <summary>完成记录或账户重新读取后触发（在 UI 线程上）。</summary>
    internal event EventHandler? CooldownsChanged;

    /// <summary>
    /// 页面可见时登记：订阅账户进度变化，编辑后防抖重算标记，并每分钟比较一次完成记录的修改时间（其他实例的写入）
    /// 并刷新剩余时间；首次读取由页面在计划加载后调用 <see cref="ReloadCooldownsAsync"/>。
    /// </summary>
    /// <param name="owner">登记的页面视图模型。</param>
    internal void StartCooldownMonitor(object owner)
    {
        _cooldownMonitorOwners.Add(owner);
        EnsureCooldownSubscription();
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

    /// <summary>订阅账户进度、账户目录与当前账户的变化；本进程写入或清除记录、修改账户后重新读取。</summary>
    internal void EnsureCooldownSubscription()
    {
        if (_isCooldownSubscribed) return;
        AccountService.AccountProgressChanged += OnAccountProgressChanged;
        AccountService.AccountsChanged += OnAccountsChanged;
        AccountService.CurrentAccountChanged += OnCurrentAccountChanged;
        _isCooldownSubscribed = true;
    }

    /// <summary>
    /// 界面临时选择查看路线 CD 的账户（为空时按各计划的运行账户设置），重新计算标记；不修改计划和实例设置。
    /// </summary>
    /// <param name="accountId">要查看的账户 ID；为空恢复按计划设置。</param>
    internal void SetCooldownViewAccount(string? accountId)
    {
        if (_cooldownViewAccountId == accountId) return;
        _cooldownViewAccountId = accountId;
        RefreshCooldownBadges();
        CooldownsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 重新读取账户目录、本实例当前账户与各账户的完成记录（按文件修改时间缓存，未变化时不重读），然后刷新标记并通知 CD 总览；
    /// 读取失败时保留上一次结果。
    /// </summary>
    internal async Task ReloadCooldownsAsync()
    {
        try
        {
            var settings = await AccountService.LoadAccountsAsync();
            var currentAccountId = await AccountService.GetCurrentAccountIdAsync(AccountService.CurrentInstanceKey);
            var completions = new Dictionary<string, PuloniaRouteCompletionIndex>(StringComparer.Ordinal);
            foreach (var account in settings.Accounts)
            {
                try
                {
                    completions[account.Id] = await AccountService.GetRouteCompletionIndexAsync(account.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 一个账户的完成记录读不出时不影响其他账户，保留上一次读到的结果
                    System.Diagnostics.Debug.WriteLine(ex);
                    completions[account.Id] = _completions.GetValueOrDefault(account.Id) ?? PuloniaRouteCompletionIndex.Empty;
                }
            }
            _cooldownAccounts = settings.Accounts;
            _currentAccountId = currentAccountId;
            _completions = completions;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        RefreshCooldownBadges();
        CooldownsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 一份计划显示路线 CD 的账户：界面临时选择的账户；否则计划指定账户时为该账户，跟随当前账户时为本实例当前账户；
    /// 都不可用（未确定或已删除）时为账户列表第一个。账户目录尚未读取时为空。
    /// </summary>
    /// <param name="document">计划。</param>
    internal PuloniaCooldownAccount? GetCooldownAccount(PuloniaTaskPlanDocumentViewModel document)
        => ResolveCooldownAccount(document, _cooldownViewAccountId);

    /// <summary>
    /// 一份计划按统筹设计 5.6 默认显示路线 CD 的账户，不考虑界面临时选择：计划指定账户时为该账户，跟随当前账户时为本实例当前账户；
    /// 都不可用时为账户列表第一个。没有“CD 按账户”选择器的页面（新一条龙）与“选回默认”的判断使用它。账户目录尚未读取时为空。
    /// </summary>
    /// <param name="document">计划。</param>
    internal PuloniaCooldownAccount? GetDefaultCooldownAccount(PuloniaTaskPlanDocumentViewModel document)
        => ResolveCooldownAccount(document, null);

    /// <summary>
    /// 确保已读取账户目录与本实例当前账户并订阅其变化；已经读取过时不再重读（之后的变化由订阅触发重新读取）。
    /// 供不显示路线 CD 标记的页面在需要按账户定位时调用。
    /// </summary>
    internal async Task EnsureCooldownAccountsAsync()
    {
        EnsureCooldownSubscription();
        if (_cooldownAccounts.Count == 0)
            await ReloadCooldownsAsync();
    }

    /// <summary>按 5.6 的顺序选出账户：指定查看的账户 → 计划指定账户 → 本实例当前账户 → 账户列表第一个。</summary>
    /// <param name="document">计划。</param>
    /// <param name="viewAccountId">界面临时选择的账户；为空时按计划设置。</param>
    private PuloniaCooldownAccount? ResolveCooldownAccount(PuloniaTaskPlanDocumentViewModel document, string? viewAccountId)
    {
        var accounts = _cooldownAccounts;
        if (accounts.Count == 0) return null;
        var account = Find(viewAccountId) ?? Find(document.Plan.AccountId) ?? Find(_currentAccountId) ?? accounts[0];
        return new PuloniaCooldownAccount(account.Id, account.Name, PuloniaServerTime.GetUtcOffsetMinutes(account.ServerTimeZone),
            _completions.GetValueOrDefault(account.Id) ?? PuloniaRouteCompletionIndex.Empty);

        PuloniaGenshinAccountProfile? Find(string? id) => id is null ? null : accounts.FirstOrDefault(item => item.Id == id);
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
    /// 目录引用只计入完成记录中仍在 CD 的路线。每份计划按自己选出的账户与其服务器时区判定。
    /// </summary>
    internal void RefreshCooldownBadges()
    {
        var now = DateTimeOffset.UtcNow;
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
            // 账户目录尚未读取：不计算标记，等首次读取完成后再刷新
            if (GetCooldownAccount(document) is not { } account)
            {
                visiting.Remove(document.Id);
                return PuloniaCooldownSummary.Empty;
            }
            var summary = document.RootNode.Children.Aggregate(PuloniaCooldownSummary.Empty,
                (total, child) => total + SummarizeNode(document, child, account));
            visiting.Remove(document.Id);
            summaries[document.Id] = summary;
            var (text, toolTip) = DescribeDocumentSummary(summary, account);
            document.SetCooldownSummary(text, toolTip);
            return summary;
        }

        PuloniaCooldownSummary SummarizeNode(PuloniaTaskPlanDocumentViewModel document, PuloniaTaskNodeViewModel node,
            PuloniaCooldownAccount account)
        {
            // 1. 地图追踪叶子：按节点当前规则判定；停用的叶子仍显示事实，但不计入上层汇总
            if (node.IsLeaf)
            {
                if (document.ResolveCooldownRule(node) is not { } rule)
                {
                    node.SetCooldownBadge(null, false, null);
                    return PuloniaCooldownSummary.Empty;
                }
                var status = PuloniaRouteCooldown.GetStatus(rule, account.Completions.Find(rule.FileIdentity), now,
                    account.ServerUtcOffsetMinutes);
                var (text, caution, toolTip) = DescribeLeaf(rule, status, account, now);
                node.SetCooldownBadge(text, caution, toolTip);
                return node.IsEffectivelyEnabled
                    ? new PuloniaCooldownSummary(1, status.IsSkipped ? 1 : 0,
                        status.State == PuloniaRouteCooldownState.Unmatched ? 1 : 0,
                        status.IsSkipped ? status.NextEligibleAt : null, false)
                    : PuloniaCooldownSummary.Empty;
            }
            // 2. 计划引用：沿用被引用计划自身的汇总（不含引用节点外层的设置覆盖，逐项结果见运行预览）
            PuloniaCooldownSummary summary;
            if (node.Model.Source is { Kind: "plan", PlanId: { } planId })
                summary = documents.FirstOrDefault(item => item.Id == planId) is { } target
                    ? SummarizeDocument(target) : PuloniaCooldownSummary.Empty;
            // 3. 目录引用：不展开目录，只按该组规则判定完成记录中目录下的路线
            else if (document.GetCooldownDirectory(node) is { } directory)
                summary = SummarizeDirectory(directory, account, now);
            // 4. 普通分组：汇总子节点
            else
                summary = node.Children.Aggregate(PuloniaCooldownSummary.Empty,
                    (total, child) => total + SummarizeNode(document, child, account));
            var group = DescribeGroup(summary);
            node.SetCooldownBadge(group.Text, group.Caution, group.ToolTip);
            return node.IsEffectivelyEnabled ? summary : PuloniaCooldownSummary.Empty;
        }
    }

    /// <summary>
    /// 一份计划会用到的路线身份与目录引用位置，含引用计划；供 CD 总览按计划筛选。
    /// </summary>
    internal PuloniaCooldownFileSet GetCooldownFileSet(PuloniaTaskPlanDocumentViewModel document)
    {
        var routes = new HashSet<string>(StringComparer.Ordinal);
        var directories = new List<PuloniaCooldownDirectory>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Collect(document);
        return new PuloniaCooldownFileSet(routes, directories);

        void Collect(PuloniaTaskPlanDocumentViewModel current)
        {
            if (!visited.Add(current.Id)) return;
            foreach (var node in current.EnumerateNodes())
            {
                if (current.ResolveCooldownRule(node) is { } rule)
                    routes.Add(rule.FileIdentity);
                else if (current.GetCooldownDirectory(node) is { } directory)
                    directories.Add(directory);
                else if (node.Model.Source is { Kind: "plan", PlanId: { } planId }
                         && AllDocuments.FirstOrDefault(item => item.Id == planId) is { } target)
                    Collect(target);
            }
        }
    }

    /// <summary>
    /// 给路线 CD 表单附上计划所选账户的状态、清除入口与跨计划规则差异；只处理地图追踪叶子。
    /// </summary>
    /// <param name="draft">表单草稿。</param>
    /// <param name="node">被编辑的叶子。</param>
    internal void AttachCooldownStatus(PuloniaRouteCooldownViewModel draft, PuloniaTaskNodeViewModel node)
    {
        if (!node.IsLeaf || node.Document.ResolveCooldownRule(node) is not { } rule
            || GetCooldownAccount(node.Document) is not { } initial)
            return;
        var key = new PuloniaCooldownRecordKey(initial.Id, rule.FileIdentity);
        draft.SetStatus(DescribeStatus(), Current().Completions.Find(key.Route) is null ? null : ClearAsync);
        draft.SetConflict(FindRuleConflict(node, rule));

        // 清除后会重新读取完成记录，状态按最新读取结果描述
        PuloniaCooldownAccount Current() => GetCooldownAccount(node.Document) is { Id: var id } account && id == key.AccountId
            ? account : initial;

        string DescribeStatus()
        {
            var account = Current();
            var status = PuloniaRouteCooldown.GetStatus(rule, account.Completions.Find(key.Route), DateTimeOffset.UtcNow,
                account.ServerUtcOffsetMinutes);
            var lines = new List<string>
            {
                $"{PuloniaCooldownText.Account(account.Name)}：{PuloniaCooldownText.State(status.State)}",
                status.Completion is null ? "上次成功：没有记录" : "上次成功：" + PuloniaCooldownText.Time(status.LastSuccessAt)
            };
            if (status.NextEligibleAt is { } next)
                lines.Add("下次可执行：" + PuloniaCooldownText.Time(next));
            return string.Join("\n", lines);
        }

        async Task<(string Status, bool HasRecord)> ClearAsync()
        {
            await ClearCooldownRecordsAsync([key], $"“{node.Name}”");
            return (DescribeStatus(), Current().Completions.Find(key.Route) is not null);
        }
    }

    /// <summary>确认后清除指定的完成记录（可跨账户），并重新读取；返回删除的条目数。</summary>
    /// <param name="keys">要清除的记录。</param>
    /// <param name="description">确认框中对清除对象的描述。</param>
    internal async Task<int> ClearCooldownRecordsAsync(IReadOnlyCollection<PuloniaCooldownRecordKey> keys, string description)
    {
        if (keys.Count == 0) return 0;
        var answer = await ThemedMessageBox.ShowAsync(
            $"清除{description}的路线 CD 记录（共 {keys.Count} 条）？\n\n清除后下次运行会执行这些路线；运行历史中的成功记录不受影响；此操作不能撤销。",
            "清除路线 CD 记录", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return 0;
        try
        {
            var removed = 0;
            // 完成记录按账户存放：逐个账户在写锁内读改写
            foreach (var group in keys.GroupBy(key => key.AccountId, StringComparer.Ordinal))
                removed += await AccountService.ClearRouteCompletionsAsync(group.Key,
                    group.Select(key => key.Route).Distinct(StringComparer.Ordinal).ToArray());
            await ReloadCooldownsAsync();
            ReportStatus($"已清除 {removed} 条路线 CD 记录，这些路线下次运行会照常执行。");
            return removed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException
                                       or PuloniaTaskValidationException or Newtonsoft.Json.JsonException)
        {
            var message = "清除路线 CD 记录失败：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法清除路线 CD 记录");
            return 0;
        }
    }

    /// <summary>账户进度变化来自写入线程，切换到 UI 线程后重新读取。</summary>
    private void OnAccountProgressChanged(object? sender, PuloniaAccountProgressChangedEventArgs e) => ReloadOnDispatcher();

    /// <summary>账户目录变化（新增、删除、改名、修改服务器）后重新读取。</summary>
    private void OnAccountsChanged(object? sender, EventArgs e) => ReloadOnDispatcher();

    /// <summary>本实例当前账户变化后，跟随当前账户的计划改按新账户显示。</summary>
    private void OnCurrentAccountChanged(object? sender, PuloniaCurrentAccountChangedEventArgs e)
    {
        if (e.InstanceKey == AccountService.CurrentInstanceKey)
            ReloadOnDispatcher();
    }

    /// <summary>在 UI 线程上重新读取完成记录；程序关闭期间不再访问 UI。</summary>
    private void ReloadOnDispatcher()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        _ = dispatcher.InvokeAsync(ReloadCooldownsAsync);
    }

    /// <summary>
    /// 页面可见期间每分钟重新读取一次（按修改时间比较，发现其他实例的写入），并刷新剩余时间与到期状态；退出后不再访问 UI。
    /// </summary>
    private async Task MonitorCooldownsAsync(Dispatcher dispatcher, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                await dispatcher.InvokeAsync(ReloadCooldownsAsync, DispatcherPriority.Background, ct).Task.Unwrap()
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || dispatcher.HasShutdownStarted) { }
        catch (Exception ex)
        {
            // 刷新失败不得成为未处理异常；下次进入页面会重新计算。
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>目录引用：按该组规则判定账户完成记录中目录下的路线，只统计会跳过的条目。</summary>
    private static PuloniaCooldownSummary SummarizeDirectory(PuloniaCooldownDirectory directory, PuloniaCooldownAccount account,
        DateTimeOffset now)
    {
        var skipped = 0;
        DateTimeOffset? earliest = null;
        foreach (var completion in account.Completions.EnumerateUnder(directory.RoutePrefix, directory.Recursive))
        {
            PuloniaRouteCooldownStatus status;
            try
            {
                status = PuloniaRouteCooldown.GetStatus(
                    PuloniaRouteCooldown.ResolveFromFileIdentity(completion.Route, directory.Setting), completion, now,
                    account.ServerUtcOffsetMinutes);
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
    private static (string? Text, bool Caution, string? ToolTip) DescribeLeaf(PuloniaRouteRefreshRule rule,
        PuloniaRouteCooldownStatus status, PuloniaCooldownAccount account, DateTimeOffset now)
    {
        var text = status.State switch
        {
            PuloniaRouteCooldownState.CoolingDown when status.NextEligibleAt is { } next => "CD 中 · " + PuloniaCooldownText.Remaining(next, now),
            PuloniaRouteCooldownState.Completed => "已完成",
            PuloniaRouteCooldownState.Unmatched => "未识别 CD",
            _ => null
        };
        if (text is null) return (null, false, null);
        var lines = new List<string>
        {
            $"路线 CD（{PuloniaCooldownText.Account(account.Name)}）：{PuloniaCooldownText.State(status.State)}",
            $"规则：{rule.MaterialName ?? "未识别"} · {rule.Description}"
        };
        if (status.LastSuccessAt is { } last) lines.Add("上次成功：" + PuloniaCooldownText.Time(last));
        if (status.NextEligibleAt is { } eligible) lines.Add("下次可执行：" + PuloniaCooldownText.Time(eligible));
        if (status.State == PuloniaRouteCooldownState.Unmatched)
            lines.Add("路径中没有已知的材料或类别目录，成功后不会跳过；可在“共享设置 → 路线 CD”中选择自定义周期。");
        else
            lines.Add("本次运行会跳过；可在“共享设置 → 路线 CD”中清除记录。");
        return (text, status.State == PuloniaRouteCooldownState.Unmatched, string.Join("\n", lines));
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
    private static (string? Text, string? ToolTip) DescribeDocumentSummary(PuloniaCooldownSummary summary, PuloniaCooldownAccount account)
    {
        if (summary.Total == 0 && !summary.HasDirectory) return (null, null);
        var parts = new List<string>();
        if (summary.Skipped > 0) parts.Add($"本次 {summary.Skipped} 项会因 CD 跳过");
        if (summary.Unmatched > 0) parts.Add($"{summary.Unmatched} 项未识别刷新规则");
        var text = "路线 CD：" + (parts.Count == 0 ? "启用的路线均可执行" : string.Join(" · ", parts));
        var toolTip = $"{PuloniaCooldownText.Account(account.Name)}。按计划的运行账户与各节点当前规则计算；运行到每个节点时会按运行账户重新判定。"
                      + (summary.EarliestNext is { } next ? "\n最早可执行：" + PuloniaCooldownText.Time(next) : string.Empty)
                      + (summary.HasDirectory ? "\n目录引用只计入仍在 CD 的记录；逐项结果请使用“预览本次执行”。" : string.Empty);
        return (text, toolTip);
    }

    /// <summary>同一路线在其他节点使用不同规则时返回提示；判定以运行时各节点自己的规则为准。</summary>
    private string? FindRuleConflict(PuloniaTaskNodeViewModel node, PuloniaRouteRefreshRule rule)
    {
        foreach (var document in AllDocuments)
        foreach (var other in document.EnumerateNodes())
        {
            if (ReferenceEquals(other, node) || document.ResolveCooldownRule(other) is not { } otherRule
                || otherRule.FileIdentity != rule.FileIdentity || otherRule.Description == rule.Description)
                continue;
            return $"同一路线在“{document.Name} / {other.Name}”中使用不同规则（{otherRule.Description}）。"
                   + "两处共用同一条完成记录，各自按自己的规则判定。";
        }
        return null;
    }
}
