using System;
using System.IO;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.Pulonia;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using BetterGenshinImpact.View.Controls;
using GongSolutions.Wpf.DragDrop;
using Newtonsoft.Json.Linq;
using System.Threading;
namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>沿用老一条龙布局的计划包装；保存和执行仍由唯一 Pulonia 编辑与任务服务负责。</summary>
public partial class PuloniaOneDragonViewModel : ViewModel, IDropTarget
{
    /// <summary>与任务计划入口共享的完整编辑会话。</summary>
    public PuloniaTaskPlanViewModel Editor { get; }
    /// <summary>唯一任务服务。</summary>
    private readonly IPuloniaTaskService _service;
    /// <summary>新增窗口使用的资源目录。</summary>
    private readonly PuloniaTaskResourceCatalog _catalog;
    /// <summary>计划和导入报告存储。</summary>
    private readonly PuloniaTaskStore _store;
    /// <summary>合并导航和 Loaded 生命周期，避免加载过程中创建重复默认配置。</summary>
    private readonly SemaphoreSlim _pageLoadGate = new(1, 1);
    /// <summary>加载成功前禁用清单编辑，避免异步读盘覆盖用户操作。</summary>
    [ObservableProperty] private bool _isReady;
    /// <summary>未选择配置时的空任务清单。</summary>
    private readonly ObservableCollection<PuloniaTaskNodeViewModel> _emptyTasks = [];
    /// <summary>按所属文档和节点身份隔离草稿；不同计划允许使用相同节点 ID。</summary>
    private readonly Dictionary<(PuloniaTaskPlanDocumentViewModel Document, string NodeId), IReadOnlyList<PuloniaOneDragonParameterFieldViewModel>> _drafts = [];
    /// <summary>仅属于一条龙的配置列表。</summary>
    public ObservableCollection<PuloniaTaskPlanDocumentViewModel> Configurations => Editor.OneDragonDocuments;
    /// <summary>当前配置的平面根清单，引用和分组均作为一行。</summary>
    public ObservableCollection<PuloniaTaskNodeViewModel> TaskList => SelectedConfiguration?.RootNode.Children ?? _emptyTasks;
    /// <summary>当前配置，与普通任务计划菜单的选择互不影响。</summary>
    [ObservableProperty] private PuloniaTaskPlanDocumentViewModel? _selectedConfiguration;
    /// <summary>左侧选中的任务。</summary>
    [ObservableProperty] private PuloniaTaskNodeViewModel? _selectedTask;
    /// <summary>右侧当前任务的老样式设置卡片。</summary>
    [ObservableProperty] private IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> _settingsFields = [];
    /// <summary>当前配置应用结果。</summary>
    [ObservableProperty] private string _settingsMessage = "";
    /// <summary>上次导入报告路径。</summary>
    [ObservableProperty] private string? _importReportPath;
    /// <summary>当前配置的活动运行请求，独立于历史选择。</summary>
    private Guid? _activeRequestId;
    /// <summary>页面是否正在接收运行事件。</summary>
    private bool _isListening;
    /// <summary>合并高频进度查询。</summary>
    private bool _statusRefreshPending;
    /// <summary>查询期间收到新事件后再读取一次。</summary>
    private bool _statusRefreshAgain;
    /// <summary>当前配置运行摘要。</summary>
    [ObservableProperty] private string _executionStatusText = "尚未执行";
    /// <summary>是否有可停止的请求。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCurrentRunCommand))]
    private bool _hasActiveRun;

    /// <summary>注入共用编辑服务，界面仅维护选择与未提交的设置草稿。</summary>
    public PuloniaOneDragonViewModel(PuloniaTaskPlanViewModel editor, IPuloniaTaskService service,
        PuloniaTaskResourceCatalog catalog, PuloniaTaskStore store)
    { Editor = editor; _service = service; _catalog = catalog; _store = store; }

    /// <summary>首次进入时加载共用文档，并为一条龙建立默认配置。</summary>
    public override async Task OnNavigatedToAsync()
    {
        if (!_isListening) { _isListening = true; _service.RunChanged += OnRunChanged; }
        await _pageLoadGate.WaitAsync();
        try
        {
            if (!_isListening) return;
            await Editor.OnNavigatedToAsync();
            if (!_isListening || !Editor.IsInitialized) return;
            if (Configurations.Count == 0) NewConfiguration();
            SelectedConfiguration ??= Configurations.FirstOrDefault();
            IsReady = true;
            await RefreshExecutionStatusAsync();
        }
        finally { _pageLoadGate.Release(); }
    }
    /// <summary>离开后解除页面事件，服务继续管理已经提交的运行。</summary>
    public override Task OnNavigatedFromAsync()
    {
        _isListening = false; _service.RunChanged -= OnRunChanged;
        return Editor.OnNavigatedFromAsync();
    }
    /// <summary>通过 Behaviors 接入加载生命周期。</summary>
    [RelayCommand] private Task OpenPageAsync() => OnNavigatedToAsync();
    /// <summary>通过 Behaviors 接入卸载生命周期。</summary>
    [RelayCommand] private Task ClosePageAsync() => OnNavigatedFromAsync();

    /// <summary>选择配置后只展示其根清单，不触碰普通菜单的当前计划。</summary>
    partial void OnSelectedConfigurationChanged(PuloniaTaskPlanDocumentViewModel? oldValue, PuloniaTaskPlanDocumentViewModel? value)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnConfigurationPropertyChanged;
        if (value is not null) value.PropertyChanged += OnConfigurationPropertyChanged;
        OnPropertyChanged(nameof(TaskList));
        SelectedTask = value?.SelectedNode is { IsRoot: false } node && ReferenceEquals(node.Parent, value.RootNode)
            ? node : TaskList.FirstOrDefault();
        // 切换时立即撤销旧请求的停止目标，异步查询返回前不能停止上一份配置。
        _activeRequestId = null;
        HasActiveRun = false;
        ExecutionStatusText = value is null ? "尚未选择配置" : "正在读取运行状态…";
        QueueStatusRefresh();
    }
    /// <summary>撤销或重新载入重建节点时同步平面清单，避免持有已脱离文档的节点。</summary>
    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PuloniaTaskPlanDocumentViewModel.RootNode))
        {
            if (sender is PuloniaTaskPlanDocumentViewModel document) RemoveSettingsDrafts(document);
            OnPropertyChanged(nameof(TaskList));
            QueueStatusRefresh();
        }
        else if (e.PropertyName == nameof(PuloniaTaskPlanDocumentViewModel.SelectedNode))
        {
            var node = SelectedConfiguration?.SelectedNode;
            SelectedTask = node is not null && TaskList.Contains(node) ? node : TaskList.FirstOrDefault();
        }
    }
    /// <summary>选中行后显示其独立设置草稿。</summary>
    partial void OnSelectedTaskChanged(PuloniaTaskNodeViewModel? value)
    {
        if (SelectedConfiguration is { } document) document.SelectedNode = value;
        SettingsFields = value is null ? [] : GetSettingsDraft(value);
        SettingsMessage = "";
    }
    /// <summary>按所属文档和稳定节点 ID 保留草稿，禁止开关或名称刷新重建输入。</summary>
    private IReadOnlyList<PuloniaOneDragonParameterFieldViewModel> GetSettingsDraft(PuloniaTaskNodeViewModel node)
    {
        var values = node.Document.GetOneDragonParameters(node);
        if (_drafts.TryGetValue((node.Document, node.Id), out var cached)
            && (cached.All(field => field.MatchesOriginalValue(values[field.Name])) || cached.Any(field => field.HasInputChanges))) return cached;
        var definition = _service.Definitions.FirstOrDefault(d => d.TaskType == node.TaskType && d.ResourceId == node.Path)
            ?? _service.Definitions.FirstOrDefault(d => d.TaskType == node.TaskType && d.ResourceId is null);
        var fields = definition?.ParameterSchema["properties"] is JObject properties
            ? properties.Properties().Select(p => new PuloniaOneDragonParameterFieldViewModel(p.Name,
                p.Name == "weekday_overrides" ? PuloniaOneDragonSettingChoices.WeekdaySchema(properties, values, node.TaskType)
                    : PuloniaOneDragonSettingChoices.Decorate(p.Name, (JObject)p.Value, values[p.Name], node.TaskType), values[p.Name],
                definition.ParameterSchema["required"] is JArray required && required.Values<string>().Contains(p.Name))).ToArray()
            : [];
        return _drafts[(node.Document, node.Id)] = fields;
    }

    /// <summary>仅丢弃指定配置的草稿，撤销、重新加载或删除不能影响其他配置的输入。</summary>
    private void RemoveSettingsDrafts(PuloniaTaskPlanDocumentViewModel document)
    {
        foreach (var key in _drafts.Keys.Where(key => ReferenceEquals(key.Document, document)).ToArray())
            _drafts.Remove(key);
    }
    /// <summary>提交当前任务的变化字段，校验失败保留全部卡片输入。</summary>
    [RelayCommand]
    private void ApplySettings() => TryApplySettings();

    /// <summary>完整校验并提交设置，供运行与高级窗口复用。</summary>
    private bool TryApplySettings()
    {
        if (SelectedTask is not { } node) return true;
        try
        {
            var changes = new JObject();
            var current = node.Document.GetOneDragonParameters(node);
            foreach (var field in SettingsFields)
                if (field.HasChanges() && field.BuildValue() is { } value)
                {
                    if (!field.MatchesOriginalValue(current[field.Name]))
                        throw new InvalidOperationException("此项设置的参数来源已改变，请重置草稿后重新设置。");
                    changes[field.Name] = value;
                }
            node.Document.ApplyOneDragonParameters(node, changes);
            _drafts.Remove((node.Document, node.Id)); SettingsFields = GetSettingsDraft(node);
            SettingsMessage = "设置已应用，将自动保存。";
            return true;
        }
        catch (Exception ex) when (ex is FormatException or Newtonsoft.Json.JsonException or PuloniaTaskValidationException or InvalidOperationException)
        { SettingsMessage = "设置未应用：" + ex.Message; return false; }
    }
    /// <summary>放弃当前任务未提交的卡片输入，重新读取有效参数。</summary>
    [RelayCommand]
    private void ResetSettingsDraft()
    {
        if (SelectedTask is not { } node) return;
        _drafts.Remove((node.Document, node.Id));
        SettingsFields = GetSettingsDraft(node);
        SettingsMessage = "已重新读取当前任务设置。";
    }
    /// <summary>以配置为单位创建独立计划和默认八项任务。</summary>
    [RelayCommand]
    private void NewConfiguration()
    {
        var name = "新一条龙配置"; var suffix = 1;
        while (Configurations.Any(d => d.Name == name)) name = "新一条龙配置 " + ++suffix;
        SelectedConfiguration = Editor.CreateOneDragonConfiguration(name);
    }
    /// <summary>重命名当前配置，保持计划与任务身份。</summary>
    [RelayCommand]
    private void RenameConfiguration()
    {
        if (SelectedConfiguration is not { } document) return;
        var name = PromptDialog.Prompt("请输入配置名称。", "重命名一条龙配置", document.Name).Trim();
        if (!string.IsNullOrWhiteSpace(name)) document.Name = name;
    }
    /// <summary>复用串行删除和引用检查，成功后选中剩余配置。</summary>
    [RelayCommand]
    private async Task DeleteConfigurationAsync()
    {
        if (SelectedConfiguration is not { } document) return;
        await Editor.DeletePlanCommand.ExecuteAsync(document);
        if (document.IsDeleted)
        {
            RemoveSettingsDrafts(document);
            if (ReferenceEquals(SelectedConfiguration, document)) SelectedConfiguration = Configurations.FirstOrDefault();
        }
    }
    /// <summary>从能力创建窗口添加任务，允许同一类型与资源重复出现。</summary>
    [RelayCommand]
    private void AddTask(string taskType)
    {
        if (SelectedConfiguration is not { } document) return;
        try
        {
            var dialog = new PuloniaOneDragonCreationDialog(_service.Definitions, _catalog, taskType) { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() == true && dialog.ViewModel.Result is { } result)
            { Editor.InsertOneDragonTask(document, SelectedTask, result.Task); SelectedTask = document.SelectedNode; }
        }
        catch (Exception ex) { ThemedMessageBox.Error("无法添加任务：" + ex.Message, "一条龙"); }
    }
    /// <summary>已有普通计划作为清单的一行引用，重复添加不改变目标归属。</summary>
    [RelayCommand]
    private async Task AddPlanReferenceAsync()
    {
        if (SelectedConfiguration is not { } document) return;
        var picker = new System.Windows.Controls.ComboBox { ItemsSource = Editor.Documents, DisplayMemberPath = "Name", MinWidth = 260, SelectedIndex = 0 };
        var dialog = new PromptDialog("选择要加入的任务计划。", "加入已有计划", picker, null) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || picker.SelectedItem is not PuloniaTaskPlanDocumentViewModel target) return;
        try
        {
            await Editor.AddOneDragonReferenceAsync(document, SelectedTask, target);
            // 保存目标期间用户可能切换配置；只更新仍在显示的来源配置，不能把它的节点选到另一份计划。
            if (ReferenceEquals(SelectedConfiguration, document)) SelectedTask = document.SelectedNode;
        }
        catch (Exception ex) { await ThemedMessageBox.ErrorAsync(ex.Message, "无法加入计划"); }
    }
    /// <summary>删除当前行，原文档负责撤销与自动保存。</summary>
    [RelayCommand]
    private void DeleteTask()
    {
        if (SelectedTask is not { } node) return;
        node.Document.RemoveNode(node); _drafts.Remove((node.Document, node.Id));
        SelectedTask = TaskList.FirstOrDefault();
    }
    /// <summary>撤销已提交的最近编辑，重建时重新读取本配置设置，不先提交或校验未保存草稿。</summary>
    [RelayCommand]
    private void UndoConfiguration()
    {
        SelectedConfiguration?.UndoCommand.Execute(null);
    }
    /// <summary>以当前配置提交固定快照，只应用启用任务的草稿；关闭任务保留输入但不阻止运行。</summary>
    [RelayCommand]
    private async Task RunAsync()
    {
        if (SelectedConfiguration is not { } document) return;
        var selection = SelectedTask;
        foreach (var node in TaskList.ToArray())
        {
            if (!node.IsEffectivelyEnabled || !_drafts.ContainsKey((document, node.Id))) continue;
            SelectedTask = node;
            if (!TryApplySettings()) return;
        }
        SelectedTask = selection;
        await Editor.RunPlanCommand.ExecuteAsync(document);
        await RefreshExecutionStatusAsync();
    }
    /// <summary>次级入口展示当前配置的定时和热键设置。</summary>
    [RelayCommand]
    private void ShowTriggers()
    {
        if (SelectedConfiguration is not { } document) return;
        var previousFilter = Editor.History.PlanFilterId;
        var scoped = Editor.CreateOneDragonTriggerEditor(document, ShowHistoryAsync);
        try
        {
            new PromptDialog("", "一条龙触发设置", new PuloniaOneDragonTriggerView { DataContext = scoped }, null)
            { Owner = Application.Current.MainWindow, Width = 1000, Height = 720, SizeToContent = SizeToContent.Manual }.ShowDialog();
        }
        finally { scoped.CloseOneDragonTriggerEditor(); Editor.History.PlanFilterId = previousFilter; }
    }
    /// <summary>次级入口展示当前配置的运行历史，关闭后恢复普通菜单的过滤范围。</summary>
    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        var previous = Editor.History.PlanFilterId;
        try
        {
            Editor.History.PlanFilterId = SelectedConfiguration?.Id;
            await Editor.History.RefreshAsync();
            new PromptDialog("", "一条龙执行记录", new PuloniaOneDragonHistoryView { DataContext = Editor.History }, null)
            { Owner = Application.Current.MainWindow, Width = 1100, Height = 750, SizeToContent = SizeToContent.Manual }.ShowDialog();
        }
        finally { Editor.History.PlanFilterId = previous; }
    }
    /// <summary>高级参数和来源保留为次级窗口，主配置使用老样式卡片。</summary>
    [RelayCommand]
    private void ConfigureNode(PuloniaTaskNodeViewModel? node)
    {
        if (node is null) return;
        SelectedTask = node;
        if (!TryApplySettings()) return;
        try { new PuloniaOneDragonSettingsDialog(this) { Owner = Application.Current.MainWindow }.ShowDialog(); }
        finally { _drafts.Remove((node.Document, node.Id)); SettingsFields = GetSettingsDraft(node); }
    }
    /// <summary>公共设置仍按本节点覆盖提交，禁止写回旧全局任务配置。</summary>
    [RelayCommand]
    private void EditCommonSettings(string taskType)
    {
        if (SelectedTask is not { } node || taskType is not ("pathing" or "javascript" or "shell")) return;
        try
        {
            var document = node.Document;
            var original = PuloniaTaskCommonSettings.ToParameters(taskType,
                PuloniaTaskCommonSettings.FromParameters(taskType, document.GetCommonSettingsParameters(node, taskType)));
            var draft = new PuloniaOneDragonCommonSettingsViewModel(taskType, original);
            var dialog = new PuloniaOneDragonCommonSettingsDialog(draft) { Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow };
            if (dialog.ShowDialog() == true) document.ApplyCommonSettings(node, taskType, original, draft.Result ?? original, draft.RestoreInheritance);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or Newtonsoft.Json.JsonException
                                   or ArgumentException or InvalidOperationException or OverflowException)
        { ThemedMessageBox.Error("任务设置未应用：" + ex.Message, "任务设置"); }
    }
    /// <summary>后台状态事件只调度 UI 查询，不能直接跨线程修改绑定。</summary>
    private void OnRunChanged(object? sender, PuloniaTaskRunChangedEventArgs e) => QueueStatusRefresh();
    /// <summary>合并高频进度事件，避免重复历史查询堆积。</summary>
    private void QueueStatusRefresh()
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (!_isListening) return;
            if (_statusRefreshPending) { _statusRefreshAgain = true; return; }
            _statusRefreshPending = true;
            try { do { _statusRefreshAgain = false; await RefreshExecutionStatusAsync(); } while (_isListening && _statusRefreshAgain); }
            finally { _statusRefreshPending = false; }
        }));
    }
    /// <summary>只应用仍属于当前配置的运行状态。</summary>
    private async Task RefreshExecutionStatusAsync()
    {
        var planId = SelectedConfiguration?.Id;
        try
        {
            var runs = (await _service.ListRunsAsync()).Where(r => r.PlanId == planId).OrderByDescending(r => r.SubmittedAt).ToArray();
            if (!_isListening || planId != SelectedConfiguration?.Id) return;
            var active = runs.FirstOrDefault(r => r.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling)
                ?? runs.FirstOrDefault(r => r.Status == PuloniaTaskRunStatus.Queued);
            _activeRequestId = active?.RequestId; HasActiveRun = active is not null;
            var latest = active ?? runs.FirstOrDefault();
            foreach (var node in TaskList) node.UpdateOneDragonStatus(latest);
            ExecutionStatusText = latest is null ? "尚未执行" : new PuloniaTaskRunItemViewModel(latest).StatusText + " · " + latest.Message;
        }
        catch (Exception ex) { if (_isListening) ExecutionStatusText = "状态读取失败：" + ex.Message; }
    }
    /// <summary>停止当前配置的明确请求，并等待执行器释放资源。</summary>
    [RelayCommand(CanExecute = nameof(HasActiveRun))]
    private async Task StopCurrentRunAsync()
    {
        if (_activeRequestId is not { } requestId) return;
        try { await _service.CancelAsync(requestId); await RefreshExecutionStatusAsync(); }
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
            var config = System.Text.Json.JsonSerializer.Deserialize<AllConfig>(System.Text.Json.JsonSerializer.Serialize(TaskContext.Instance().Config, ConfigService.JsonOptions), ConfigService.JsonOptions)!;
            result = await Task.Run(() => new PuloniaTaskLegacyImporter(_service.Definitions, _catalog).PrepareAsync(picker.FileNames, config));
            var preview = new PuloniaOneDragonImportDialog(result) { Owner = Application.Current.MainWindow };
            if (preview.ShowDialog() != true || result.Plans.Count == 0) return;
            await Editor.ImportPreparedAsync(result);
            SelectedConfiguration = Configurations.LastOrDefault();
            result.Differences.Add("写入完成：" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            Editor.StatusMessage = $"已导入 {result.Plans.Count} 个计划；原文件保留，差异报告可从导入按钮旁打开。";
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
                    ImportReportPath = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".txt");
                    await File.WriteAllTextAsync(ImportReportPath, result.Report, new UTF8Encoding(false));
                }
                catch (Exception ex) { Editor.StatusMessage = "导入报告保存失败：" + ex.Message; ImportReportPath = null; }
            }
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
