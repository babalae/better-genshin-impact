using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.Pulonia;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// “任务计划”页面视图模型，组织计划列表选择、树编辑命令、跨计划复制和拖拽。
/// 文档集合、加载、自动保存、资源检查与运行提交由共用的编辑会话负责。
/// </summary>
public partial class PuloniaTaskPlanViewModel : ViewModel, IDropTarget
{
    /// <summary>独立任务注册表；新增与编辑弹窗据此优先使用独立任务的专属设置界面，为空时只使用通用设置卡片。</summary>
    private readonly SoloTaskRegistry? _soloTaskRegistry;

    /// <summary>共享设置、路线 CD 与运行预览窗口的入口；为空时这些窗口不可用。</summary>
    private readonly IPuloniaTaskSettingsDialogs? _settingsDialogs;

    /// <summary>首次加载结果是否已经显示，后续进入页面不再覆盖最近操作结果。</summary>
    private bool _hasShownLoadMessage;

    /// <summary>
    /// 与新一条龙页共用的编辑会话。
    /// </summary>
    public PuloniaPlanEditingSession Session { get; }

    /// <summary>
    /// 当前选中的计划文档。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskPlanDocumentViewModel? _selectedDocument;

    /// <summary>
    /// 编辑会话是否正在执行文件读写；两个页面共用同一忙碌状态。
    /// </summary>
    public bool IsBusy
    {
        get => Session.IsBusy;
        set => Session.IsBusy = value;
    }

    /// <summary>供页面判断加载是否成功，失败时不得创建并覆盖尚未加载的配置。</summary>
    internal bool IsInitialized => Session.IsInitialized;

    /// <summary>
    /// 页面底部的最近操作结果。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "使用工具栏“添加”或在任务列表中右键添加任务；所有修改都会自动保存。";

    /// <summary>
    /// 普通任务计划列表，即编辑会话中的普通计划集合。
    /// </summary>
    public ObservableCollection<PuloniaTaskPlanDocumentViewModel> Documents => Session.Documents;

    /// <summary>
    /// 当前计划可引用的其他计划。
    /// </summary>
    public ObservableCollection<PuloniaTaskPlanDocumentViewModel> ReferencePlans { get; } = [];

    /// <summary>
    /// 完整的“添加任务”菜单：“独立任务”子菜单加上资源类与其他任务类型。
    /// </summary>
    public IReadOnlyList<PuloniaTaskMenuItemViewModel> AddTaskMenu { get; }

    /// <summary>
    /// 只含独立任务的一层菜单，按分组插入分隔项；任务计划页的“添加独立任务”子菜单使用它。
    /// </summary>
    public IReadOnlyList<PuloniaTaskMenuItemViewModel> SoloTaskMenu { get; }

    /// <summary>
    /// 当前计划的定时与快捷键触发配置，“触发方式”页签绑定此视图模型。
    /// </summary>
    public PuloniaPlanTriggersViewModel TriggerSettings { get; }

    /// <summary>计划页当前页签，触发器可直达其最近执行记录。</summary>
    [ObservableProperty] private int _selectedPlanTabIndex;

    /// <summary>
    /// 剪贴板是否已有可粘贴子树。
    /// </summary>
    public bool CanPaste => Session.Clipboard.HasContent && SelectedDocument is not null;

    /// <summary>
    /// 当前是否存在可供引用的其他计划。
    /// </summary>
    public bool CanAddPlanReference => !IsBusy && SelectedDocument is not null && ReferencePlans.Count > 0;

    /// <summary>
    /// 建立任务计划页面视图模型。
    /// </summary>
    /// <param name="session">与一条龙页共用的编辑会话。</param>
    /// <param name="history">计划内执行记录。</param>
    /// <param name="triggerHost">调度宿主；为空时只显示触发配置。</param>
    /// <param name="soloTaskRegistry">独立任务注册表。</param>
    /// <param name="settingsDialogs">公共设置、路线 CD 与运行预览窗口入口。</param>
    /// <param name="cooldowns">“路线 CD”页签的总览；为空时按会话新建。</param>
    public PuloniaTaskPlanViewModel(PuloniaPlanEditingSession session, PuloniaTaskHistoryViewModel history,
        PuloniaTaskTriggerHost? triggerHost = null, SoloTaskRegistry? soloTaskRegistry = null,
        IPuloniaTaskSettingsDialogs? settingsDialogs = null, PuloniaCollectionCooldownOverviewViewModel? cooldowns = null)
    {
        Session = session;
        History = history;
        Cooldowns = cooldowns ?? new PuloniaCollectionCooldownOverviewViewModel(session);
        _soloTaskRegistry = soloTaskRegistry;
        _settingsDialogs = settingsDialogs;
        TriggerSettings = new PuloniaPlanTriggersViewModel(session, triggerHost);
        // 能力定义在服务建立后不再变化，菜单只需生成一次
        AddTaskMenu = PuloniaTaskMenuBuilder.Build(session.TaskService.Definitions);
        SoloTaskMenu = PuloniaTaskMenuBuilder.BuildSoloTaskMenu(session.TaskService.Definitions);
        Session.StatusReported += (_, message) => StatusMessage = message;
        Session.DocumentChanged += OnSessionDocumentChanged;
        Session.PropertyChanged += OnSessionPropertyChanged;
        Session.Documents.CollectionChanged += (_, _) => RefreshReferencePlans();
    }

    /// <summary>
    /// 进入页面时加载计划（首次）并设置当前计划的记录范围，开始定期检查资源；再次进入保留内存草稿。
    /// </summary>
    public override async Task OnNavigatedToAsync()
    {
        // 在首次异步加载前登记资源检查，避免加载期间离开后又启动后台监控。
        var ct = Session.StartResourceMonitor(this);
        // 任务树的路线 CD 标记只在本页可见时计算并每分钟刷新
        Session.StartCooldownMonitor(this);
        try
        {
            await InitializeAsync();
            ct.ThrowIfCancellationRequested();
            History.PlanFilterId = SelectedDocument?.Id;
            await History.RefreshAsync();
            // 计划加载完成后读取账本并计算任务树的路线 CD 标记
            await Session.ReloadCooldownLedgerAsync();
            await Session.RefreshResourceVersionsAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 离开页面或关闭窗口时取消版本检查，不弹出错误也不重新创建监控。
        }
    }

    /// <summary>离开页面时停止本页的资源检查，不改变已经保存的计划或历史。</summary>
    public override Task OnNavigatedFromAsync()
    {
        Session.StopResourceMonitor(this);
        Session.StopCooldownMonitor(this);
        return base.OnNavigatedFromAsync();
    }

    /// <summary>
    /// 页面 Loaded 是唯一的进入入口，兼容导航缓存重新进入页面。
    /// </summary>
    [RelayCommand]
    private Task OpenPageAsync() => OnNavigatedToAsync();

    /// <summary>页面 Unloaded 是唯一的离开入口，兼容导航缓存及窗口关闭。</summary>
    [RelayCommand]
    private Task ClosePageAsync() => OnNavigatedFromAsync();

    /// <summary>
    /// 等待编辑会话完成首次加载（并发进入共用同一次加载），然后选中第一个计划。
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        await Session.InitializeAsync();
        if (!Session.IsInitialized)
            return;
        if (SelectedDocument is null || !Documents.Contains(SelectedDocument))
            SelectedDocument = Documents.FirstOrDefault();
        if (!_hasShownLoadMessage && Session.LoadMessage is { } message)
        {
            StatusMessage = message;
            _hasShownLoadMessage = true;
        }
        NotifyCommandStates();
    }

    /// <summary>
    /// 弹窗填写名称并选择设置方案后新建计划，加入自动保存队列；取消时不创建。
    /// </summary>
    [RelayCommand]
    private void NewPlan()
    {
        // 1. 预填不重名的默认名称，方案选项取当前已加载的设置方案
        var baseName = "新任务计划";
        var suffix = 1;
        var name = baseName;
        while (Documents.Any(document => document.Name == name))
            name = baseName + " " + ++suffix;
        var viewModel = new PuloniaPlanCreationDialogViewModel(name, Session.Profiles);
        if (PuloniaPlanCreationDialog.Show(viewModel, Application.Current.MainWindow) is not { } creation)
            return;

        // 2. 所选方案作为计划级共享设置挂在根节点上
        SelectedDocument = Session.CreatePlanDocument(creation.Name, creation.ProfileId);
        StatusMessage = $"已新建计划“{creation.Name}”。";
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
    /// 用户明确确认后，从磁盘重新读取当前计划并丢弃其内存草稿。
    /// </summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (SelectedDocument is { } document)
            await Session.ReloadDocumentAsync(document);
    }

    /// <summary>
    /// 在显式右键目标位置新增普通分组。
    /// </summary>
    [RelayCommand]
    private void AddGroup(PuloniaTaskNodeViewModel? targetNode)
    {
        if (!TryGetInsertionPoint(targetNode, out var document, out var parent, out var index))
            return;
        var name = PromptDialog.Prompt("请先填写分组名称；取消不会创建占位节点。", "添加分组", "新分组").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        document.InsertNode(new PuloniaTask { Name = name, TaskType = "group" }, parent, index);
        StatusMessage = $"已添加分组“{name}”。";
    }

    /// <summary>
    /// 在指定节点位置新增独立任务。参数由 XAML 的 MultiBinding 组装为 [目标节点, 任务类型]。
    /// 没有可编辑参数的独立任务直接以显示名插入；有参数的打开新增弹窗，优先展示独立任务的专属设置界面。
    /// </summary>
    /// <param name="parameters">第 0 项为目标节点，第 1 项为独立任务的任务类型。</param>
    [RelayCommand]
    private void AddSoloTask(object[]? parameters)
    {
        // 1. 解析菜单传入的目标节点和任务类型；菜单尚未完成绑定时忽略
        if (parameters is not [PuloniaTaskNodeViewModel targetNode, string taskType, ..])
            return;
        var definition = Session.TaskService.Definitions.FirstOrDefault(item => item.TaskType == taskType);
        if (definition is null)
        {
            StatusMessage = $"没有注册任务类型 {taskType}。";
            return;
        }
        if (!TryGetInsertionPoint(targetNode, out var document, out var parent, out var index))
            return;

        // 2. 无参数的独立任务不弹窗，直接插入；参数保持为空，运行时使用能力默认值
        var task = new PuloniaTask { Name = definition.DisplayName, TaskType = definition.TaskType };
        if (PuloniaSoloTaskExecutor.HasEditableParameters(definition))
        {
            // 3. 有参数的独立任务按插入位置的继承值打开新增弹窗，确认后才写入计划
            var viewModel = new PuloniaSoloTaskCreationDialogViewModel(definition, _soloTaskRegistry,
                document.PreviewEffectiveParameters(task, parent));
            if (PuloniaSoloTaskCreationDialog.Show(viewModel, Application.Current.MainWindow) is not { } created)
                return;
            task = created;
        }
        document.InsertNode(task, parent, index);
        StatusMessage = $"已添加任务“{task.Name}”。";
    }

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
        if (targetNode is null)
            return;
        // 1. 选择资源和常用运行选项
        var creation = PuloniaTaskCreationDialog.Show(Session.TaskService.Definitions, Session.ResourceCatalog, taskType,
            owner: Application.Current.MainWindow);
        if (creation is null)
            return;
        var task = creation.Task;

        // 2. JS 项目声明了 settings_ui 时，与独立任务一致在添加后弹出设置窗口；取消则不添加
        if (creation.JavaScriptSettingItems.Count > 0)
        {
            var settingsViewModel = new PuloniaJsScriptSettingsDialogViewModel(task.Name, creation.JavaScriptSettingItems,
                task.Parameters["settings"] as JObject, isCreation: true);
            if (PuloniaJsScriptSettingsDialog.Show(settingsViewModel, Application.Current.MainWindow) is not { } settings)
                return;
            if (settings.Count > 0)
                task.Parameters["settings"] = settings;
            else
                task.Parameters.Remove("settings");
        }

        // 3. 确认后才一次插入任务树
        if (!TryGetInsertionPoint(targetNode, out var document, out var parent, out var index))
            return;
        document.InsertNode(task, parent, index);
        StatusMessage = $"已添加任务“{task.Name}”。";
    }

    /// <summary>
    /// 在独立弹窗中编辑任意可见任务节点；独立任务优先展示其专属设置界面。
    /// </summary>
    [RelayCommand]
    private void EditNodeConfiguration(PuloniaTaskNodeViewModel? node)
    {
        if (node is null || node.IsRoot)
            return;
        // 打开前选中被编辑节点，关闭后树上仍停留在该任务
        SelectedDocument = node.Document;
        node.Document.SelectedNode = node;
        var definition = Session.TaskService.Definitions.FirstOrDefault(item => item.TaskType == node.TaskType);
        var dialog = new PuloniaTaskNodeSettingsDialog(
            new PuloniaTaskNodeSettingsDialogViewModel(Session, node, definition, _soloTaskRegistry, _settingsDialogs))
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() == true)
            StatusMessage = $"已保存“{node.Name}”的配置。";
    }

    /// <summary>
    /// 打开节点（含计划根）的共享设置窗口；修改即时生效并向下传播，能力不使用共享设置的叶子不打开。
    /// </summary>
    [RelayCommand]
    private void EditNodeSharedSettings(PuloniaTaskNodeViewModel? node)
    {
        if (node is null || _settingsDialogs is null)
            return;
        if (node.IsLeaf && node.Document.GetSharedSettingUsage(node).Keys is not { Count: > 0 })
        {
            StatusMessage = $"“{node.Name}”这类任务不使用共享设置，请在“编辑配置”中修改任务参数。";
            return;
        }
        SelectedDocument = node.Document;
        _settingsDialogs.EditSharedSettings(new PuloniaSharedSettingsEditorViewModel(
            new PuloniaNodeSharedSettingsTarget(node), Session, _settingsDialogs));
    }

    /// <summary>打开计划级共享设置（计划根节点），会传给计划中的全部任务。</summary>
    [RelayCommand]
    private void EditPlanSharedSettings(PuloniaTaskPlanDocumentViewModel? document)
    {
        if ((document ?? SelectedDocument) is not { } target)
            return;
        EditNodeSharedSettings(target.RootNode);
    }

    /// <summary>打开设置方案管理。</summary>
    [RelayCommand]
    private void ManageSettingsProfiles()
    {
        if (_settingsDialogs is null)
            return;
        _settingsDialogs.ManageProfiles(Session, null);
    }

    /// <summary>
    /// 新增对其他计划的引用节点，不复制目标计划内容；目标计划之后的修改会同步生效。
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
        var dialog = new PromptDialog("选择要引用的任务计划；之后修改该计划会同步影响这里。", "引用已有任务计划", selector, null);
        if (dialog.ShowDialog() != true
            || selector.SelectedItem is not PuloniaTaskPlanDocumentViewModel referencePlan)
            return;
        var sourceDocument = targetNode.Document;

        if (Session.WouldCreatePlanReferenceCycle(sourceDocument, referencePlan))
        {
            StatusMessage = $"不能引用“{referencePlan.Name}”：两个计划会互相引用。";
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法引用任务计划");
            return;
        }

        // 引用必须指向已落盘版本；若目标仍在防抖窗口中，则立即完成它的自动保存，不再要求用户确认。
        if (referencePlan.IsDirty && !await Session.SaveDocumentAsync(referencePlan))
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
        var clipboard = Session.Clipboard;
        if (!TryGetInsertionPoint(targetNode, out var document, out var parent, out var index)
            || !clipboard.TryCreatePaste(out var source, out var pasted, out var effectiveParameters, out var outerSettings)
            || source is null || pasted is null)
            return;

        if (document.HasPasteInheritanceDifference(source, pasted, effectiveParameters, parent))
        {
            var result = await ThemedMessageBox.ShowAsync(
                $"复制内容来自“{clipboard.SourcePlanName}”，目标位置继承的共享设置会改变部分有效设置。\n\n" +
                "选择“是”：保留复制时的有效设置，不同的项写在粘贴的节点上。\n" +
                "选择“否”：使用目标位置的继承结果。\n选择“取消”：不粘贴。",
                "粘贴时发现设置差异", MessageBoxButton.YesNoCancel,
                ThemedMessageBox.MessageBoxIcon.Question, MessageBoxResult.Cancel);
            if (result == MessageBoxResult.Cancel)
                return;
            if (result == MessageBoxResult.Yes)
                document.PreserveCopiedSettings(pasted, outerSettings, parent);
        }

        document.InsertNode(pasted, parent, index);
        StatusMessage = "已粘贴；粘贴的任务是独立副本，修改不会影响原任务。";
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
    /// 将合法拖拽提交为一次可撤销的树移动，或调整计划列表顺序。
    /// </summary>
    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is PuloniaTaskPlanDocumentViewModel document)
        {
            if (Session.MoveDocument(document, dropInfo.InsertIndex))
            {
                SelectedDocument = document;
                StatusMessage = $"已调整“{document.Name}”的计划列表顺序。";
            }
            return;
        }

        if (dropInfo.Data is not PuloniaTaskNodeViewModel source
            || dropInfo.TargetItem is not PuloniaTaskNodeViewModel target
            || !TryResolveDropTarget(source, target, dropInfo.InsertPosition, out var parent, out var index))
            return;
        // 跨父节点移动可能改变继承的共享设置，有变化时显示非模态提示
        var settingsChange = source.Document.MoveNodeTrackingSettings(source, parent, index);
        ShowMoveSettingsNotice(settingsChange);
        StatusMessage = $"已移动“{source.Name}”。";
    }

    /// <summary>
    /// 当前计划切换后刷新可引用计划列表、记录范围、触发配置和命令状态。
    /// </summary>
    partial void OnSelectedDocumentChanged(PuloniaTaskPlanDocumentViewModel? oldValue,
        PuloniaTaskPlanDocumentViewModel? newValue)
    {
        RefreshReferencePlans();
        History.PlanFilterId = newValue?.Id;
        // 路线 CD 页签默认只看当前计划用到的路线，用户可改为全部计划
        Cooldowns.SetPlanFilter(newValue?.Id);
        NotifyCommandStates();
        TriggerSettings.Document = newValue;
        // 移动提示只针对当时的计划，切换计划后不再保留
        ShowMoveSettingsNotice(null);
    }

    /// <summary>
    /// 会话内文档变化时刷新粘贴与列表命令。
    /// </summary>
    private void OnSessionDocumentChanged(object? sender, EventArgs e) => NotifyCommandStates();

    /// <summary>
    /// 会话忙碌状态变化后同步页面属性和命令。
    /// </summary>
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PuloniaPlanEditingSession.IsBusy))
            return;
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommandStates();
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
    /// 通知源生成命令重新计算可执行状态。
    /// </summary>
    private void NotifyCommandStates()
    {
        // 文档的选中变化也经由此处通知，工具栏目标随之刷新
        NotifyToolbarTargets();
        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(CanAddPlanReference));
        OnPropertyChanged(nameof(CanRunPlan));
        AddPlanReferenceCommand.NotifyCanExecuteChanged();
        RunPlanCommand.NotifyCanExecuteChanged();
        PreviewRunCommand.NotifyCanExecuteChanged();
        CopyPlanCommand.NotifyCanExecuteChanged();
        DeletePlanCommand.NotifyCanExecuteChanged();
    }
}
