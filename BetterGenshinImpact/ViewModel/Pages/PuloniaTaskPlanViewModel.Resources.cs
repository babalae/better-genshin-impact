using System;
using System.Collections.Generic;
using BetterGenshinImpact.Core.Script.Repositories;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>计划资源更新检查、树标签与卡片批量确认；磁盘变化不会自动改写固定版本。</summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>统一资源版本读取服务，JS 在创建、准备与续跑中都只检查根目录 manifest.json 和 main.js。</summary>
    private readonly PuloniaTaskResourceVersionService _resourceVersionService;
    /// <summary>串行化异步检查与批量确认，避免过期结果覆盖用户刚确认的版本。</summary>
    private readonly SemaphoreSlim _resourceCheckGate = new(1, 1);
    /// <summary>页面可见时的检查循环取消源；离开页面或关闭软件时终止。</summary>
    private CancellationTokenSource? _resourceMonitorCancellation;
    /// <summary>资源检查是否正在进行，防止用户确认尚未完成的检查结果。</summary>
    [ObservableProperty] private bool _isCheckingResources;

    /// <summary>页面加载、手动刷新和运行提交前读取当前资源；错误作为树标签展示。</summary>
    [RelayCommand]
    public async Task RefreshResourceVersionsAsync(CancellationToken ct = default)
        => await RefreshResourceVersionsCoreAsync(null, ct);

    /// <summary>仓库事件只检查引用该来源的节点，完整刷新仍覆盖旧路径与其他来源。</summary>
    private async Task RefreshResourceVersionsCoreAsync(string? repositoryId, CancellationToken ct)
    {
        await _resourceCheckGate.WaitAsync(ct);
        IsCheckingResources = true;
        try
        {
            var documents = GetResourceDocuments();
            foreach (var document in documents)
            foreach (var node in document.EnumerateNodes().Where(item => item.CanUpdateResourceVersion
                && (repositoryId is null || (item.Model.Resource ?? item.Model.Source?.Resource)?.RepositoryId == repositoryId)).ToArray())
            {
                ct.ThrowIfCancellationRequested();
                var key = ResourceCheckKey(node.Model);
                var snapshot = PuloniaTaskJson.Read<PuloniaTask>(PuloniaTaskJson.Write(node.Model));
                try
                {
                    var state = await _resourceVersionService.ReadCurrentStateAsync(snapshot, _taskService.Definitions, ct);
                    // IO 期间路径、目录来源或固定版本发生变化时，不能把旧检查结果贴到新节点上。
                    if (key != ResourceCheckKey(node.Model)) continue;
                    node.ResourceCheckError = null;
                    node.CurrentResourceVersion = state.CurrentContentVersion;
                    node.CurrentRepositoryRevision = state.CurrentRepositoryRevision;
                    node.ApprovedDeclaredVersion = state.ApprovedDeclaredVersion;
                    node.CurrentDeclaredVersion = state.CurrentDeclaredVersion;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
                    or LibGit2Sharp.LibGit2SharpException)
                {
                    if (key != ResourceCheckKey(node.Model)) continue;
                    node.CurrentResourceVersion = null;
                    node.CurrentRepositoryRevision = null;
                    node.ApprovedDeclaredVersion = null;
                    node.CurrentDeclaredVersion = null;
                    node.ResourceCheckError = snapshot.Resource is not null || snapshot.Source?.Resource is not null
                        ? (ex is FileNotFoundException or DirectoryNotFoundException ? "来源中已移除：" : "检查失败：")
                            + ex.Message + "；仍可使用已确认缓存。" : ex.Message;
                }
            }
            UpdateResourceSummaries(documents);
        }
        finally
        {
            IsCheckingResources = false;
            _resourceCheckGate.Release();
            ConfirmResourceUpdatesCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>卡片目标必须有已完成检查的更新且没有资源读取错误，不依赖当前选中计划。</summary>
    private bool CanConfirmResourceUpdates(PuloniaTaskPlanDocumentViewModel? document)
        => !IsBusy && !IsCheckingResources && document?.HasResourceUpdates == true;

    /// <summary>全部计划批量更新需要至少一个已完成检查的待更新资源。</summary>
    private bool CanUpdateAllResourceVersions()
        => !IsBusy && !IsCheckingResources && GetResourceDocuments().Any(document => document.ResourceUpdateCount > 0);

    /// <summary>一键同步与更新在计划加载完成后始终可用，也能处理尚未建立仓库引用的兼容本地资源。</summary>
    private bool CanSyncAndUpdateResources()
        => !IsBusy && !IsCheckingResources && IsInitialized && GetResourceDocuments().Length > 0;

    /// <summary>用户确认后一次接受计划及其引用计划中的全部资源更新，并立即保存到本地。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmResourceUpdates))]
    private async Task ConfirmResourceUpdatesAsync(PuloniaTaskPlanDocumentViewModel? document)
    {
        if (document is null) return;
        try
        {
            var targets = GetReferencedDocuments(document, GetResourceDocuments()).ToArray();
            await UpdateResourceBatchAsync(targets, $"“{document.Name}”", true);
        }
        catch (Exception ex)
        {
            StatusMessage = "更新任务资源失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法更新此计划资源");
        }
    }

    /// <summary>更新普通计划和一条龙中的全部可用资源；失败项保留旧版本并继续处理其他项。</summary>
    [RelayCommand(CanExecute = nameof(CanUpdateAllResourceVersions))]
    private async Task UpdateAllResourceVersionsAsync()
    {
        try
        {
            await UpdateResourceBatchAsync(GetResourceDocuments(), "全部 Pulonia 任务计划", true);
        }
        catch (Exception ex)
        {
            StatusMessage = "更新所有任务计划失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法更新所有任务计划");
        }
    }

    /// <summary>同步全部计划实际引用的来源，再使用本机内容尽力更新普通计划与一条龙配置。</summary>
    [RelayCommand(CanExecute = nameof(CanSyncAndUpdateResources))]
    private async Task SyncAndUpdateResourcesAsync()
    {
        var documents = GetResourceDocuments();
        var repositoryIds = documents.SelectMany(document => document.EnumerateNodes())
            .Select(node => node.Model.Resource ?? node.Model.Source?.Resource)
            .Where(reference => reference is not null)
            .Select(reference => reference!.RepositoryId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = await ThemedMessageBox.ShowAsync(
            $"将同步或刷新 {repositoryIds.Length} 个任务实际引用的仓库，然后更新全部 Pulonia 任务计划中的可用资源。\n"
            + "同步失败但本机副本仍可用时，将继续使用本机内容；失败资源保持原版本。",
            "一键同步并更新", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        var syncSucceeded = 0;
        var syncFailures = new List<string>();
        try
        {
            for (var index = 0; index < repositoryIds.Length; index++)
            {
                var id = repositoryIds[index];
                try
                {
                    var repository = await _resourceCatalog.Repositories.GetRepositoryAsync(id);
                    StatusMessage = repository.IsRemote
                        ? $"正在同步仓库（{index + 1}/{repositoryIds.Length}）：{repository.Name}"
                        : $"正在刷新本地仓库（{index + 1}/{repositoryIds.Length}）：{repository.Name}";
                    if (repository.IsRemote)
                        await _repositoryManagementService.UpdateAsync(id, null);
                    else
                        await _repositoryManagementService.RefreshLocalAsync(id);
                    syncSucceeded++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                               or ArgumentException or LibGit2Sharp.LibGit2SharpException)
                {
                    // 同步失败不删除或替换已有副本；后续检查会自行判断本机内容是否仍可读取。
                    var fallback = string.Empty;
                    try
                    {
                        using var local = await _resourceCatalog.Repositories.OpenCurrentAsync(id);
                        fallback = "；本机副本仍可用，已继续检查，但它可能不是远端最新版本";
                    }
                    catch (Exception localError) when (localError is IOException or UnauthorizedAccessException
                                                           or InvalidOperationException or ArgumentException
                                                           or LibGit2Sharp.LibGit2SharpException)
                    {
                        fallback = "；没有可用的本机副本，对应资源将跳过";
                    }
                    syncFailures.Add(id + "：" + ex.Message + fallback);
                }
            }

            StatusMessage = "正在检查本机仓库与任务使用版本…";
            var updateResult = await UpdateResourceBatchAsync(documents, "全部 Pulonia 任务计划", false,
                CancellationToken.None, false);
            var syncResult = new PuloniaBatchOperationResult(syncSucceeded, syncFailures);
            StatusMessage = $"仓库同步/刷新成功 {syncResult.Succeeded} 个、失败 {syncResult.Failed} 个；"
                            + $"任务资源更新成功 {updateResult.Succeeded} 项、失败 {updateResult.Failed} 项。";
            var failureReasons = syncResult.FailureReasons.Select(item => "仓库：" + item)
                .Concat(updateResult.FailureReasons.Select(item => "资源：" + item)).ToArray();
            if (failureReasons.Length > 0)
                await ThemedMessageBox.WarningAsync(StatusMessage + "\n\n"
                    + string.Join("\n", failureReasons.Take(10).Select(item => "• " + item))
                    + (failureReasons.Length > 10 ? "\n…" : ""), "一键同步并更新已完成");
        }
        catch (Exception ex)
        {
            StatusMessage = "一键同步并更新失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法完成一键同步并更新");
        }
        finally
        {
            IsBusy = false;
            ConfirmResourceUpdatesCommand.NotifyCanExecuteChanged();
            UpdateAllResourceVersionsCommand.NotifyCanExecuteChanged();
            SyncAndUpdateResourcesCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>逐项准备可用资源，并按计划合并为一次可撤销保存；单项失败不会阻止后续资源。</summary>
    private async Task<PuloniaBatchOperationResult> UpdateResourceBatchAsync(
        IEnumerable<PuloniaTaskPlanDocumentViewModel> sourceDocuments, string scopeName, bool requestConfirmation,
        CancellationToken ct = default, bool displayFailureDialog = true)
    {
        await RefreshResourceVersionsAsync(ct);
        var targets = sourceDocuments.Distinct().Where(document => !document.IsDeleted).ToArray();
        var updates = targets.SelectMany(document => document.EnumerateNodes()
            .Where(node => node.CanUpdateResourceVersion && node.HasResourceUpdate)
            .Select(node => (Document: document, Node: node))).ToArray();
        var initialErrors = targets.SelectMany(document => document.EnumerateNodes()
            .Where(node => node.CanUpdateResourceVersion && node.ResourceCheckError is not null)
            .Select(node => $"{document.Name} / {node.Name}：{node.ResourceCheckError}")).ToList();
        if (updates.Length == 0)
        {
            StatusMessage = initialErrors.Count == 0
                ? $"{scopeName}的任务资源已是本机仓库当前版本。"
                : $"{scopeName}没有可更新资源，{initialErrors.Count} 项检查失败。";
            return new PuloniaBatchOperationResult(0, initialErrors);
        }
        if (requestConfirmation)
        {
            var result = await ThemedMessageBox.ShowAsync(
                $"更新{scopeName}中的 {updates.Length} 项任务资源？\n"
                + string.Join("\n", updates.Take(12).Select(item => "• " + item.Document.Name + " / " + item.Node.Name))
                + (updates.Length > 12 ? "\n…" : "")
                + (initialErrors.Count > 0 ? $"\n另有 {initialErrors.Count} 项检查失败，将保持原版本。" : "")
                + "\n成功项会保存新的任务使用版本；参数、开关与历史快照保持不变。",
                "更新任务资源", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return new PuloniaBatchOperationResult(0, initialErrors);
            // 对话框期间本机仓库或编辑树可能变化，更新前重新检查每个候选项。
            await RefreshResourceVersionsAsync(ct);
            updates = targets.SelectMany(document => document.EnumerateNodes()
                .Where(node => node.CanUpdateResourceVersion && node.HasResourceUpdate)
                .Select(node => (Document: document, Node: node))).ToArray();
        }

        var ownsBusy = !IsBusy;
        await _resourceCheckGate.WaitAsync(ct);
        if (ownsBusy) IsBusy = true;
        var failures = new List<string>(initialErrors);
        var prepared = new List<(PuloniaTaskPlanDocumentViewModel Document, PuloniaTaskNodeViewModel Node,
            string Version, ScriptResourceReference? Reference, string Key)>();
        try
        {
            var processed = 0;
            foreach (var update in updates)
            {
                ct.ThrowIfCancellationRequested();
                StatusMessage = $"正在更新任务资源（{++processed}/{updates.Length}）：{update.Node.Name}";
                var node = update.Node;
                var version = node.CurrentResourceVersion;
                if (version is null || node.ResourceCheckError is not null)
                {
                    failures.Add($"{update.Document.Name} / {node.Name}：资源检查结果不可用");
                    continue;
                }
                var key = ResourceCheckKey(node.Model);
                var snapshot = PuloniaTaskJson.Read<PuloniaTask>(PuloniaTaskJson.Write(node.Model));
                var state = new PuloniaTaskResourceVersionState(version, node.CurrentRepositoryRevision,
                    node.ApprovedDeclaredVersion, node.CurrentDeclaredVersion);
                try
                {
                    var reference = await _resourceVersionService.PrepareUpdateAsync(snapshot, state, ct);
                    if (key != ResourceCheckKey(node.Model) || !update.Document.EnumerateNodes().Contains(node))
                        throw new InvalidOperationException("准备期间任务配置发生变化");
                    prepared.Add((update.Document, node, version, reference, key));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                               or ArgumentException or LibGit2Sharp.LibGit2SharpException
                                               or System.Text.Json.JsonException or Newtonsoft.Json.JsonException)
                {
                    failures.Add($"{update.Document.Name} / {node.Name}：{ex.Message}");
                }
            }

            var updated = 0;
            foreach (var group in prepared.GroupBy(item => item.Document))
            {
                var valid = group.Where(item => item.Key == ResourceCheckKey(item.Node.Model)).ToArray();
                foreach (var invalid in group.Except(valid))
                    failures.Add($"{group.Key.Name} / {invalid.Node.Name}：保存前任务配置发生变化");
                if (valid.Length == 0) continue;
                Action? rollback = null;
                var rollbackAttempted = false;
                try
                {
                    rollback = group.Key.ApplyConfirmedResourceVersions(valid.ToDictionary(item => item.Node.Id, item => item.Version),
                        valid.ToDictionary(item => item.Node.Id, item => item.Reference));
                    if (await SaveDocumentAsync(group.Key)) updated += valid.Length;
                    else
                    {
                        rollbackAttempted = true;
                        rollback();
                        foreach (var item in valid)
                            failures.Add($"{group.Key.Name} / {item.Node.Name}：计划保存失败");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                               or ArgumentException or Newtonsoft.Json.JsonException)
                {
                    if (rollback is not null && !rollbackAttempted)
                    {
                        rollbackAttempted = true;
                        try { rollback(); }
                        catch (InvalidOperationException rollbackError)
                        {
                            failures.Add($"{group.Key.Name}：{rollbackError.Message}");
                            continue;
                        }
                    }
                    foreach (var item in valid)
                        failures.Add($"{group.Key.Name} / {item.Node.Name}：{ex.Message}");
                }
            }
            UpdateResourceSummaries(GetResourceDocuments());
            StatusMessage = failures.Count == 0
                ? $"已更新{scopeName}中的 {updated} 项任务资源。"
                : $"已更新 {updated} 项任务资源，{failures.Count} 项失败并保持原版本。";
            if (failures.Count > 0 && displayFailureDialog)
                await ThemedMessageBox.WarningAsync(StatusMessage + "\n\n"
                    + string.Join("\n", failures.Take(10).Select(item => "• " + item))
                    + (failures.Count > 10 ? "\n…" : ""), "部分任务资源未更新");
            return new PuloniaBatchOperationResult(updated, failures);
        }
        finally
        {
            if (ownsBusy) IsBusy = false;
            _resourceCheckGate.Release();
            ConfirmResourceUpdatesCommand.NotifyCanExecuteChanged();
            UpdateAllResourceVersionsCommand.NotifyCanExecuteChanged();
            SyncAndUpdateResourcesCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>包含当前独立编辑文档，便于卡片操作与进程内调用共享同一路径。</summary>
    private PuloniaTaskPlanDocumentViewModel[] GetResourceDocuments()
        => AllDocuments.Concat(SelectedDocument is { } selected ? [selected] : Array.Empty<PuloniaTaskPlanDocumentViewModel>()).Distinct().ToArray();

    /// <summary>递归收集引用计划，按计划 ID 去重；无论重复引用还是循环草稿都不能无限扫描。</summary>
    private static IEnumerable<PuloniaTaskPlanDocumentViewModel> GetReferencedDocuments(
        PuloniaTaskPlanDocumentViewModel root, IReadOnlyList<PuloniaTaskPlanDocumentViewModel> documents, bool enabledOnly = false)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<PuloniaTaskPlanDocumentViewModel>();
        pending.Push(root);
        while (pending.TryPop(out var document))
        {
            if (!visited.Add(document.Id)) continue;
            yield return document;
            foreach (var node in document.EnumerateNodes())
                if ((!enabledOnly || node.IsEffectivelyEnabled) && node.Model.Source is { Kind: "plan", PlanId: { } id }
                    && documents.FirstOrDefault(item => item.Id == id) is { } target)
                    pending.Push(target);
        }
    }

    /// <summary>更新引用节点标签及卡片统计，不能用只看当前计划的统计漏掉引用资源。</summary>
    private void UpdateResourceSummaries(IReadOnlyList<PuloniaTaskPlanDocumentViewModel> documents)
    {
        foreach (var document in documents)
        {
            var related = GetReferencedDocuments(document, documents).ToArray();
            var resources = related.SelectMany(item => item.EnumerateNodes()).Where(node => node.CanUpdateResourceVersion).ToArray();
            document.ResourceUpdateCount = resources.Count(node => node.HasResourceUpdate);
            document.ResourceErrorCount = resources.Count(node => node.ResourceCheckError is not null)
                + related.Sum(item => item.EnumerateNodes().Count(node => node.Model.Source is { Kind: "plan", PlanId: { } id }
                    && documents.All(target => target.Id != id)));
        }
        foreach (var document in documents)
        foreach (var node in document.EnumerateNodes().Where(item => item.Model.Source?.Kind == "plan"))
        {
            var target = documents.FirstOrDefault(item => item.Id == node.Model.Source!.PlanId);
            node.ReferencedResourceUpdateCount = target?.ResourceUpdateCount ?? 0;
            node.ResourceCheckError = target is null ? "引用计划不存在，无法检查资源。"
                : target.ResourceErrorCount > 0 ? "引用计划中存在无法读取的资源，请打开该计划检查。" : null;
        }
        ConfirmResourceUpdatesCommand.NotifyCanExecuteChanged();
        UpdateAllResourceVersionsCommand.NotifyCanExecuteChanged();
        SyncAndUpdateResourcesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>只捕获影响资源定位和比较的字段，参数或名称修改不会把无关 IO 结果误判为新版本。</summary>
    private static string ResourceCheckKey(PuloniaTask task)
        => PuloniaTaskJson.Write(new { task.TaskType, task.Path, task.Resource, task.Source, task.ResourceVersion });

    /// <summary>页面可见时定期检查外部编辑或本机仓库变化，不在 UI 线程同步读取文件。</summary>
    private void StartResourceMonitor()
    {
        if (_resourceMonitorCancellation is not null || Application.Current?.Dispatcher is not { } dispatcher) return;
        _resourceMonitorCancellation = new CancellationTokenSource();
        _resourceCatalog.Repositories.RepositoryChanged += OnRepositoryChanged;
        _ = MonitorResourceVersionsAsync(dispatcher, _resourceMonitorCancellation.Token);
    }

    /// <summary>观察循环的取消与错误，退出后不访问 Application.Current 或重新排队 UI 工作。</summary>
    private async Task MonitorResourceVersionsAsync(Dispatcher dispatcher, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                await dispatcher.InvokeAsync(() => RefreshResourceVersionsAsync(ct), DispatcherPriority.Background, ct)
                    .Task.Unwrap().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) { }
        catch (Exception ex)
        {
            // 监控失败不得成为关闭期间的未处理异常；下次导航仍可重新进行显式检查。
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>导航离开或控件卸载时停止检查，不改变已经保存的计划或历史。</summary>
    public override Task OnNavigatedFromAsync()
    {
        _resourceMonitorCancellation?.Cancel();
        _resourceCatalog.Repositories.RepositoryChanged -= OnRepositoryChanged;
        _resourceMonitorCancellation?.Dispose();
        _resourceMonitorCancellation = null;
        return base.OnNavigatedFromAsync();
    }

    /// <summary>控件卸载同样释放检查循环，兼容导航缓存及窗口关闭。</summary>
    [RelayCommand]
    private Task ClosePageAsync() => OnNavigatedFromAsync();

    /// <summary>仓库更新仅触发相关页面检查，不在更新线程访问编辑树或改写固定版本。</summary>
    private void OnRepositoryChanged(object? sender, ScriptRepositoryChangedEventArgs args)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || _resourceMonitorCancellation is null) return;
        _ = dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (_resourceMonitorCancellation is not { } monitor) return;
                if (GetResourceDocuments().SelectMany(document => document.EnumerateNodes()).Any(node =>
                    (node.Model.Resource ?? node.Model.Source?.Resource)?.RepositoryId == args.RepositoryId))
                    await RefreshResourceVersionsCoreAsync(args.RepositoryId, monitor.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        });
    }

    /// <summary>检查状态变化时同步卡片确认命令，不自动修改计划内容。</summary>
    partial void OnIsCheckingResourcesChanged(bool value)
    {
        ConfirmResourceUpdatesCommand.NotifyCanExecuteChanged();
        UpdateAllResourceVersionsCommand.NotifyCanExecuteChanged();
        SyncAndUpdateResourcesCommand.NotifyCanExecuteChanged();
    }
}
