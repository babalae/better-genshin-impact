using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 任务计划页与新一条龙页共用的单例编辑会话：持有全部计划文档，负责加载、自动保存、计划增删与导入、
/// 资源版本检查和运行提交。页面选择、菜单和弹窗输入留在各自的页面视图模型。
/// </summary>
public partial class PuloniaPlanEditingSession : ObservableObject
{
    /// <summary>
    /// 连续编辑完成后等待写盘的时间，避免快速勾选或拖拽产生无意义的中间修订。
    /// </summary>
    private const int AutoSaveDelayMilliseconds = 350;

    /// <summary>
    /// 计划、设置方案与编辑器元数据的文件存储服务。
    /// </summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>
    /// 当前从存储加载的设置方案，按名称排序。
    /// </summary>
    private IReadOnlyList<PuloniaSettingsProfile> _profiles = [];

    /// <summary>
    /// 每个计划当前等待执行的自动保存延迟，用于合并短时间内的连续操作。
    /// </summary>
    private readonly Dictionary<PuloniaTaskPlanDocumentViewModel, CancellationTokenSource> _autoSaveDelays = [];

    /// <summary>
    /// 首次加载任务；两个页面的进入入口复用同一次加载，失败后下次进入重新读取。
    /// </summary>
    private Task? _initialization;

    /// <summary>
    /// 会话是否正在执行文件读写或批量操作；两个页面共用，期间禁止其他整份计划操作。
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// 是否已经成功完成首次加载；失败时不得创建并覆盖尚未加载的配置。
    /// </summary>
    [ObservableProperty]
    private bool _isInitialized;

    /// <summary>
    /// 跨计划复制使用的进程内剪贴板。
    /// </summary>
    public PuloniaTaskClipboardService Clipboard { get; }

    /// <summary>
    /// 统一的任务提交、状态查询和取消服务。
    /// </summary>
    public IPuloniaTaskService TaskService { get; }

    /// <summary>
    /// 任务创建弹窗与资源检查使用的资源索引。
    /// </summary>
    public PuloniaTaskResourceCatalog ResourceCatalog { get; }

    /// <summary>
    /// 普通任务计划，任务计划页的列表直接绑定此集合。
    /// </summary>
    public ObservableCollection<PuloniaTaskPlanDocumentViewModel> Documents { get; } = [];

    /// <summary>
    /// 仅属于一条龙入口的配置，与普通任务计划列表分开。
    /// </summary>
    public ObservableCollection<PuloniaTaskPlanDocumentViewModel> OneDragonDocuments { get; } = [];

    /// <summary>
    /// 完整编辑会话，用于保存、引用校验和资源检查。
    /// </summary>
    internal IEnumerable<PuloniaTaskPlanDocumentViewModel> AllDocuments => Documents.Concat(OneDragonDocuments);

    /// <summary>
    /// 最近一次加载的结果说明，供首次进入的页面显示。
    /// </summary>
    public string? LoadMessage { get; private set; }

    /// <summary>
    /// 会话操作的结果或进度说明；页面自行决定是否显示。
    /// </summary>
    public event EventHandler<string>? StatusReported;

    /// <summary>
    /// 任一会话内文档的内容、选择或保存状态变化；sender 为发生变化的文档。
    /// </summary>
    public event EventHandler? DocumentChanged;

    /// <summary>
    /// 建立编辑会话；构造过程不读写文件。
    /// </summary>
    /// <param name="store">计划与预设存储。</param>
    /// <param name="clipboard">跨计划剪贴板。</param>
    /// <param name="taskService">任务服务。</param>
    /// <param name="resourceCatalog">资源索引。</param>
    public PuloniaPlanEditingSession(PuloniaTaskStore store, PuloniaTaskClipboardService clipboard,
        IPuloniaTaskService taskService, PuloniaTaskResourceCatalog resourceCatalog)
    {
        _store = store;
        Clipboard = clipboard;
        TaskService = taskService;
        ResourceCatalog = resourceCatalog;
        _repositoryManagementService = new PuloniaRepositoryManagementService(resourceCatalog.Repositories);
        _resourceVersionService = new PuloniaTaskResourceVersionService(resourceCatalog);
    }

    /// <summary>
    /// 首次加载计划、预设与展开状态；加载中或已成功时复用同一个任务，失败后允许重新加载。
    /// </summary>
    public Task InitializeAsync()
    {
        if (_initialization is { IsCompleted: true } && !IsInitialized)
            _initialization = null;
        return _initialization ??= InitializeCoreAsync();
    }

    /// <summary>
    /// 读取全部文档；空存储会建立一个等待自动保存的新普通计划。
    /// </summary>
    private async Task InitializeCoreAsync()
    {
        IsBusy = true;
        try
        {
            // 1. 读取设置方案、计划和编辑器展开状态；损坏的方案与计划文件都被跳过并提示，文件保留在磁盘上
            var profiles = await _store.ListProfilesAsync();
            _profiles = profiles.Items;
            var plans = await _store.ListPlansWithFailuresAsync();
            var viewState = await LoadViewStateAsync();

            // 2. 按用途建立文档；首次使用时补一份普通计划
            Documents.Clear();
            OneDragonDocuments.Clear();
            foreach (var plan in plans.Items)
            {
                viewState.CollapsedTaskIds.TryGetValue(plan.Id, out var collapsed);
                AddDocument(new PuloniaTaskPlanDocumentViewModel(plan, _profiles, Clipboard, isNew: false, collapsed));
            }
            IsInitialized = true;
            if (Documents.Count == 0)
                CreatePlanDocument("我的任务计划");
            var failures = plans.Failures.Concat(profiles.Failures).ToArray();
            var failureNotice = failures.Length == 0 ? null
                : $"有 {failures.Length} 个计划或设置方案文件无法读取，已跳过（文件未删除）：" + string.Join("；", failures.Select(item => item.ToString()));
            LoadMessage = string.Join("\n", new[] { TaskService.RecoveryNotice, failureNotice }.OfType<string>()) is { Length: > 0 } notice
                ? notice
                : $"已加载 {Documents.Count} 个计划和 {_profiles.Count} 个设置方案。";
            if (failureNotice is not null)
            {
                ReportStatus(failureNotice);
                await ThemedMessageBox.WarningAsync(failureNotice, "部分任务计划无法读取");
            }
        }
        catch (Exception ex)
        {
            LoadMessage = "加载失败：" + ex.Message;
            ReportStatus(LoadMessage);
            await ThemedMessageBox.ErrorAsync(LoadMessage, "无法加载任务计划");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 报告会话操作的结果或进度。
    /// </summary>
    /// <param name="message">面向用户的说明。</param>
    internal void ReportStatus(string message) => StatusReported?.Invoke(this, message);

    /// <summary>
    /// 新建带根分组的普通计划并加入自动保存队列。
    /// </summary>
    /// <param name="name">计划名称。</param>
    /// <param name="profileId">挂在计划根节点上的设置方案 ID；为空表示不使用方案。</param>
    /// <returns>新建的计划文档。</returns>
    internal PuloniaTaskPlanDocumentViewModel CreatePlanDocument(string name, string? profileId = null)
    {
        // 计划级方案挂在根节点上，与“计划共享设置”中选择方案的效果相同
        var plan = new PuloniaTaskPlan
        {
            Name = name,
            RootTask = new PuloniaTask { Name = name, TaskType = "group", ProfileId = profileId }
        };
        var document = new PuloniaTaskPlanDocumentViewModel(plan, _profiles, Clipboard, isNew: true);
        AddDocument(document);
        ScheduleAutoSave(document);
        return document;
    }

    /// <summary>创建一份独立的一条龙计划，默认节点各自拥有稳定 ID。</summary>
    /// <param name="name">配置名称。</param>
    /// <returns>新建的一条龙配置。</returns>
    internal PuloniaTaskPlanDocumentViewModel CreateOneDragonConfiguration(string name)
    {
        var document = new PuloniaTaskPlanDocumentViewModel(
            PuloniaOneDragonDefaults.CreatePlan(name, TaskService.Definitions), _profiles, Clipboard, isNew: true);
        AddDocument(document);
        ScheduleAutoSave(document);
        return document;
    }

    /// <summary>
    /// 把文档加入会话并订阅其编辑状态变化。
    /// </summary>
    /// <param name="document">计划文档。</param>
    private void AddDocument(PuloniaTaskPlanDocumentViewModel document)
    {
        document.SetDefinitions(TaskService.Definitions);
        if (document.Purpose == PuloniaTaskPlanPurpose.OneDragon) OneDragonDocuments.Add(document);
        else Documents.Add(document);
        document.Changed += OnDocumentChanged;
        document.ContentChanged += OnDocumentContentChanged;
        document.ViewStateChanged += OnDocumentViewStateChanged;
    }

    /// <summary>
    /// 解除文档事件订阅，只在文件删除成功后调用。
    /// </summary>
    /// <param name="document">已删除的计划文档。</param>
    private void DetachDocument(PuloniaTaskPlanDocumentViewModel document)
    {
        document.Changed -= OnDocumentChanged;
        document.ContentChanged -= OnDocumentContentChanged;
        document.ViewStateChanged -= OnDocumentViewStateChanged;
        Documents.Remove(document);
        OneDragonDocuments.Remove(document);
    }

    /// <summary>
    /// 文档变化时转发给页面刷新命令和触发器列表。
    /// </summary>
    private void OnDocumentChanged(object? sender, EventArgs e) => DocumentChanged?.Invoke(sender, e);

    /// <summary>
    /// 计划内容变化后刷新资源统计并安排自动保存；保存失败后等待下一次真实编辑再重试。
    /// </summary>
    private void OnDocumentContentChanged(object? sender, EventArgs e)
    {
        // 删除节点、修改路径或撤销后立即撤掉失效统计；磁盘版本由页面检查循环重新读取。
        UpdateResourceSummaries(GetResourceDocuments());
        NotifyResourceCommands();
        // 结构或共享设置变化可能改变路线 CD 规则，防抖后重新计算任务树标记。
        ScheduleCooldownRefresh();
        if (sender is PuloniaTaskPlanDocumentViewModel { IsDirty: true, LastSaveError: null } document)
            ScheduleAutoSave(document);
    }

    /// <summary>
    /// 重新安排指定计划的自动保存；短时间内的连续操作只保留最后一个延迟任务。
    /// </summary>
    /// <param name="document">计划文档。</param>
    internal void ScheduleAutoSave(PuloniaTaskPlanDocumentViewModel document)
    {
        if (!AllDocuments.Contains(document) || document.IsDeleted
            || !document.IsDirty || document.LastSaveError is not null)
            return;

        CancelPendingAutoSave(document);
        var cancellation = new CancellationTokenSource();
        _autoSaveDelays[document] = cancellation;
        _ = AutoSaveAfterDelayAsync(document, cancellation);
    }

    /// <summary>
    /// 等待防抖时间后保存计划；取消只终止尚未开始的延迟，不中断原子文件替换。
    /// </summary>
    private async Task AutoSaveAfterDelayAsync(PuloniaTaskPlanDocumentViewModel document,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(AutoSaveDelayMilliseconds, cancellation.Token);
            if (AllDocuments.Contains(document) && document.IsDirty)
                await SaveDocumentAsync(document);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 新操作已重新安排保存，本次延迟正常结束。
        }
        finally
        {
            if (_autoSaveDelays.TryGetValue(document, out var current)
                && ReferenceEquals(current, cancellation))
                _autoSaveDelays.Remove(document);
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// 取消指定计划尚处于防抖等待中的自动保存。
    /// </summary>
    private void CancelPendingAutoSave(PuloniaTaskPlanDocumentViewModel document)
    {
        if (_autoSaveDelays.Remove(document, out var cancellation))
            cancellation.Cancel();
    }

    /// <summary>
    /// 保存指定计划文档；自动保存、建立引用前的立即提交和运行前保存共用此串行入口。
    /// 同一文档的保存经文档自带的保存门排队，删除后排队中的保存直接退出。
    /// </summary>
    /// <param name="document">计划文档。</param>
    /// <returns>已保存或无需保存时返回 true。</returns>
    internal async Task<bool> SaveDocumentAsync(PuloniaTaskPlanDocumentViewModel document)
    {
        if (document.IsDeleted)
            return false;
        CancelPendingAutoSave(document);
        await document.SaveGate.WaitAsync();
        try
        {
            // 删除可能在本次等待保存门期间完成，必须在实际写盘前再次检查。
            if (document.IsDeleted)
                return false;
            if (!document.IsDirty)
                return true;

            // Store 会在首次让出线程前复制当前模型；保存期间发生的新编辑仍留在内存，并在本次完成后再次保存。
            var saved = await _store.SavePlanAsync(document.Plan);
            document.AcceptAutoSaved(saved);
            if (saved.Revision == 1)
                _ = PersistPlanOrderAsync();
            return true;
        }
        catch (Exception ex)
        {
            document.SetSaveError(ex.Message);
            var message = $"“{document.Name}”保存失败，修改仍保留在当前页面：{ex.Message}";
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "任务计划保存失败");
            return false;
        }
        finally
        {
            document.SaveGate.Release();
            NotifyResourceCommands();
        }
    }

    /// <summary>
    /// 用户明确确认后，从磁盘重新读取计划并丢弃其内存草稿。
    /// </summary>
    /// <param name="document">计划文档。</param>
    internal async Task ReloadDocumentAsync(PuloniaTaskPlanDocumentViewModel document)
    {
        if (document.IsDeleted || IsBusy)
            return;
        if (document.IsDirty)
        {
            var result = await ThemedMessageBox.ShowAsync(
                "重新加载会丢弃当前计划尚未保存的编辑，是否继续？",
                "重新加载任务计划", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
                return;
        }

        CancelPendingAutoSave(document);
        IsBusy = true;
        await document.SaveGate.WaitAsync();
        try
        {
            if (document.IsDeleted)
                return;
            var diskPlan = await _store.LoadPlanAsync(document.Id);
            if (diskPlan is null)
            {
                ReportStatus("该计划尚未保存，磁盘上没有可重新加载的版本。");
                ScheduleAutoSave(document);
                return;
            }
            document.ReplaceFromDisk(diskPlan);
            ReportStatus($"已从磁盘重新加载“{diskPlan.Name}”。");
        }
        catch (Exception ex)
        {
            var message = "重新加载失败，当前内容未变：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "任务计划重新加载失败");
            ScheduleAutoSave(document);
        }
        finally
        {
            document.SaveGate.Release();
            IsBusy = false;
        }
    }

    /// <summary>
    /// 整份计划操作只针对仍在会话中的文档，忙碌时不可操作。
    /// </summary>
    /// <param name="document">计划文档。</param>
    internal bool CanManagePlan(PuloniaTaskPlanDocumentViewModel? document)
        => !IsBusy && document is not null && AllDocuments.Contains(document) && !document.IsDeleted;

    /// <summary>
    /// 复制计划的当前草稿，以唯一名称加入同一列表并立即保存为独立计划。
    /// </summary>
    /// <param name="document">来源计划。</param>
    /// <returns>副本文档；复制失败返回 null。</returns>
    internal async Task<PuloniaTaskPlanDocumentViewModel?> CopyPlanAsync(PuloniaTaskPlanDocumentViewModel document)
    {
        if (!CanManagePlan(document))
            return null;
        IsBusy = true;
        try
        {
            var documents = document.Purpose == PuloniaTaskPlanPurpose.OneDragon ? OneDragonDocuments : Documents;
            var plan = PuloniaTaskJson.CopyPlan(document.Plan);
            var baseName = document.Name + " - 副本";
            var name = baseName;
            var suffix = 1;
            while (documents.Any(item => item.Name == name))
                name = baseName + " " + ++suffix;
            if (plan.RootTask.Name == plan.Name)
                plan.RootTask.Name = name;
            plan.Name = name;

            var copy = new PuloniaTaskPlanDocumentViewModel(plan, _profiles, Clipboard, isNew: true);
            AddDocument(copy);
            // 副本紧邻来源计划，便于连续复制后比较编辑内容。
            documents.Move(documents.IndexOf(copy), documents.IndexOf(document) + 1);
            if (await SaveDocumentAsync(copy))
                ReportStatus($"已复制为“{name}”。副本的定时和快捷键触发默认关闭，可在触发方式中启用。");
            return copy;
        }
        catch (Exception ex)
        {
            var message = "复制计划失败：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法复制任务计划");
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 确认后等待当前保存结束，再删除计划文件并移出编辑会话；失败时保留草稿。
    /// </summary>
    /// <param name="document">要删除的计划。</param>
    /// <returns>删除成功返回 true。</returns>
    internal async Task<bool> DeletePlanAsync(PuloniaTaskPlanDocumentViewModel document)
    {
        if (!CanManagePlan(document))
            return false;
        var answer = await ThemedMessageBox.ShowAsync(
            $"确定删除任务计划“{document.Name}”及其全部任务和触发配置？此操作无法撤销。\n"
            + "已有执行记录会保留；已开始的手动运行仍按提交时的内容继续执行。",
            "删除任务计划", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes || !CanManagePlan(document))
            return false;

        IsBusy = true;
        CancelPendingAutoSave(document);
        await document.SaveGate.WaitAsync();
        try
        {
            // 除磁盘检查外也检查未保存草稿，避免删除后其他文档的自动保存失败。
            var reference = AllDocuments.FirstOrDefault(other => !ReferenceEquals(other, document)
                && EnumeratePlanReferenceIds(other.Plan.RootTask).Contains(document.Id));
            if (reference is not null)
                throw new InvalidOperationException($"计划“{reference.Name}”仍引用此计划，请先移除引用后再删除。");
            await _store.DeletePlanAsync(document.Id, document.Revision);

            // 只有文件删除成功才解除事件订阅；排队中的保存通过删除标记退出，不会重新写盘。
            var hadCollapsedNodes = document.GetCollapsedNodeIds().Count > 0;
            document.IsDeleted = true;
            DetachDocument(document);
            UpdateResourceSummaries(GetResourceDocuments());
            ReportStatus($"已删除任务计划“{document.Name}”，执行记录已保留。");
            await PersistPlanOrderAsync();
            // 被删计划有折叠记录时才重写展开状态，其余情况无需写盘。
            if (hadCollapsedNodes)
                ScheduleViewStateSave();
            return true;
        }
        catch (Exception ex)
        {
            var message = "删除计划失败，当前内容已保留：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法删除任务计划");
            return false;
        }
        finally
        {
            document.SaveGate.Release();
            // 删除失败时，等待期间被取消的自动保存需要重新安排。
            if (!document.IsDeleted)
                ScheduleAutoSave(document);
            IsBusy = false;
        }
    }

    /// <summary>
    /// 在普通计划列表中移动文档，并把当前顺序作为独立界面元数据持久化。
    /// </summary>
    /// <param name="document">被移动的计划。</param>
    /// <param name="insertIndex">拖拽目标插入位置。</param>
    /// <returns>顺序确有变化时返回 true。</returns>
    internal bool MoveDocument(PuloniaTaskPlanDocumentViewModel document, int insertIndex)
    {
        var sourceIndex = Documents.IndexOf(document);
        if (sourceIndex < 0)
            return false;
        var targetIndex = Math.Clamp(insertIndex, 0, Documents.Count);
        if (sourceIndex < targetIndex)
            targetIndex--;
        if (sourceIndex == targetIndex)
            return false;

        Documents.Move(sourceIndex, targetIndex);
        _ = PersistPlanOrderAsync();
        return true;
    }

    /// <summary>
    /// 保存计划列表顺序；失败只影响下次启动的显示顺序，不回滚当前编辑会话。
    /// </summary>
    private async Task PersistPlanOrderAsync()
    {
        try
        {
            await _store.SavePlanOrderAsync(AllDocuments.Select(document => document.Id).ToArray());
        }
        catch (Exception ex)
        {
            var message = "计划内容未受影响，但列表顺序保存失败：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法保存计划顺序");
        }
    }

    /// <summary>
    /// 判断新增引用是否会通过任意打开计划间的引用关系回到来源计划。
    /// </summary>
    /// <param name="source">发起引用的计划。</param>
    /// <param name="target">被引用的计划。</param>
    internal bool WouldCreatePlanReferenceCycle(PuloniaTaskPlanDocumentViewModel source,
        PuloniaTaskPlanDocumentViewModel target)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return ReachesSource(target);

        bool ReachesSource(PuloniaTaskPlanDocumentViewModel current)
        {
            if (!visited.Add(current.Id))
                return false;
            foreach (var referencedId in EnumeratePlanReferenceIds(current.Plan.RootTask))
            {
                if (referencedId == source.Id)
                    return true;
                var referencedDocument = AllDocuments.FirstOrDefault(document => document.Id == referencedId);
                if (referencedDocument is not null && ReachesSource(referencedDocument))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 深度优先枚举任务树中的计划引用 ID。
    /// </summary>
    private static IEnumerable<string> EnumeratePlanReferenceIds(PuloniaTask task)
    {
        if (task.Source is { Kind: "plan", PlanId: { } planId })
            yield return planId;
        foreach (var child in task.Children)
        foreach (var referencedPlanId in EnumeratePlanReferenceIds(child))
            yield return referencedPlanId;
    }

    /// <summary>在明确节点位置写入创建窗口确认的完整任务；一条龙始终插入根清单。</summary>
    /// <param name="document">一条龙配置。</param>
    /// <param name="selected">当前选中的清单行，新任务插在其后；为空时追加到末尾。</param>
    /// <param name="task">待插入的任务。</param>
    internal void InsertOneDragonTask(PuloniaTaskPlanDocumentViewModel document, PuloniaTaskNodeViewModel? selected, PuloniaTask task)
    {
        if (!OneDragonDocuments.Contains(document) || document.IsDeleted) return;
        var root = document.RootNode;
        var index = selected is not null && ReferenceEquals(selected.Parent, root)
            ? root.Children.IndexOf(selected) + 1 : root.Children.Count;
        document.InsertNode(task, root, index);
        ReportStatus($"已添加“{task.Name}”。");
    }

    /// <summary>引用普通计划前保存草稿并检查循环；目标计划保持原菜单归属。</summary>
    /// <param name="document">一条龙配置。</param>
    /// <param name="selected">当前选中的清单行。</param>
    /// <param name="target">被引用的普通计划。</param>
    /// <exception cref="InvalidOperationException">目标不可引用或会形成循环。</exception>
    internal async Task AddOneDragonReferenceAsync(PuloniaTaskPlanDocumentViewModel document,
        PuloniaTaskNodeViewModel? selected, PuloniaTaskPlanDocumentViewModel target)
    {
        if (!Documents.Contains(target) || WouldCreatePlanReferenceCycle(document, target))
            throw new InvalidOperationException("此计划不可引用，或引用关系会形成循环。");
        if (!await SaveDocumentAsync(target)) return;
        InsertOneDragonTask(document, selected, new PuloniaTask
        {
            Name = target.Name, TaskType = "group",
            Source = new PuloniaTaskSource { Kind = "plan", PlanId = target.Id }
        });
    }

    /// <summary>导入确认后逐份原子保存并加入当前会话；部分失败保留已完成计划并由调用方反馈。</summary>
    /// <param name="result">导入预览确认后的计划。</param>
    internal async Task ImportPreparedAsync(PuloniaTaskImportResult result)
    {
        foreach (var plan in result.Plans)
        {
            var baseName = plan.Name; var suffix = 2;
            while (OneDragonDocuments.Any(d => d.Name == plan.Name)) plan.Name = baseName + $"（导入 {suffix++}）";
            // 归属来自导入入口，不以名称、任务类型或引用目标推断。
            plan.Purpose = PuloniaTaskPlanPurpose.OneDragon;
            var saved = await _store.SavePlanAsync(plan);
            AddDocument(new PuloniaTaskPlanDocumentViewModel(saved, _profiles, Clipboard, isNew: false));
        }
        await PersistPlanOrderAsync();
        await RefreshResourceVersionsAsync();
    }

    /// <summary>
    /// 当前计划是否可以提交运行。
    /// </summary>
    /// <param name="document">计划文档。</param>
    internal bool CanRunDocument(PuloniaTaskPlanDocumentViewModel? document) => !IsBusy && document is not null;

    /// <summary>
    /// 保存后在指定执行实例上提交运行；执行实例为空时在主实例本地执行。
    /// 提交时固定快照，随后编辑不会影响本次运行；页面执行入口默认不限时。
    /// </summary>
    /// <param name="document">要运行的计划。</param>
    /// <param name="executionTarget">执行实例；为空表示本机。</param>
    /// <returns>新运行 ID；未提交时返回 null。</returns>
    internal async Task<string?> RunPlanOnAsync(PuloniaTaskPlanDocumentViewModel document,
        PuloniaTaskExecutionTarget? executionTarget)
    {
        if (!CanRunDocument(document))
            return null;
        if (document.IsDirty && !await SaveDocumentAsync(document))
            return null;

        try
        {
            await RefreshResourceVersionsCoreAsync(null, CancellationToken.None, document);
            // 禁用资源仍显示更新标签，但不会阻止其他已启用任务执行。
            var pendingUpdates = GetReferencedDocuments(document, GetResourceDocuments(document), enabledOnly: true)
                .SelectMany(target => target.EnumerateNodes())
                .Count(node => node.IsEffectivelyEnabled && node.CanUpdateResourceVersion && node.HasResourceUpdate
                    && node.Model.Resource is null && node.Model.Source?.Resource is null);
            if (pendingUpdates > 0)
                throw new InvalidOperationException($"“{document.Name}”有 {pendingUpdates} 项启用中的资源更新，请先点击计划卡片的更新任务资源按钮。");
            var runId = await TaskService.EnqueueAsync(new PuloniaTaskRequest
            {
                PlanId = document.Id,
                Source = "ui",
                ExecutionTarget = executionTarget
            });
            ReportStatus(executionTarget is null
                ? $"已提交“{document.Name}”，将按提交顺序执行；之后的修改不影响本次运行。"
                : $"已提交“{document.Name}”到“{executionTarget.DisplayName}”执行；之后的修改不影响本次运行。");
            return runId;
        }
        catch (Exception ex)
        {
            var message = "提交运行失败：" + ex.Message;
            ReportStatus(message);
            await ThemedMessageBox.ErrorAsync(message, "无法运行任务计划");
            return null;
        }
    }



    /// <summary>
    /// 忙碌状态变化后刷新资源相关命令。
    /// </summary>
    partial void OnIsBusyChanged(bool value) => NotifyResourceCommands();

    /// <summary>
    /// 加载完成后刷新依赖加载状态的命令。
    /// </summary>
    partial void OnIsInitializedChanged(bool value) => NotifyResourceCommands();
}
