using System;
using System.Collections.Generic;
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
    /// <summary>统一资源版本读取服务，和创建、准备、续跑保持相同的 JS 静态资源范围。</summary>
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
    {
        await _resourceCheckGate.WaitAsync(ct);
        IsCheckingResources = true;
        try
        {
            var documents = GetResourceDocuments();
            foreach (var document in documents)
            foreach (var node in document.EnumerateNodes().Where(item => item.CanUpdateResourceVersion).ToArray())
            {
                ct.ThrowIfCancellationRequested();
                var key = ResourceCheckKey(node.Model);
                var snapshot = PuloniaTaskJson.Read<PuloniaTask>(PuloniaTaskJson.Write(node.Model));
                try
                {
                    var version = await _resourceVersionService.ReadCurrentVersionAsync(snapshot, _taskService.Definitions, ct);
                    // IO 期间路径、目录来源或固定版本发生变化时，不能把旧检查结果贴到新节点上。
                    if (key != ResourceCheckKey(node.Model)) continue;
                    node.ResourceCheckError = null;
                    node.CurrentResourceVersion = version;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                {
                    if (key != ResourceCheckKey(node.Model)) continue;
                    node.CurrentResourceVersion = null;
                    node.ResourceCheckError = ex.Message;
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

    /// <summary>用户确认后一次接受计划及其引用计划中的全部资源更新，并立即保存到本地。</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmResourceUpdates))]
    private async Task ConfirmResourceUpdatesAsync(PuloniaTaskPlanDocumentViewModel? document)
    {
        if (document is null) return;
        try
        {
            await ConfirmResourceUpdatesCoreAsync(document);
        }
        catch (Exception ex)
        {
            StatusMessage = "确认资源更新失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法确认全部资源更新");
        }
    }

    /// <summary>重新核对用户已审阅的完整更新集合，再串行修改与保存相关计划。</summary>
    private async Task ConfirmResourceUpdatesCoreAsync(PuloniaTaskPlanDocumentViewModel document)
    {
        await RefreshResourceVersionsAsync();
        var targets = GetReferencedDocuments(document, GetResourceDocuments()).ToArray();
        var updates = targets.SelectMany(target => target.EnumerateNodes()
            .Where(node => node.CanUpdateResourceVersion && node.HasResourceUpdate)
            .Select(node => (Document: target, Node: node, Version: node.CurrentResourceVersion!, Key: ResourceCheckKey(node.Model)))).ToArray();
        if (!document.HasResourceUpdates || updates.Length == 0) return;
        var result = await ThemedMessageBox.ShowAsync(
            $"确认“{document.Name}”的全部 {updates.Length} 项资源更新？\n"
            + string.Join("\n", updates.Take(12).Select(item => "• " + item.Document.Name + " / " + item.Node.Name))
            + (updates.Length > 12 ? "\n…" : "")
            + "\n将保存新的固定版本；现有参数、开关与历史快照保持不变。"
            + (targets.Length > 1 ? "\n其中包含引用计划，相关计划的资源版本也会一并更新。" : ""),
            "确认全部资源更新", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        // 对话框期间文件或编辑树可能变化，必须重新读取且与用户刚确认的内容一致。
        await RefreshResourceVersionsAsync();
        await _resourceCheckGate.WaitAsync();
        IsBusy = true;
        try
        {
            var currentTargets = GetReferencedDocuments(document, GetResourceDocuments()).ToArray();
            var currentUpdates = currentTargets.SelectMany(target => target.EnumerateNodes())
                .Where(node => node.CanUpdateResourceVersion && node.HasResourceUpdate).ToHashSet();
            // 对话框期间新增节点或改换引用计划也属于未审阅变更，不能只接受原集合的一个子集。
            if (!targets.ToHashSet().SetEquals(currentTargets)
                || !currentUpdates.SetEquals(updates.Select(item => item.Node))
                || document.ResourceErrorCount > 0 || updates.Any(item => item.Node.CurrentResourceVersion != item.Version
                || item.Key != ResourceCheckKey(item.Node.Model)))
                throw new InvalidOperationException("确认期间资源或任务配置再次变化，请重新检查并确认。");
            foreach (var group in updates.GroupBy(item => item.Document))
            {
                group.Key.ApplyConfirmedResourceVersions(group.ToDictionary(item => item.Node.Id, item => item.Version));
                if (!await SaveDocumentAsync(group.Key)) return;
            }
            UpdateResourceSummaries(GetResourceDocuments());
            StatusMessage = $"已确认并保存“{document.Name}”的 {updates.Length} 项资源更新，历史记录未改动。";
        }
        finally
        {
            IsBusy = false;
            _resourceCheckGate.Release();
            ConfirmResourceUpdatesCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>包含当前独立编辑文档，便于卡片操作与进程内调用共享同一路径。</summary>
    private PuloniaTaskPlanDocumentViewModel[] GetResourceDocuments()
        => Documents.Concat(SelectedDocument is { } selected ? [selected] : Array.Empty<PuloniaTaskPlanDocumentViewModel>()).Distinct().ToArray();

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
    private static void UpdateResourceSummaries(IReadOnlyList<PuloniaTaskPlanDocumentViewModel> documents)
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
    }

    /// <summary>只捕获影响资源定位和比较的字段，参数或名称修改不会把无关 IO 结果误判为新版本。</summary>
    private static string ResourceCheckKey(PuloniaTask task)
        => PuloniaTaskJson.Write(new { task.TaskType, task.Path, task.Source, task.ResourceVersion });

    /// <summary>页面可见时定期检查外部编辑或脚本订阅更新，不在 UI 线程同步读取文件。</summary>
    private void StartResourceMonitor()
    {
        if (_resourceMonitorCancellation is not null || Application.Current?.Dispatcher is not { } dispatcher) return;
        _resourceMonitorCancellation = new CancellationTokenSource();
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
        _resourceMonitorCancellation?.Dispose();
        _resourceMonitorCancellation = null;
        return base.OnNavigatedFromAsync();
    }

    /// <summary>控件卸载同样释放检查循环，兼容导航缓存及窗口关闭。</summary>
    [RelayCommand]
    private Task ClosePageAsync() => OnNavigatedFromAsync();

    /// <summary>检查状态变化时同步卡片确认命令，不自动修改计划内容。</summary>
    partial void OnIsCheckingResourcesChanged(bool value) => ConfirmResourceUpdatesCommand.NotifyCanExecuteChanged();
}
