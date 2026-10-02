using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// “任务计划”页面视图模型，组织计划文档、树编辑命令、跨计划复制和拖拽。
/// </summary>
public partial class PuloniaTaskPlanViewModel : ViewModel, IDropTarget
{
    /// <summary>
    /// 连续编辑完成后等待写盘的时间，避免快速勾选或拖拽产生无意义的中间修订。
    /// </summary>
    private const int AutoSaveDelayMilliseconds = 350;

    /// <summary>
    /// 计划与预设的文件存储服务。
    /// </summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>
    /// 跨计划复制使用的进程内剪贴板。
    /// </summary>
    private readonly PuloniaTaskClipboardService _clipboard;

    /// <summary>
    /// 当前从存储加载的共享预设。
    /// </summary>
    private IReadOnlyList<PuloniaTaskPreset> _presets = [];

    /// <summary>
    /// 每个计划当前等待执行的自动保存延迟，用于合并短时间内的连续操作。
    /// </summary>
    private readonly Dictionary<PuloniaTaskPlanDocumentViewModel, CancellationTokenSource> _autoSaveDelays = [];

    /// <summary>
    /// 每个计划独立的保存门，确保同一计划的修订始终按顺序写入。
    /// </summary>
    private readonly Dictionary<PuloniaTaskPlanDocumentViewModel, SemaphoreSlim> _saveGates = [];

    /// <summary>
    /// 是否已经完成首次加载，避免导航缓存重复覆盖未保存草稿。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 当前选中的计划文档。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskPlanDocumentViewModel? _selectedDocument;

    /// <summary>
    /// 页面是否正在执行文件读写。
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// 页面底部的最近操作结果。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "步骤 2 仅编辑配置，不会启动游戏或执行任务。";

    /// <summary>
    /// 当前打开的计划编辑文档。
    /// </summary>
    public ObservableCollection<PuloniaTaskPlanDocumentViewModel> Documents { get; } = [];

    /// <summary>
    /// 当前计划可引用的其他计划。
    /// </summary>
    public ObservableCollection<PuloniaTaskPlanDocumentViewModel> ReferencePlans { get; } = [];

    /// <summary>
    /// 首版树编辑器允许直接新增的叶子任务类型。
    /// </summary>
    public IReadOnlyList<PuloniaTaskTypeOption> TaskTypes { get; } =
    [
        new("builtin.sample", "内置任务"),
        new("pathing", "地图追踪"),
        new("javascript", "JS 脚本"),
        new("keymouse", "录制回放"),
        new("shell", "Shell"),
        new("csharp", "进程内 C#")
    ];

    /// <summary>
    /// 剪贴板是否已有可粘贴子树。
    /// </summary>
    public bool CanPaste => _clipboard.HasContent && SelectedDocument is not null;

    /// <summary>
    /// 当前是否存在可供引用的其他计划。
    /// </summary>
    public bool CanAddPlanReference => !IsBusy && SelectedDocument is not null && ReferencePlans.Count > 0;

    /// <summary>
    /// 建立任务计划页面视图模型。
    /// </summary>
    public PuloniaTaskPlanViewModel(PuloniaTaskStore store, PuloniaTaskClipboardService clipboard)
    {
        _store = store;
        _clipboard = clipboard;
    }

    /// <summary>
    /// 页面首次导航时加载计划和预设，后续导航保留内存草稿。
    /// </summary>
    public override async Task OnNavigatedToAsync()
    {
        await InitializeAsync();
    }

    /// <summary>
    /// 首次加载存储内容；空存储会建立一个等待自动保存的新计划。
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (_isInitialized || IsBusy)
            return;
        IsBusy = true;
        try
        {
            _presets = await _store.ListPresetsAsync();
            var plans = await _store.ListPlansAsync();
            Documents.Clear();
            foreach (var plan in plans)
                AddDocument(new PuloniaTaskPlanDocumentViewModel(plan, _presets, _clipboard, isNew: false));
            if (Documents.Count == 0)
                AddDocument(CreateNewDocument("我的任务计划"));
            SelectedDocument = Documents[0];
            _isInitialized = true;
            if (SelectedDocument.IsDirty)
                ScheduleAutoSave(SelectedDocument);
            StatusMessage = $"已加载 {Documents.Count} 个计划和 {_presets.Count} 个共享预设。";
        }
        catch (Exception ex)
        {
            StatusMessage = "加载失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法加载任务计划");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>
    /// 新建计划并加入自动保存队列。
    /// </summary>
    [RelayCommand]
    private void NewPlan()
    {
        var baseName = "新任务计划";
        var suffix = 1;
        var name = baseName;
        while (Documents.Any(document => document.Name == name))
            name = baseName + " " + ++suffix;
        var document = CreateNewDocument(name);
        AddDocument(document);
        SelectedDocument = document;
        ScheduleAutoSave(document);
        StatusMessage = "已新建计划。";
    }

    /// <summary>
    /// 通过输入对话框重命名指定计划；稳定 ID、引用和历史定位均保持不变。
    /// </summary>
    [RelayCommand]
    private void RenamePlan(PuloniaTaskPlanDocumentViewModel? document)
    {
        if (document is null)
            return;
        var name = PromptDialog.Prompt("请输入新的计划名称。", "重命名任务计划", document.Name).Trim();
        if (string.IsNullOrWhiteSpace(name) || name == document.Name)
            return;
        SelectedDocument = document;
        document.Name = name;
        StatusMessage = $"计划已重命名为“{name}”。";
    }

    /// <summary>
    /// 保存指定计划文档；自动保存和建立计划引用前的立即提交共用此串行入口。
    /// </summary>
    private async Task<bool> SaveDocumentAsync(PuloniaTaskPlanDocumentViewModel document)
    {
        CancelPendingAutoSave(document);
        var saveGate = GetSaveGate(document);
        await saveGate.WaitAsync();
        try
        {
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
            StatusMessage = $"“{document.Name}”保存失败，草稿仍保留在当前编辑会话：{ex.Message}";
            await ThemedMessageBox.ErrorAsync(StatusMessage, "任务计划保存失败");
            return false;
        }
        finally
        {
            saveGate.Release();
            NotifyCommandStates();
        }
    }

    /// <summary>
    /// 用户明确确认后，从磁盘重新读取当前计划并丢弃其内存草稿。
    /// </summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        var document = SelectedDocument;
        if (document is null)
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
        var saveGate = GetSaveGate(document);
        IsBusy = true;
        await saveGate.WaitAsync();
        try
        {
            var diskPlan = await _store.LoadPlanAsync(document.Id);
            if (diskPlan is null)
            {
                StatusMessage = "该计划尚未保存，磁盘上没有可重新加载的版本。";
                ScheduleAutoSave(document);
                return;
            }
            document.ReplaceFromDisk(diskPlan);
            StatusMessage = $"已从磁盘重新加载“{diskPlan.Name}”。";
        }
        catch (Exception ex)
        {
            StatusMessage = "重新加载失败，当前草稿未变：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "任务计划重新加载失败");
            ScheduleAutoSave(document);
        }
        finally
        {
            saveGate.Release();
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>
    /// 在显式右键目标位置新增普通分组。
    /// </summary>
    [RelayCommand]
    private void AddGroup(PuloniaTaskNodeViewModel? targetNode)
    {
        if (!TryGetInsertionPoint(targetNode, out var document, out var parent, out var index))
            return;
        document.InsertNode(new PuloniaTask { Name = "新分组", TaskType = "group" }, parent, index);
        StatusMessage = "已添加分组。";
    }

    /// <summary>
    /// 在指定节点位置新增内置任务。
    /// </summary>
    [RelayCommand]
    private void AddBuiltinTask(PuloniaTaskNodeViewModel? targetNode)
        => AddTask(targetNode, "builtin.sample");

    /// <summary>
    /// 在指定节点位置新增地图追踪任务。
    /// </summary>
    [RelayCommand]
    private void AddPathingTask(PuloniaTaskNodeViewModel? targetNode)
        => AddTask(targetNode, "pathing");

    /// <summary>
    /// 在指定节点位置新增 JS 脚本任务。
    /// </summary>
    [RelayCommand]
    private void AddJavascriptTask(PuloniaTaskNodeViewModel? targetNode)
        => AddTask(targetNode, "javascript");

    /// <summary>
    /// 在指定节点位置新增录制回放任务。
    /// </summary>
    [RelayCommand]
    private void AddKeyMouseTask(PuloniaTaskNodeViewModel? targetNode)
        => AddTask(targetNode, "keymouse");

    /// <summary>
    /// 在指定节点位置新增 Shell 任务。
    /// </summary>
    [RelayCommand]
    private void AddShellTask(PuloniaTaskNodeViewModel? targetNode)
        => AddTask(targetNode, "shell");

    /// <summary>
    /// 在指定节点位置新增进程内 C# 任务。
    /// </summary>
    [RelayCommand]
    private void AddCSharpTask(PuloniaTaskNodeViewModel? targetNode)
        => AddTask(targetNode, "csharp");

    /// <summary>
    /// 按类型在显式目标节点处建立叶子任务。
    /// </summary>
    private void AddTask(PuloniaTaskNodeViewModel? targetNode, string taskType)
    {
        var selectedTaskType = TaskTypes.FirstOrDefault(option => option.TaskType == taskType);
        if (selectedTaskType is null
            || !TryGetInsertionPoint(targetNode, out var document, out var parent, out var index))
            return;
        var task = new PuloniaTask
        {
            Name = "新" + selectedTaskType.DisplayName,
            TaskType = selectedTaskType.TaskType,
            Path = selectedTaskType.TaskType is "pathing" or "javascript" or "keymouse" ? "未配置" : null
        };
        document.InsertNode(task, parent, index);
        StatusMessage = $"已添加{selectedTaskType.DisplayName}节点。";
    }

    /// <summary>
    /// 新增对其他计划的引用节点，不复制目标计划内容。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddPlanReference))]
    private async Task AddPlanReferenceAsync(PuloniaTaskNodeViewModel? targetNode)
    {
        if (targetNode is null)
            return;
        SelectedDocument = targetNode.Document;
        if (!CanAddPlanReference)
            return;

        // 引用目标是低频选择，按需弹出选择器，避免长期占用树表空间。
        var selector = new ComboBox
        {
            DisplayMemberPath = nameof(PuloniaTaskPlanDocumentViewModel.Name),
            ItemsSource = ReferencePlans,
            SelectedIndex = 0,
            MinWidth = 280
        };
        var dialog = new PromptDialog("选择要引用的计划。", "添加计划引用", selector, null);
        if (dialog.ShowDialog() != true
            || selector.SelectedItem is not PuloniaTaskPlanDocumentViewModel referencePlan)
            return;
        var sourceDocument = targetNode.Document;

        if (WouldCreatePlanReferenceCycle(sourceDocument, referencePlan))
        {
            StatusMessage = $"不能引用“{referencePlan.Name}”：这会形成计划引用循环。";
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法添加计划引用");
            return;
        }

        // 引用必须指向已落盘版本；若目标仍在防抖窗口中，则立即完成它的自动保存，不再要求用户确认。
        if (referencePlan.IsDirty && !await SaveDocumentAsync(referencePlan))
            return;

        // 保存引用目标期间用户仍可能切换计划；插入位置始终以发起右键操作的节点为准。
        if (!TryGetInsertionPoint(targetNode, out var document, out var parent, out var index))
            return;
        var reference = new PuloniaTask
        {
            Name = "引用：" + referencePlan.Name,
            TaskType = "group",
            Source = new PuloniaTaskSource { Kind = "plan", PlanId = referencePlan.Id }
        };
        document.InsertNode(reference, parent, index);
        StatusMessage = $"已引用计划“{referencePlan.Name}”。";
    }

    /// <summary>
    /// 把进程内剪贴板子树粘贴到显式目标位置，并在继承变化时询问处理方式。
    /// </summary>
    [RelayCommand]
    private async Task PasteAsync(PuloniaTaskNodeViewModel? targetNode)
    {
        if (!TryGetInsertionPoint(targetNode, out var document, out var parent, out var index)
            || !_clipboard.TryCreatePaste(out var source, out var pasted, out var effectiveParameters)
            || source is null || pasted is null)
            return;

        if (document.HasPasteInheritanceDifference(source, pasted, effectiveParameters, parent))
        {
            var result = await ThemedMessageBox.ShowAsync(
                $"复制内容来自“{_clipboard.SourcePlanName}”，目标位置的父组继承会改变部分有效参数。\n\n" +
                "选择“是”：把复制时已有的有效值写入新节点；目标位置额外继承项仍保留。\n" +
                "选择“否”：使用目标位置的继承结果。\n选择“取消”：不粘贴。",
                "粘贴时发现配置差异", MessageBoxButton.YesNoCancel,
                ThemedMessageBox.MessageBoxIcon.Question, MessageBoxResult.Cancel);
            if (result == MessageBoxResult.Cancel)
                return;
            if (result == MessageBoxResult.Yes)
                PuloniaTaskPlanDocumentViewModel.PreserveCopiedEffectiveParameters(source, pasted, effectiveParameters);
        }

        document.InsertNode(pasted, parent, index);
        StatusMessage = "已粘贴子树；全部节点已生成新 ID，预设引用和 JSON 参数类型保持不变。";
    }

    /// <summary>
    /// 刷新预设文件并立即更新所有打开文档的预设选择和来源预览。
    /// </summary>
    [RelayCommand]
    private async Task RefreshPresetsAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            _presets = await _store.ListPresetsAsync();
            foreach (var document in Documents)
                document.SetPresets(_presets);
            StatusMessage = $"已刷新 {_presets.Count} 个共享预设。";
        }
        catch (Exception ex)
        {
            StatusMessage = "刷新预设失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法刷新共享预设");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    /// <summary>
    /// 拖拽进入目标时复用 DragOver 的完整合法性判断。
    /// </summary>
    public void DragEnter(IDropInfo dropInfo) => DragOver(dropInfo);

    /// <summary>
    /// 检查树拖拽目标，禁止根节点移动、跨文档移动和拖入自身后代。
    /// </summary>
    public void DragOver(IDropInfo dropInfo)
    {
        dropInfo.Effects = DragDropEffects.None;
        if (dropInfo.Data is PuloniaTaskPlanDocumentViewModel sourceDocument)
        {
            if (Documents.Contains(sourceDocument)
                && (dropInfo.TargetItem is null or PuloniaTaskPlanDocumentViewModel))
            {
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            }
            return;
        }

        if (dropInfo.Data is not PuloniaTaskNodeViewModel source || source.IsRoot
            || dropInfo.TargetItem is not PuloniaTaskNodeViewModel target
            || !ReferenceEquals(source.Document, target.Document)
            || !TryResolveDropTarget(source, target, dropInfo.InsertPosition, out _, out _))
            return;

        dropInfo.Effects = DragDropEffects.Move;
        dropInfo.DropTargetAdorner = target.CanAcceptChildren
                                     && dropInfo.InsertPosition == RelativeInsertPosition.TargetItemCenter
            ? DropTargetAdorners.Highlight
            : DropTargetAdorners.Insert;
    }

    /// <summary>
    /// 离开潜在目标时无需保留额外拖拽状态。
    /// </summary>
    public void DragLeave(IDropInfo dropInfo)
    {
    }

    /// <summary>
    /// 将合法拖拽提交为一次可撤销的树移动。
    /// </summary>
    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is PuloniaTaskPlanDocumentViewModel document)
        {
            MoveDocument(document, dropInfo.InsertIndex);
            return;
        }

        if (dropInfo.Data is not PuloniaTaskNodeViewModel source
            || dropInfo.TargetItem is not PuloniaTaskNodeViewModel target
            || !TryResolveDropTarget(source, target, dropInfo.InsertPosition, out var parent, out var index))
            return;
        source.Document.MoveNode(source, parent, index);
        StatusMessage = $"已移动“{source.Name}”；节点 ID 和子项启用选择保持不变。";
    }

    /// <summary>
    /// 当前计划切换后刷新可引用计划列表和命令状态。
    /// </summary>
    partial void OnSelectedDocumentChanged(PuloniaTaskPlanDocumentViewModel? oldValue,
        PuloniaTaskPlanDocumentViewModel? newValue)
    {
        RefreshReferencePlans();
        NotifyCommandStates();
    }

    /// <summary>
    /// 忙碌状态变化后刷新保存命令。
    /// </summary>
    partial void OnIsBusyChanged(bool value) => NotifyCommandStates();

    /// <summary>
    /// 建立一个带根分组的新计划文档。
    /// </summary>
    private PuloniaTaskPlanDocumentViewModel CreateNewDocument(string name)
    {
        var plan = new PuloniaTaskPlan
        {
            Name = name,
            RootTask = new PuloniaTask { Name = name, TaskType = "group" }
        };
        return new PuloniaTaskPlanDocumentViewModel(plan, _presets, _clipboard, isNew: true);
    }

    /// <summary>
    /// 把文档加入页面并订阅其编辑状态变化。
    /// </summary>
    private void AddDocument(PuloniaTaskPlanDocumentViewModel document)
    {
        Documents.Add(document);
        _saveGates.Add(document, new SemaphoreSlim(1, 1));
        document.Changed += OnDocumentChanged;
        document.ContentChanged += OnDocumentContentChanged;
        RefreshReferencePlans();
    }

    /// <summary>
    /// 文档变化时刷新跨文档命令和页面状态。
    /// </summary>
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CanPaste));
        NotifyCommandStates();
    }

    /// <summary>
    /// 计划内容变化后安排自动保存；保存失败后等待下一次真实编辑再重试。
    /// </summary>
    private void OnDocumentContentChanged(object? sender, EventArgs e)
    {
        if (sender is PuloniaTaskPlanDocumentViewModel { IsDirty: true, LastSaveError: null } document)
            ScheduleAutoSave(document);
    }

    /// <summary>
    /// 重新安排指定计划的自动保存；短时间内的连续操作只保留最后一个延迟任务。
    /// </summary>
    private void ScheduleAutoSave(PuloniaTaskPlanDocumentViewModel document)
    {
        if (!Documents.Contains(document) || !document.IsDirty || document.LastSaveError is not null)
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
            if (Documents.Contains(document) && document.IsDirty)
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
    /// 取得指定计划的串行保存门。
    /// </summary>
    private SemaphoreSlim GetSaveGate(PuloniaTaskPlanDocumentViewModel document)
    {
        if (_saveGates.TryGetValue(document, out var saveGate))
            return saveGate;
        saveGate = new SemaphoreSlim(1, 1);
        _saveGates.Add(document, saveGate);
        return saveGate;
    }

    /// <summary>
    /// 依据显式目标节点确定新增位置：分组内追加，叶子后插入。
    /// </summary>
    private bool TryGetInsertionPoint(PuloniaTaskNodeViewModel? targetNode,
        out PuloniaTaskPlanDocumentViewModel document,
        out PuloniaTaskNodeViewModel parent, out int index)
    {
        document = null!;
        parent = null!;
        index = 0;
        if (targetNode is null || !Documents.Contains(targetNode.Document))
            return false;

        document = targetNode.Document;
        var root = document.RootNode;
        SelectedDocument = document;
        document.SelectedNode = targetNode;
        if (targetNode.CanAcceptChildren)
        {
            parent = targetNode;
            index = targetNode.Children.Count;
            return true;
        }
        if (targetNode.Parent is { } targetParent)
        {
            var targetIndex = targetParent.Children.IndexOf(targetNode);
            if (targetIndex < 0)
                return false;
            parent = targetParent;
            index = targetIndex + 1;
            return true;
        }
        parent = root;
        index = root.Children.Count;
        return true;
    }

    /// <summary>
    /// 将拖拽相对位置解析为目标父节点和插入索引。
    /// </summary>
    private static bool TryResolveDropTarget(PuloniaTaskNodeViewModel source, PuloniaTaskNodeViewModel target,
        RelativeInsertPosition position, out PuloniaTaskNodeViewModel parent, out int index)
    {
        parent = null!;
        index = 0;
        if (ReferenceEquals(source, target) || source.ContainsDescendant(target))
            return false;
        if (target.CanAcceptChildren && position == RelativeInsertPosition.TargetItemCenter)
        {
            parent = target;
            index = target.Children.Count;
            return true;
        }
        if (target.Parent is null)
            return false;
        parent = target.Parent;
        if (!parent.CanAcceptChildren && !ReferenceEquals(source.Parent, parent))
            return false;
        index = parent.Children.IndexOf(target);
        if (position is RelativeInsertPosition.AfterTargetItem or RelativeInsertPosition.TargetItemCenter)
            index++;
        return index >= 0;
    }

    /// <summary>
    /// 重建可引用计划列表，并避免默认引用当前计划。
    /// </summary>
    private void RefreshReferencePlans()
    {
        ReferencePlans.Clear();
        foreach (var document in Documents.Where(document => !ReferenceEquals(document, SelectedDocument)))
            ReferencePlans.Add(document);
        OnPropertyChanged(nameof(CanAddPlanReference));
        AddPlanReferenceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 判断新增引用是否会通过任意打开计划间的引用关系回到来源计划。
    /// </summary>
    private bool WouldCreatePlanReferenceCycle(PuloniaTaskPlanDocumentViewModel source,
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
                var referencedDocument = Documents.FirstOrDefault(document => document.Id == referencedId);
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

    /// <summary>
    /// 在计划列表中移动文档，并把当前顺序作为独立界面元数据持久化。
    /// </summary>
    private void MoveDocument(PuloniaTaskPlanDocumentViewModel document, int insertIndex)
    {
        var sourceIndex = Documents.IndexOf(document);
        if (sourceIndex < 0)
            return;
        var targetIndex = Math.Clamp(insertIndex, 0, Documents.Count);
        if (sourceIndex < targetIndex)
            targetIndex--;
        if (sourceIndex == targetIndex)
            return;

        Documents.Move(sourceIndex, targetIndex);
        SelectedDocument = document;
        StatusMessage = $"已调整“{document.Name}”的计划列表顺序。";
        _ = PersistPlanOrderAsync();
    }

    /// <summary>
    /// 保存当前计划列表顺序；失败只影响下次启动的显示顺序，不回滚当前编辑会话。
    /// </summary>
    private async Task PersistPlanOrderAsync()
    {
        try
        {
            await _store.SavePlanOrderAsync(Documents.Select(document => document.Id).ToArray());
        }
        catch (Exception ex)
        {
            StatusMessage = "计划内容未受影响，但列表顺序保存失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法保存计划顺序");
        }
    }

    /// <summary>
    /// 通知源生成命令重新计算可执行状态。
    /// </summary>
    private void NotifyCommandStates()
    {
        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(CanAddPlanReference));
        AddPlanReferenceCommand.NotifyCanExecuteChanged();
    }
}
