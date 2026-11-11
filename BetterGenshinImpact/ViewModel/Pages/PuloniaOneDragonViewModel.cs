using System;
using System.IO;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.Pulonia;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Wpf.Ui;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using BetterGenshinImpact.View.Controls;
using GongSolutions.Wpf.DragDrop;
namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>沿用老一条龙布局的计划包装；保存和执行仍由共用的 Pulonia 编辑会话与任务服务负责。</summary>
public partial class PuloniaOneDragonViewModel : ViewModel, IDropTarget
{
    /// <summary>与任务计划入口共享的编辑会话。</summary>
    public PuloniaPlanEditingSession Session { get; }
    /// <summary>唯一任务服务。</summary>
    private readonly IPuloniaTaskService _service;
    /// <summary>新增窗口使用的资源目录。</summary>
    private readonly PuloniaTaskResourceCatalog _catalog;
    /// <summary>计划和导入报告存储。</summary>
    private readonly PuloniaTaskStore _store;
    /// <summary>独立任务注册表；按描述符声明的设置界面创建专属设置 VM。</summary>
    private readonly SoloTaskRegistry _registry;
    /// <summary>调度宿主，为触发设置窗口提供运行反馈；测试直接构造时为空。</summary>
    private readonly PuloniaTaskTriggerHost? _triggerHost;
    /// <summary>共享设置、方案与路线 CD 窗口入口；测试直接构造时为空。</summary>
    private readonly IPuloniaTaskSettingsDialogs? _settingsDialogs;
    /// <summary>首次进入页面的加载任务；导航与 Loaded 并发进入时复用同一次加载，失败后下次进入重试。</summary>
    private Task? _pageLoad;
    /// <summary>合并高频进度查询，查询期间收到新事件后再读取一次。</summary>
    private readonly PuloniaCoalescedRefresh _statusRefresh;
    /// <summary>加载成功前禁用清单编辑，避免异步读盘覆盖用户操作。</summary>
    [ObservableProperty] private bool _isReady;
    /// <summary>未选择配置时的空任务清单。</summary>
    private readonly ObservableCollection<PuloniaTaskNodeViewModel> _emptyTasks = [];
    /// <summary>仅属于一条龙的配置列表。</summary>
    public ObservableCollection<PuloniaTaskPlanDocumentViewModel> Configurations => Session.OneDragonDocuments;
    /// <summary>当前配置的平面根清单，引用和分组均作为一行。</summary>
    public ObservableCollection<PuloniaTaskNodeViewModel> TaskList => SelectedConfiguration?.RootNode.Children ?? _emptyTasks;
    /// <summary>“添加任务”菜单：“独立任务”子菜单加上资源类与其他任务类型。</summary>
    public IReadOnlyList<PuloniaTaskMenuItemViewModel> AddTaskMenu { get; }
    /// <summary>当前配置，与普通任务计划菜单的选择互不影响。</summary>
    [ObservableProperty] private PuloniaTaskPlanDocumentViewModel? _selectedConfiguration;
    /// <summary>左侧选中的任务。</summary>
    [ObservableProperty] private PuloniaTaskNodeViewModel? _selectedTask;
    /// <summary>上次导入报告路径。</summary>
    [ObservableProperty] private string? _importReportPath;
    /// <summary>当前配置的活动运行 ID，独立于历史选择。</summary>
    private string? _activeRunId;
    /// <summary>活动运行是否为主实例下发到子实例的运行；是时停止经下发客户端请求子实例停止。</summary>
    private bool _activeRunIsDispatched;
    /// <summary>页面是否正在接收运行事件。</summary>
    private bool _isListening;
    /// <summary>当前配置运行摘要。</summary>
    [ObservableProperty] private string _executionStatusText = "尚未执行";
    /// <summary>是否有可停止的请求。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCurrentRunCommand))]
    private bool _hasActiveRun;
    /// <summary>
    /// “运行于”：主实例加在线子实例，与任务计划页共用同一组件；选子实例时整次下发，运行状态、历史与完成记录由该实例按它的运行账户保存。
    /// 只有主实例且有在线子实例时显示。
    /// </summary>
    public PuloniaRunTargetsViewModel RunTargets { get; }
    /// <summary>跨实例运行视图：最近状态与清单各行节点状态从合并后的运行读取，包括下发到子实例的运行。</summary>
    private readonly PuloniaRunOverview _runOverview;
    /// <summary>触发设置窗口的实例目录；测试直接构造时为空，窗口按会话建立只含本地目录的实例目录。</summary>
    private readonly PuloniaTriggerInstanceCatalog? _triggerInstances;
    // 所有实例都能提交运行：子实例界面提交的运行在本实例执行；只有主实例能经“运行于”下发到子实例。
    /// <summary>页面导航，用于跳转到账户页；测试直接构造时为空。</summary>
    private readonly INavigationService? _navigation;

    /// <summary>注入共用编辑会话，界面只维护选择和右侧设置区，设置修改即时写入计划。</summary>
    /// <param name="session">与任务计划入口共享的编辑会话</param>
    /// <param name="service">任务服务</param>
    /// <param name="catalog">资源目录</param>
    /// <param name="store">计划存储</param>
    /// <param name="registry">独立任务注册表</param>
    /// <param name="instances">多实例服务，提供运行实例选项</param>
    /// <param name="triggerHost">调度宿主，为触发设置窗口提供运行反馈</param>
    /// <param name="settingsDialogs">共享设置、方案与路线 CD 窗口入口；测试直接构造时为空</param>
    /// <param name="navigation">页面导航，用于跳转到账户页；测试直接构造时为空</param>
    /// <param name="accountsPage">账户页视图模型，“查看路线 CD”在导航前记下要查看的账户和配置；测试直接构造时为空</param>
    /// <param name="runOverview">跨实例运行视图；为空时按本实例建立（测试用）</param>
    /// <param name="triggerInstances">触发设置窗口的实例目录（执行实例选项与其他实例的游标）；测试直接构造时为空</param>
    public PuloniaOneDragonViewModel(PuloniaPlanEditingSession session, IPuloniaTaskService service,
        PuloniaTaskResourceCatalog catalog, PuloniaTaskStore store, SoloTaskRegistry registry,
        InstanceService? instances = null, PuloniaTaskTriggerHost? triggerHost = null,
        IPuloniaTaskSettingsDialogs? settingsDialogs = null, INavigationService? navigation = null,
        PuloniaAccountsPageViewModel? accountsPage = null, PuloniaRunOverview? runOverview = null,
        PuloniaTriggerInstanceCatalog? triggerInstances = null)
    {
        Session = session; _service = service; _catalog = catalog; _store = store; _registry = registry;
        _triggerHost = triggerHost; _settingsDialogs = settingsDialogs; _navigation = navigation;
        _accountsPage = accountsPage; _triggerInstances = triggerInstances;
        RunTargets = new PuloniaRunTargetsViewModel(session, instances);
        _runOverview = runOverview ?? new PuloniaRunOverview(service, store, session.AccountService.CurrentInstanceKey);
        AddTaskMenu = PuloniaTaskMenuBuilder.Build(service.Definitions);
        _statusRefresh = new PuloniaCoalescedRefresh(async () =>
        {
            if (_isListening) await RefreshExecutionStatusAsync();
        });
    }

    /// <summary>进入页面：接收运行事件并开始资源检查；首次进入时加载共用文档，并为一条龙建立默认配置。</summary>
    public override async Task OnNavigatedToAsync()
    {
        if (!_isListening)
        {
            _isListening = true; _service.RunChanged += OnRunChanged;
            if (Session.RunDispatcher is { } dispatcher) dispatcher.Changed += OnDispatchedRunsChanged;
        }
        // “运行于”订阅子实例上下线与账户变化，并重读各实例的当前账户（可能刚在其他实例中修改）
        var runTargetsRefresh = RunTargets.ActivateAsync();
        var ct = Session.StartResourceMonitor(this);
        // 首领讨伐的累计次数与“查看路线 CD”按统筹设计 5.6 选账户：先确保会话已读取账户目录与本实例当前账户
        await Session.EnsureCooldownAccountsAsync();
        await EnsurePageLoadedAsync();
        // 账户页等其他页面请求打开的配置：加载完成后选中
        if (Session.TakeOpenPlanRequest(Configurations) is { } requested)
            SelectedConfiguration = requested;
        await runTargetsRefresh;
        if (!_isListening || !IsReady) return;
        // 每次进入都重新读取账户目录：账户可能刚在账户页或其他实例中修改
        try { await RefreshGenshinAccountsAsync(); }
        catch (Exception ex) { AccountMessage = "账户目录读取失败，运行前请修复：" + ex.Message; }
        await RefreshExecutionStatusAsync();
        try { await Session.RefreshResourceVersionsAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 离开页面时取消版本检查，不弹出错误。
        }
    }

    /// <summary>复用首次加载任务；加载失败（会话未初始化）后允许下次进入重新加载。</summary>
    private Task EnsurePageLoadedAsync()
    {
        if (_pageLoad is { IsCompleted: true } && !IsReady)
            _pageLoad = null;
        return _pageLoad ??= LoadPageCoreAsync();
    }

    /// <summary>等待会话加载，然后补默认配置、选中首个配置；账户目录在每次进入页面时读取。</summary>
    private async Task LoadPageCoreAsync()
    {
        await Session.InitializeAsync();
        if (!Session.IsInitialized) return;
        if (Configurations.Count == 0) NewConfiguration();
        SelectedConfiguration ??= Configurations.FirstOrDefault();
        IsReady = true;
    }

    /// <summary>离开后解除页面事件并停止本页的资源检查，服务继续管理已经提交的运行。</summary>
    public override Task OnNavigatedFromAsync()
    {
        _isListening = false; _service.RunChanged -= OnRunChanged;
        if (Session.RunDispatcher is { } dispatcher) dispatcher.Changed -= OnDispatchedRunsChanged;
        RunTargets.Deactivate();
        Session.StopResourceMonitor(this);
        return base.OnNavigatedFromAsync();
    }
    /// <summary>下发到子实例的运行状态或连接变化：只调度一次状态查询，事件可能来自管道后台线程。</summary>
    private void OnDispatchedRunsChanged(object? sender, EventArgs e) => QueueStatusRefresh();
    /// <summary>页面 Loaded 是唯一的进入入口，通过 Behaviors 接入。</summary>
    [RelayCommand] private Task OpenPageAsync() => OnNavigatedToAsync();
    /// <summary>页面 Unloaded 是唯一的离开入口，通过 Behaviors 接入。</summary>
    [RelayCommand] private Task ClosePageAsync() => OnNavigatedFromAsync();

    /// <summary>选择配置后只展示其根清单，不触碰普通菜单的当前计划。</summary>
    partial void OnSelectedConfigurationChanged(PuloniaTaskPlanDocumentViewModel? oldValue, PuloniaTaskPlanDocumentViewModel? value)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnConfigurationPropertyChanged;
            oldValue.ContentChanged -= OnConfigurationContentChanged;
        }
        if (value is not null)
        {
            value.PropertyChanged += OnConfigurationPropertyChanged;
            // 高级设置、公共设置或撤销改写参数后，右侧设置区按最新有效值重建
            value.ContentChanged += OnConfigurationContentChanged;
        }
        OnPropertyChanged(nameof(TaskList));
        IsAccountPreparationSelected = false;
        NotifyAccountBindingChanged();
        SelectedTask = value?.SelectedNode is { IsRoot: false } node && ReferenceEquals(node.Parent, value.RootNode)
            ? node : TaskList.FirstOrDefault();
        // 切换时立即撤销旧请求的停止目标，异步查询返回前不能停止上一份配置。
        _activeRunId = null;
        _activeRunIsDispatched = false;
        HasActiveRun = false;
        ExecutionStatusText = value is null ? "尚未选择配置" : "正在读取运行状态…";
        QueueStatusRefresh();
    }
    /// <summary>撤销或重新载入重建节点时同步平面清单，避免持有已脱离文档的节点。</summary>
    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PuloniaTaskPlanDocumentViewModel.RootNode))
        {
            OnPropertyChanged(nameof(TaskList));
            NotifyAccountBindingChanged();
            QueueStatusRefresh();
        }
        else if (e.PropertyName == nameof(PuloniaTaskPlanDocumentViewModel.SelectedNode))
        {
            var node = SelectedConfiguration?.SelectedNode;
            SelectedTask = node is not null && TaskList.Contains(node) ? node : TaskList.FirstOrDefault();
        }
    }
    /// <summary>选中行后按该任务的当前有效参数重建右侧设置区；固定任务使用专属视图，其余走通用卡片。</summary>
    partial void OnSelectedTaskChanged(PuloniaTaskNodeViewModel? value)
    {
        if (value is not null) IsAccountPreparationSelected = false;
        if (SelectedConfiguration is { } document) document.SelectedNode = value;
        if (value is null) ClearSettingsSurfaces();
        else RefreshSettingsSurfaces(value);
        SettingsMessage = "";
    }
    /// <summary>以配置为单位创建独立计划和注册表声明的默认任务。</summary>
    [RelayCommand]
    private void NewConfiguration()
    {
        var name = "新一条龙配置"; var suffix = 1;
        while (Configurations.Any(d => d.Name == name)) name = "新一条龙配置 " + ++suffix;
        SelectedConfiguration = Session.CreateOneDragonConfiguration(name);
    }
    /// <summary>重命名当前配置，保持计划与任务身份。</summary>
    [RelayCommand]
    private void RenameConfiguration()
    {
        if (SelectedConfiguration is not { } document) return;
        var name = PromptDialog.Prompt("请输入配置名称。", "重命名一条龙配置", document.Name).Trim();
        if (!string.IsNullOrWhiteSpace(name)) document.Name = name;
    }
    /// <summary>复用会话的串行删除和引用检查，成功后选中剩余配置。</summary>
    [RelayCommand]
    private async Task DeleteConfigurationAsync()
    {
        if (SelectedConfiguration is not { } document) return;
        await Session.DeletePlanAsync(document);
        if (document.IsDeleted && ReferenceEquals(SelectedConfiguration, document))
            SelectedConfiguration = Configurations.FirstOrDefault();
    }
    /// <summary>从能力创建窗口添加任务，允许同一类型与资源重复出现。</summary>
    [RelayCommand]
    private async Task AddTask(string taskType)
    {
        if (SelectedConfiguration is not { } document) return;
        try
        {
            if (PuloniaSoloTaskExecutor.IsSoloTaskType(taskType)
                && _service.Definitions.FirstOrDefault(d => d.TaskType == taskType) is { } definition)
            {
                // 没有可填写参数的独立任务直接以显示名插入；有参数的使用独立任务新增弹窗，优先展示专属设置界面
                var task = new PuloniaTask { Name = definition.DisplayName, TaskType = taskType };
                if (PuloniaSoloTaskExecutor.HasEditableParameters(definition))
                {
                    var viewModel = CreateSoloTaskDialog(document, definition);
                    if (PuloniaSoloTaskCreationDialog.Show(viewModel, Application.Current.MainWindow) is not { } created) return;
                    task = created;
                }
                Session.InsertOneDragonTask(document, SelectedTask, task);
                SelectedTask = document.SelectedNode;
                return;
            }
            var dialog = new PuloniaOneDragonCreationDialog(_service.Definitions, _catalog, taskType) { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() != true || dialog.ViewModel.Result is not { } result) return;
            // 基础表单已内嵌在添加弹窗中；声明了 HTML 设置页的 JS 项目在插入前打开设置页面
            var resourceBaseDirectory = _service.Definitions.FirstOrDefault(d => d.TaskType == taskType)?.ResourceBaseDirectory;
            if (!await PuloniaJavaScriptTaskCreationSettings.EditAsync(result, resourceBaseDirectory,
                    basicFormEdited: true, Application.Current.MainWindow)) return;
            Session.InsertOneDragonTask(document, SelectedTask, result.Task);
            SelectedTask = document.SelectedNode;
        }
        catch (Exception ex) { ThemedMessageBox.Error("无法添加任务：" + ex.Message, "一条龙"); }
    }

    /// <summary>
    /// 为一条龙配置建立新增独立任务弹窗视图模型：插入位置为根清单，独立任务保持内置名称。
    /// </summary>
    /// <param name="document">一条龙配置。</param>
    /// <param name="definition">独立任务能力定义。</param>
    /// <returns>新增弹窗视图模型。</returns>
    private PuloniaSoloTaskCreationDialogViewModel CreateSoloTaskDialog(PuloniaTaskPlanDocumentViewModel document,
        PuloniaTaskDefinition definition)
        => new(definition, _registry,
            document.PreviewEffectiveParameters(new PuloniaTask { TaskType = definition.TaskType }, document.RootNode),
            canEditName: false);

    /// <summary>已有普通计划作为清单的一行引用，重复添加不改变目标归属；之后修改该计划会同步生效。</summary>
    [RelayCommand]
    private async Task AddPlanReferenceAsync()
    {
        if (SelectedConfiguration is not { } document) return;
        var picker = new System.Windows.Controls.ComboBox { ItemsSource = Session.Documents, DisplayMemberPath = "Name", MinWidth = 260, SelectedIndex = 0 };
        var dialog = new PromptDialog("选择要引用的任务计划；之后修改该计划会同步影响这里。", "引用已有任务计划", picker, null) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || picker.SelectedItem is not PuloniaTaskPlanDocumentViewModel target) return;
        try
        {
            await Session.AddOneDragonReferenceAsync(document, SelectedTask, target);
            // 保存目标期间用户可能切换配置；只更新仍在显示的来源配置，不能把它的节点选到另一份计划。
            if (ReferenceEquals(SelectedConfiguration, document)) SelectedTask = document.SelectedNode;
        }
        catch (Exception ex) { await ThemedMessageBox.ErrorAsync(ex.Message, "无法引用任务计划"); }
    }
    /// <summary>删除当前行，原文档负责撤销与自动保存。</summary>
    [RelayCommand]
    private void DeleteTask()
    {
        if (SelectedTask is not { } node) return;
        node.Document.RemoveNode(node);
        SelectedTask = TaskList.FirstOrDefault();
    }
    /// <summary>撤销已提交的最近编辑（设置区的连续输入合并为一步），重建时重新读取本配置设置。</summary>
    [RelayCommand]
    private void UndoConfiguration()
    {
        SelectedConfiguration?.UndoCommand.Execute(null);
    }
    /// <summary>
    /// 以当前配置在“运行于”选中的每个实例上各运行一次：主实例本地提交固定快照；子实例整次下发，由该实例固定快照并执行。
    /// 设置已即时写入计划，未生效的无效输入不阻止运行。子实例没有“运行于”选项，提交到本实例的队列。
    /// </summary>
    [RelayCommand]
    private async Task RunAsync()
    {
        if (SelectedConfiguration is not { } document) return;
        await RunTargets.SubmitAsync(document);
        await RefreshExecutionStatusAsync();
    }
    /// <summary>次级入口展示当前配置的定时和快捷键设置，与任务计划页共用同一触发配置界面。</summary>
    [RelayCommand]
    private void ShowTriggers()
    {
        if (SelectedConfiguration is not { } document) return;
        // 窗口专属的触发配置视图模型直接编辑共享文档，关闭时解除订阅；不复制页面或编辑会话。
        using var triggers = new PuloniaPlanTriggersViewModel(Session, _triggerHost, _triggerInstances) { Document = document };
        new PromptDialog("", "一条龙触发设置", new PuloniaTaskTriggerView { DataContext = triggers }, null)
        { Owner = Application.Current.MainWindow, Width = 1000, Height = 720, SizeToContent = SizeToContent.Manual }.ShowDialog();
    }
    /// <summary>次级入口展示当前配置的运行历史；窗口使用自己的记录实例，不改写任务计划页的记录范围。</summary>
    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        // 窗口只显示运行记录，关闭时解除订阅；路线 CD 通过“更多 → 查看路线 CD”在账户页查看
        // 与执行记录页一样显示所有实例的运行；主实例下发的运行可以从窗口中停止
        using var history = new PuloniaTaskHistoryViewModel(_service, _store, _runOverview, Session.RunDispatcher)
        {
            PlanFilterId = SelectedConfiguration?.Id
        };
        await history.RefreshAsync();
        new PromptDialog("", "一条龙执行记录", new PuloniaOneDragonHistoryView { DataContext = history }, null)
        { Owner = Application.Current.MainWindow, Width = 1100, Height = 750, SizeToContent = SizeToContent.Manual }.ShowDialog();
    }
    /// <summary>高级参数和来源保留为次级窗口，主配置使用老样式卡片。</summary>
    [RelayCommand]
    private void ConfigureNode(PuloniaTaskNodeViewModel? node)
    {
        if (node is null) return;
        SelectedTask = node;
        using var viewModel = new PuloniaOneDragonSettingsDialogViewModel(Session, node, _settingsDialogs);
        try { new PuloniaOneDragonSettingsDialog(viewModel) { Owner = Application.Current.MainWindow }.ShowDialog(); }
        finally
        {
            // 高级窗口可能改写参数 JSON、资源位置或共享设置；按最新有效参数重建，节点已被替换时由选择变化负责。
            if (ReferenceEquals(SelectedTask, node)) RefreshSettingsSurfaces(node);
        }
    }
    /// <summary>编辑选中步骤的共享设置；能力不使用共享设置时不打开。</summary>
    [RelayCommand]
    private void ConfigureNodeSharedSettings(PuloniaTaskNodeViewModel? node)
    {
        if (node is null || _settingsDialogs is null) return;
        if (node.IsLeaf && node.Document.GetSharedSettingUsage(node).Keys is not { Count: > 0 }) return;
        _settingsDialogs.EditSharedSettings(new PuloniaSharedSettingsEditorViewModel(
            new PuloniaNodeSharedSettingsTarget(node), Session, _settingsDialogs));
        if (ReferenceEquals(SelectedTask, node)) RefreshSettingsSurfaces(node);
    }
    /// <summary>编辑当前一条龙配置的配置级共享设置（根节点），会传给配置中的全部任务。</summary>
    [RelayCommand]
    private void ConfigurePlanSharedSettings()
    {
        if (SelectedConfiguration is not { } document || _settingsDialogs is null) return;
        _settingsDialogs.EditSharedSettings(new PuloniaSharedSettingsEditorViewModel(
            new PuloniaNodeSharedSettingsTarget(document.RootNode), Session, _settingsDialogs));
        if (SelectedTask is { } selected) RefreshSettingsSurfaces(selected);
    }
    /// <summary>打开设置方案管理。</summary>
    [RelayCommand]
    private void ManageSettingsProfiles()
    {
        if (_settingsDialogs is null) return;
        _settingsDialogs.ManageProfiles(Session, null);
    }
    /// <summary>后台状态事件只调度 UI 查询，不能直接跨线程修改绑定。</summary>
    private void OnRunChanged(object? sender, PuloniaTaskRunChangedEventArgs e) => QueueStatusRefresh();
    /// <summary>合并高频进度事件，避免重复历史查询堆积。</summary>
    private void QueueStatusRefresh() => _statusRefresh.Request();
    /// <summary>
    /// 只应用仍属于当前配置的运行状态。运行取自合并后的跨实例视图（本实例内存运行、其他实例运行状态与共享历史），
    /// 下发到子实例的运行同样按它的运行状态显示最近状态与清单各行节点状态；连接断开时提示“连接断开，状态未知”。
    /// </summary>
    private async Task RefreshExecutionStatusAsync()
    {
        var planId = SelectedConfiguration?.Id;
        try
        {
            var runs = (await _runOverview.ListAsync()).Runs.Where(r => r.PlanId == planId)
                .OrderByDescending(r => r.SubmittedAt).ToArray();
            if (!_isListening || planId != SelectedConfiguration?.Id) return;
            var localKey = Session.AccountService.CurrentInstanceKey;
            // 1. 所有实例的运行：执行中优先，其次排队，再其次最近一次
            var active = runs.FirstOrDefault(r => r.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling)
                ?? runs.FirstOrDefault(r => r.Status == PuloniaTaskRunStatus.Queued);
            var latest = active ?? runs.FirstOrDefault();
            // 2. 主实例下发到子实例的运行（列表按提交时间倒序）：合并视图中已有的按合并视图显示；
            //    合并视图中还没有的（子实例运行状态读不出等）按下发跟踪器显示：未结束的优先，都已结束时与最近一次比较提交时间
            var dispatched = Session.RunDispatcher?.ListRuns().Where(r => r.PlanId == planId).ToArray() ?? [];
            var trackedOnly = dispatched.Where(r => runs.All(view => view.RunId != r.RunId)).ToArray();
            var remote = trackedOnly.FirstOrDefault(r => r.IsActive) ?? trackedOnly.FirstOrDefault();
            var showRemote = remote is not null && (active is not null
                ? remote.IsActive && remote.SubmittedAt > active.SubmittedAt
                : remote.IsActive || latest is null || remote.SubmittedAt > latest.SubmittedAt);
            if (showRemote)
            {
                // 只有下发跟踪器中的状态与消息；连接断开时状态未知、不能远程停止
                _activeRunId = remote!.CanCancel ? remote.RunId : null;
                _activeRunIsDispatched = true;
                HasActiveRun = remote.CanCancel;
                foreach (var node in TaskList) node.UpdateOneDragonStatus(null);
                ExecutionStatusText = $"[{remote.InstanceName}] " + (remote.IsDisconnected
                    ? "连接断开，状态未知"
                    : PuloniaTaskHistoryText.Status(remote.Status) + " · " + remote.Message);
            }
            else
            {
                // 3. 停止：本实例的运行照常停止；主实例下发、连接正常的运行经下发客户端停止；其他实例自己的运行在该实例界面停止
                var tracked = latest is { IsHistorical: false }
                    ? dispatched.FirstOrDefault(r => r.RunId == latest.RunId && r.Instance == latest.Instance)
                    : null;
                var isLocal = active is not null && active.Instance == localKey;
                var canStopRemote = active is not null && !isLocal && tracked is { CanCancel: true };
                _activeRunId = isLocal || canStopRemote ? active!.RunId : null;
                _activeRunIsDispatched = canStopRemote;
                HasActiveRun = _activeRunId is not null;
                foreach (var node in TaskList) node.UpdateOneDragonStatus(latest);
                // 历史目录由所有实例共用：最近一次运行在其他实例执行时，状态里标明是哪个实例
                var instancePrefix = latest is not null && latest.Instance != localKey
                    ? $"[{tracked?.InstanceName ?? PuloniaInstanceKey.Describe(latest.Instance)}] " : "";
                ExecutionStatusText = latest is null
                    ? "尚未执行"
                    // 未归档的下发运行在连接断开后可能已变化，读到的运行状态只作参考
                    : tracked is { IsDisconnected: true }
                        ? instancePrefix + "连接断开，状态未知"
                        : instancePrefix + new PuloniaTaskRunItemViewModel(latest).StatusText + " · " + latest.Message;
            }
            // 运行状态变化时同步首领累计，子实例落盘的结果也能在当前设置页看到。
            if (FixedSettings is AutoBossSettingsViewModel boss && boss.RefreshProgressCommand.CanExecute(null))
                await boss.RefreshProgressCommand.ExecuteAsync(null);
        }
        catch (Exception ex) { if (_isListening) ExecutionStatusText = "状态读取失败：" + ex.Message; }
    }
    /// <summary>
    /// 停止当前配置的明确请求：本实例的运行等待执行器释放资源；下发到子实例的运行请求子实例停止，最终状态随后上报。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasActiveRun))]
    private async Task StopCurrentRunAsync()
    {
        if (_activeRunId is not { } runId) return;
        try
        {
            if (_activeRunIsDispatched && Session.RunDispatcher is { } dispatcher)
                await dispatcher.CancelAsync(runId);
            else
                await _service.CancelAsync(runId);
            await RefreshExecutionStatusAsync();
        }
        catch (Exception ex) { await ThemedMessageBox.ErrorAsync("停止失败：" + ex.Message, "一条龙"); }
    }
    /// <summary>拖拽仅允许当前根清单内的任务排序。</summary>
    public void DragOver(IDropInfo dropInfo)
    {
        dropInfo.Effects = DragDropEffects.None;
        if (dropInfo.Data is PuloniaTaskNodeViewModel node && TaskList.Contains(node))
        { dropInfo.Effects = DragDropEffects.Move; dropInfo.DropTargetAdorner = DropTargetAdorners.Insert; }
    }
    /// <summary>进入目标时复用相同合法性检查。</summary>
    public void DragEnter(IDropInfo dropInfo) => DragOver(dropInfo);
    /// <summary>离开目标不保留拖拽状态。</summary>
    public void DragLeave(IDropInfo dropInfo) { }
    /// <summary>以一次可撤销变更移动根清单行，不允许拖入分组。</summary>
    public void Drop(IDropInfo dropInfo)
    {
        if (SelectedConfiguration is not { } document || dropInfo.Data is not PuloniaTaskNodeViewModel node || !TaskList.Contains(node)) return;
        document.MoveNode(node, document.RootNode, dropInfo.InsertIndex); SelectedTask = node;
    }

    /// <summary>选择并预览旧配置，确认后仅写新存储及差异报告。</summary>
    [RelayCommand]
    private async Task ImportLegacyAsync()
    {
        var picker = new OpenFileDialog { Title = "导入旧配置组、一条龙或 d-v3 计划", Filter = "JSON 配置|*.json", Multiselect = true, InitialDirectory = Global.Absolute("User") };
        if (picker.ShowDialog(Application.Current.MainWindow) != true) return;
        PuloniaTaskImportResult? result = null;
        try
        {
            // 冻结当前业务配置，以免后台导入期间其他页面修改配置导致不同节点取值不一致。
            // 构造函数由测试直接调用，不增加配置参数，暂时读配置根（见 task-context-removal.md 第 5 节）
            var config = System.Text.Json.JsonSerializer.Deserialize<AllConfig>(System.Text.Json.JsonSerializer.Serialize(ConfigService.Config!, ConfigService.JsonOptions), ConfigService.JsonOptions)!;
            result = await Task.Run(() => new PuloniaTaskLegacyImporter(_service.Definitions, _catalog).PrepareAsync(picker.FileNames, config));
            var preview = new PuloniaOneDragonImportDialog(result) { Owner = Application.Current.MainWindow };
            if (preview.ShowDialog() != true || result.Plans.Count == 0) return;
            await Session.ImportPreparedAsync(result);
            SelectedConfiguration = Configurations.LastOrDefault();
            result.Differences.Add("写入完成：" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            Session.ReportStatus($"已导入 {result.Plans.Count} 个计划；原文件保留，差异报告可从导入按钮旁打开。");
        }
        catch (Exception ex)
        {
            result?.Differences.Add("写入未全部完成：" + ex.Message + "；已保存的计划保留在列表中，原文件不变。");
            await ThemedMessageBox.ErrorAsync("导入未全部完成：" + ex.Message, "导入旧数据");
        }
        finally
        {
            if (result is not null)
            {
                try
                {
                    var directory = Path.Combine(_store.RootDirectory, "imports"); Directory.CreateDirectory(directory);
                    ImportReportPath = await WriteImportReportAsync(directory, result.Report);
                }
                catch (Exception ex) { Session.ReportStatus("导入报告保存失败：" + ex.Message); ImportReportPath = null; }
            }
        }
    }

    /// <summary>
    /// 以 <c>yyyyMMdd-HHmmss.txt</c> 新建导入报告，同秒重名时依次追加 <c>-2</c>、<c>-3</c>，不覆盖已有报告。
    /// </summary>
    /// <param name="directory">imports 目录</param>
    /// <param name="report">报告正文</param>
    /// <returns>新报告的完整路径</returns>
    private static async Task<string> WriteImportReportAsync(string directory, string report)
    {
        var baseName = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var bytes = new UTF8Encoding(false).GetBytes(report);
        for (var index = 1; ; index++)
        {
            var path = Path.Combine(directory, (index == 1 ? baseName : baseName + "-" + index) + ".txt");
            FileStream stream;
            try { stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            await using (stream)
                await stream.WriteAsync(bytes);
            return path;
        }
    }

    /// <summary>打开可复制的上次导入报告，不执行导入或修改源文件。</summary>
    [RelayCommand]
    private void ShowImportReport()
    {
        if (ImportReportPath is null || !File.Exists(ImportReportPath)) return;
        var text = new System.Windows.Controls.TextBox { Text = File.ReadAllText(ImportReportPath), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
        new PromptDialog("", "导入差异报告", text, null) { Owner = Application.Current.MainWindow, Width = 850, Height = 650, SizeToContent = SizeToContent.Manual }.ShowDialog();
    }
}
