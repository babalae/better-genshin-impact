using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>计划资源更新检查、树标签与卡片批量确认；磁盘变化不会自动改写固定版本。</summary>
public partial class PuloniaPlanEditingSession
{
    /// <summary>任务计划页复用的仓库同步服务；老配置组的订阅与安装流程不经过此服务。</summary>
    private readonly PuloniaRepositoryManagementService _repositoryManagementService;
    /// <summary>统一资源版本读取服务，JS 在创建、准备与续跑中都只检查根目录 manifest.json 和 main.js。</summary>
    private readonly PuloniaTaskResourceVersionService _resourceVersionService;
    /// <summary>串行化异步检查与批量确认，避免过期结果覆盖用户刚确认的版本。</summary>
    private readonly SemaphoreSlim _resourceCheckGate = new(1, 1);
    /// <summary>当前可见并需要定期资源检查的页面；全部离开后停止检查。</summary>
    private readonly HashSet<object> _resourceMonitorOwners = [];
    /// <summary>页面可见时的检查循环取消源；全部页面离开或关闭软件时终止。</summary>
    private CancellationTokenSource? _resourceMonitorCancellation;
    /// <summary>资源检查是否正在进行，防止用户确认尚未完成的检查结果。</summary>
    [ObservableProperty] private bool _isCheckingResources;

    /// <summary>页面加载、手动刷新和运行提交前读取当前资源；错误作为树标签展示。</summary>
    [RelayCommand]
    public async Task RefreshResourceVersionsAsync(CancellationToken ct = default)
        => await RefreshResourceVersionsCoreAsync(null, ct);

    /// <summary>
    /// 仓库事件只检查引用该来源的节点，完整刷新仍覆盖旧路径与其他来源。
    /// 一轮检查交给服务批量读取：每个仓库只打开一次当前快照，已确认版本的声明信息先查缓存。
    /// </summary>
    /// <param name="repositoryId">只检查引用该仓库的节点；为空表示全部。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="extra">尚未加入会话、但本次需要一并检查的文档（例如直接提交运行的计划）。</param>
    private async Task RefreshResourceVersionsCoreAsync(string? repositoryId, CancellationToken ct,
        PuloniaTaskPlanDocumentViewModel? extra = null)
    {
        await _resourceCheckGate.WaitAsync(ct);
        IsCheckingResources = true;
        try
        {
            var documents = GetResourceDocuments(extra);
            var nodes = documents.SelectMany(document => document.EnumerateNodes()).Where(item => item.CanUpdateResourceVersion
                && (repositoryId is null || (item.Model.Resource ?? item.Model.Source?.Resource)?.RepositoryId == repositoryId)).ToArray();
            // 服务读取节点副本，后台 IO 期间界面编辑不会改变本轮读取的任务。
            var keys = nodes.Select(node => ResourceCheckKey(node.Model)).ToArray();
            var snapshots = nodes.Select(node => PuloniaTaskJson.Read<PuloniaTask>(PuloniaTaskJson.Write(node.Model))).ToArray();
            var results = await _resourceVersionService.ReadCurrentStatesAsync(snapshots, TaskService.Definitions, ct);
            for (var index = 0; index < nodes.Length; index++)
            {
                var node = nodes[index];
                var snapshot = snapshots[index];
                // IO 期间路径、目录来源或固定版本发生变化时，不能把旧检查结果贴到新节点上；等待下一轮检查。
                if (keys[index] != ResourceCheckKey(node.Model)) continue;
                if (results[index].State is { } state)
                {
                    node.ResourceCheckError = null;
                    node.CurrentResourceVersion = state.CurrentContentVersion;
                    node.CurrentRepositoryRevision = state.CurrentRepositoryRevision;
                    node.ApprovedDeclaredVersion = state.ApprovedDeclaredVersion;
                    node.CurrentDeclaredVersion = state.CurrentDeclaredVersion;
                    continue;
                }
                var ex = results[index].Error!;
                node.CurrentResourceVersion = null;
                node.CurrentRepositoryRevision = null;
                node.ApprovedDeclaredVersion = null;
                node.CurrentDeclaredVersion = null;
                node.ResourceCheckError = snapshot.Resource is not null || snapshot.Source?.Resource is not null
                    ? (ex is FileNotFoundException or DirectoryNotFoundException ? "来源中已移除：" : "检查失败：")
                        + ex.Message + "；仍可使用已确认缓存。" : ex.Message;
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

    /// <summary>
    /// 重新读取本机仓库并在用户确认后更新任务使用版本，资源变化不会静默影响运行。
    /// </summary>
    /// <param name="node">要更新资源的节点。</param>
    [RelayCommand]
    private async Task UpdateResourceVersionAsync(PuloniaTaskNodeViewModel? node)
    {
        if (node is null || !node.CanUpdateResourceVersion)
            return;
        try
        {
            var model = node.Model;
            var key = ResourceCheckKey(model);
            var snapshot = PuloniaTaskJson.Read<PuloniaTask>(PuloniaTaskJson.Write(model));
            string newVersion;
            ScriptResourceReference? updatedReference;
            // 资源版本服务的缓存不是线程安全的，与页面检查共用同一个门串行调用；对话框期间暂停后台检查。
            await _resourceCheckGate.WaitAsync();
            try
            {
                var reviewed = await _resourceVersionService.ReadCurrentStateAsync(snapshot, TaskService.Definitions);
                newVersion = reviewed.CurrentContentVersion
                             ?? throw new InvalidOperationException("当前节点没有可确认的资源。");
                var oldVersion = model.Source?.Kind == "directory" ? model.Source.Version : model.ResourceVersion;

                if (oldVersion == newVersion)
                {
                    ReportStatus($"“{node.Name}”的资源版本没有变化。");
                    return;
                }
                var result = await ThemedMessageBox.ShowAsync(
                    $"资源“{node.Name}”已经变化。\n\n{FormatVersionChange(reviewed)}\n\n确认后只影响之后的运行，已有执行记录不变。",
                    "更新任务资源", MessageBoxButton.YesNo,
                    ThemedMessageBox.MessageBoxIcon.Question, MessageBoxResult.No);
                if (result != MessageBoxResult.Yes)
                    return;

                if (newVersion != await _resourceVersionService.ReadCurrentVersionAsync(snapshot, TaskService.Definitions)
                    || key != ResourceCheckKey(model) || !node.Document.EnumerateNodes().Contains(node))
                    throw new InvalidOperationException("确认期间资源再次变化，请重新检查并确认。");

                updatedReference = await _resourceVersionService.PrepareUpdateAsync(snapshot, reviewed);
            }
            finally
            {
                _resourceCheckGate.Release();
            }
            if (key != ResourceCheckKey(model))
                throw new InvalidOperationException("提取期间任务配置变化，请重新确认。");
            node.Document.ApplyMutation(() =>
            {
                if (model.Source?.Kind == "directory")
                {
                    model.Source.Version = newVersion;
                    if (updatedReference is not null) model.Source.Resource = updatedReference;
                }
                else
                {
                    model.ResourceVersion = newVersion;
                    if (updatedReference is not null) model.Resource = updatedReference;
                }
            }, node);
            node.NotifyResourceVersionChanged();
            if (!await SaveDocumentAsync(node.Document)) return;
            await RefreshResourceVersionsAsync();
            ReportStatus($"已更新“{node.Name}”的任务使用版本。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
            or LibGit2Sharp.LibGit2SharpException or System.Text.Json.JsonException or Newtonsoft.Json.JsonException)
        {
            var message = "更新任务资源失败：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法更新任务资源");
        }
    }

    /// <summary>优先展示资源声明版本变化；缺少可读版本时只说明内容已经变化。</summary>
    private static string FormatVersionChange(PuloniaTaskResourceVersionState state)
    {
        if (!string.IsNullOrWhiteSpace(state.ApprovedDeclaredVersion)
            && !string.IsNullOrWhiteSpace(state.CurrentDeclaredVersion)
            && state.ApprovedDeclaredVersion != state.CurrentDeclaredVersion)
            return $"版本：{state.ApprovedDeclaredVersion} → {state.CurrentDeclaredVersion}";
        return "内容已经变化，但资源没有可显示的版本号变化。";
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
            var message = "更新任务资源失败：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法更新此计划资源");
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
            var message = "更新所有任务计划失败：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法更新所有任务计划");
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
                    var repository = await ResourceCatalog.Repositories.GetRepositoryAsync(id);
                    ReportStatus(repository.IsRemote
                        ? $"正在同步仓库（{index + 1}/{repositoryIds.Length}）：{repository.Name}"
                        : $"正在刷新本地仓库（{index + 1}/{repositoryIds.Length}）：{repository.Name}");
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
                        using var local = await ResourceCatalog.Repositories.OpenCurrentAsync(id);
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

            ReportStatus("正在检查本机仓库与任务使用版本…");
            var updateResult = await UpdateResourceBatchAsync(documents, "全部 Pulonia 任务计划", false,
                CancellationToken.None, false);
            var syncResult = new PuloniaBatchOperationResult(syncSucceeded, syncFailures);
            var summary = $"仓库同步/刷新成功 {syncResult.Succeeded} 个、失败 {syncResult.Failed} 个；"
                          + $"任务资源更新成功 {updateResult.Succeeded} 项、失败 {updateResult.Failed} 项。";
            ReportStatus(summary);
            var failureReasons = syncResult.FailureReasons.Select(item => "仓库：" + item)
                .Concat(updateResult.FailureReasons.Select(item => "资源：" + item)).ToArray();
            if (failureReasons.Length > 0)
                await ThemedMessageBox.WarningAsync(summary + "\n\n"
                    + string.Join("\n", failureReasons.Take(10).Select(item => "• " + item))
                    + (failureReasons.Length > 10 ? "\n…" : ""), "一键同步并更新已完成");
        }
        catch (Exception ex)
        {
            var message = "一键同步并更新失败：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法完成一键同步并更新");
        }
        finally
        {
            IsBusy = false;
            NotifyResourceCommands();
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
            ReportStatus(initialErrors.Count == 0
                ? $"{scopeName}的任务资源已是本机仓库当前版本。"
                : $"{scopeName}没有可更新资源，{initialErrors.Count} 项检查失败。");
            return new PuloniaBatchOperationResult(0, initialErrors);
        }
        if (requestConfirmation)
        {
            var result = await ThemedMessageBox.ShowAsync(
                $"更新{scopeName}中的 {updates.Length} 项任务资源？\n"
                + string.Join("\n", updates.Take(12).Select(item => "• " + item.Document.Name + " / " + item.Node.Name))
                + (updates.Length > 12 ? "\n…" : "")
                + (initialErrors.Count > 0 ? $"\n另有 {initialErrors.Count} 项检查失败，将保持原版本。" : "")
                + "\n成功项会保存新的任务使用版本；参数、开关与已有执行记录保持不变。",
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
            string Version, ScriptResourceReference? Reference)>();
        try
        {
            var processed = 0;
            foreach (var update in updates)
            {
                ct.ThrowIfCancellationRequested();
                ReportStatus($"正在更新任务资源（{++processed}/{updates.Length}）：{update.Node.Name}");
                var node = update.Node;
                var version = node.CurrentResourceVersion;
                if (version is null || node.ResourceCheckError is not null)
                {
                    failures.Add($"{update.Document.Name} / {node.Name}：资源检查结果不可用");
                    continue;
                }
                var snapshot = PuloniaTaskJson.Read<PuloniaTask>(PuloniaTaskJson.Write(node.Model));
                var state = new PuloniaTaskResourceVersionState(version, node.CurrentRepositoryRevision,
                    node.ApprovedDeclaredVersion, node.CurrentDeclaredVersion);
                try
                {
                    // 准备时在固定来源上核对修订与指纹；节点在此期间被编辑或删除，由下面的整批写入统一拒绝。
                    var reference = await _resourceVersionService.PrepareUpdateAsync(snapshot, state, ct);
                    prepared.Add((update.Document, node, version, reference));
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
                try
                {
                    // 路径或来源修改会清空节点检查结果，删除会让节点不在树中；这两种情况整份计划保持原版本。
                    group.Key.ApplyConfirmedResourceVersions(group.ToDictionary(item => item.Node.Id, item => item.Version),
                        group.ToDictionary(item => item.Node.Id, item => item.Reference));
                }
                catch (InvalidOperationException ex)
                {
                    foreach (var item in group)
                        failures.Add($"{group.Key.Name} / {item.Node.Name}：{ex.Message}");
                    continue;
                }
                // 保存失败不自动回滚：新版本留在草稿中并显示 LastSaveError，用户可撤销本次更新或稍后重试保存。
                if (await SaveDocumentAsync(group.Key)) updated += group.Count();
                else
                    foreach (var item in group)
                        failures.Add($"{group.Key.Name} / {item.Node.Name}：计划保存失败，更新保留在未保存的修改中，可撤销");
            }
            UpdateResourceSummaries(GetResourceDocuments());
            var summary = failures.Count == 0
                ? $"已更新{scopeName}中的 {updated} 项任务资源。"
                : $"已更新 {updated} 项任务资源，{failures.Count} 项失败并保持原版本。";
            ReportStatus(summary);
            if (failures.Count > 0 && displayFailureDialog)
                await ThemedMessageBox.WarningAsync(summary + "\n\n"
                    + string.Join("\n", failures.Take(10).Select(item => "• " + item))
                    + (failures.Count > 10 ? "\n…" : ""), "部分任务资源未更新");
            return new PuloniaBatchOperationResult(updated, failures);
        }
        finally
        {
            if (ownsBusy) IsBusy = false;
            _resourceCheckGate.Release();
            NotifyResourceCommands();
        }
    }

    /// <summary>会话内全部文档，可附带一份尚未加入会话的文档，便于直接调用与卡片操作共享同一路径。</summary>
    /// <param name="extra">额外纳入检查的文档。</param>
    private PuloniaTaskPlanDocumentViewModel[] GetResourceDocuments(PuloniaTaskPlanDocumentViewModel? extra = null)
        => AllDocuments.Concat(extra is not null ? [extra] : Array.Empty<PuloniaTaskPlanDocumentViewModel>()).Distinct().ToArray();

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
        NotifyResourceCommands();
    }

    /// <summary>只捕获影响资源定位和比较的字段，参数或名称修改不会把无关 IO 结果误判为新版本。</summary>
    private static string ResourceCheckKey(PuloniaTask task)
        => PuloniaTaskJson.Write(new { task.TaskType, task.Path, task.Resource, task.Source, task.ResourceVersion });

    /// <summary>
    /// 页面可见时登记为资源检查的使用者；至少一个页面可见时保持定期检查外部编辑或本机仓库变化。
    /// </summary>
    /// <param name="owner">登记的页面视图模型。</param>
    /// <returns>本轮检查的取消令牌；没有 UI 调度器（例如测试）时不启动检查。</returns>
    internal CancellationToken StartResourceMonitor(object owner)
    {
        _resourceMonitorOwners.Add(owner);
        if (_resourceMonitorCancellation is null && Application.Current?.Dispatcher is { } dispatcher)
        {
            _resourceMonitorCancellation = new CancellationTokenSource();
            ResourceCatalog.Repositories.RepositoryChanged += OnRepositoryChanged;
            _ = MonitorResourceVersionsAsync(dispatcher, _resourceMonitorCancellation.Token);
        }
        return _resourceMonitorCancellation?.Token ?? CancellationToken.None;
    }

    /// <summary>
    /// 页面离开时注销；最后一个页面离开后停止检查，不改变已经保存的计划或历史。
    /// </summary>
    /// <param name="owner">之前登记的页面视图模型。</param>
    internal void StopResourceMonitor(object owner)
    {
        if (!_resourceMonitorOwners.Remove(owner) || _resourceMonitorOwners.Count > 0
            || _resourceMonitorCancellation is not { } cancellation)
            return;
        cancellation.Cancel();
        ResourceCatalog.Repositories.RepositoryChanged -= OnRepositoryChanged;
        cancellation.Dispose();
        _resourceMonitorCancellation = null;
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

    /// <summary>仓库更新仅触发相关检查，不在更新线程访问编辑树或改写固定版本。</summary>
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
    partial void OnIsCheckingResourcesChanged(bool value) => NotifyResourceCommands();

    /// <summary>通知资源相关命令重新计算可执行状态。</summary>
    private void NotifyResourceCommands()
    {
        ConfirmResourceUpdatesCommand.NotifyCanExecuteChanged();
        UpdateAllResourceVersionsCommand.NotifyCanExecuteChanged();
        SyncAndUpdateResourcesCommand.NotifyCanExecuteChanged();
    }
}
