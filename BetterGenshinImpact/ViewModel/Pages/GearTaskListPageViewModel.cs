using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.GearTask;
using BetterGenshinImpact.Service.GearTask.Execution;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.GearTask;
using BetterGenshinImpact.ViewModel.Pages.Component;
using BetterGenshinImpact.ViewModel.Pages.View;
using BetterGenshinImpact.ViewModel.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;
using Wpf.Ui.Violeta.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 任务列表页面 ViewModel。
/// </summary>
public partial class GearTaskListPageViewModel : ViewModel
{
    private readonly ILogger<GearTaskListPageViewModel> _logger;
    private readonly GearTaskStorageService _storageService;
    private readonly IGearTaskHistoryStore _historyStore;
    private readonly GearTaskExecutor _taskExecutor;

    /// <summary>
    /// 任务定义列表（左侧）。
    /// </summary>
    [ObservableProperty] private ObservableCollection<GearTaskDefinitionViewModel> _taskDefinitions = new();

    /// <summary>
    /// 当前选中的任务定义。
    /// </summary>
    [ObservableProperty] private GearTaskDefinitionViewModel? _selectedTaskDefinition;

    /// <summary>
    /// 当前任务树根节点（右侧定义区）。
    /// </summary>
    [ObservableProperty] private GearTaskViewModel _currentTaskTreeRoot = new();

    /// <summary>
    /// 当前选中的任务节点。
    /// </summary>
    [ObservableProperty] private GearTaskViewModel? _selectedTaskNode;

    [ObservableProperty] private int _selectedDetailTabIndex;

    [ObservableProperty] private string _executionRecordTabTitle = "执行记录 (0)";

    [ObservableProperty] private string _latestExecutionStatusText = "暂无执行记录";

    [ObservableProperty] private string _latestResumeNodeText = "-";

    [ObservableProperty] private string _executionSuccessRateText = "-";

    [ObservableProperty] private string _executionAverageDurationText = "-";

    [ObservableProperty] private ObservableCollection<GearTaskExecutionRecordItemViewModel> _recentExecutionRecords = [];

    [ObservableProperty] private GearTaskExecutionRecordItemViewModel? _selectedExecutionRecord;

    [ObservableProperty] private ObservableCollection<GearTaskExecutionNodeItemViewModel> _selectedExecutionNodes = [];

    [ObservableProperty] private bool _canResumeSelectedExecutionRecord;

    public GearTaskExecutor Executor => _taskExecutor;

    public GearTaskListPageViewModel(
        ILogger<GearTaskListPageViewModel> logger,
        GearTaskStorageService storageService,
        IGearTaskHistoryStore historyStore,
        GearTaskExecutor taskExecutor)
    {
        _logger = logger;
        _storageService = storageService;
        _historyStore = historyStore;
        _taskExecutor = taskExecutor;

        InitializeData();

        TaskDefinitions.CollectionChanged += OnTaskDefinitionsChanged;
        CurrentTaskTreeRoot.Children.CollectionChanged += OnCurrentTaskTreeChanged;
    }

    partial void OnSelectedTaskDefinitionChanged(GearTaskDefinitionViewModel? value)
    {
        foreach (var task in TaskDefinitions)
        {
            task.IsSelected = false;
        }

        if (value != null)
        {
            value.IsSelected = true;
        }

        CurrentTaskTreeRoot.Children.CollectionChanged -= OnCurrentTaskTreeChanged;

        CurrentTaskTreeRoot = value?.RootTask ?? new GearTaskViewModel();

        CurrentTaskTreeRoot.Children.CollectionChanged += OnCurrentTaskTreeChanged;

        _ = LoadExecutionRecordsForSelectedTaskDefinitionAsync(value);
    }

    partial void OnSelectedExecutionRecordChanged(GearTaskExecutionRecordItemViewModel? value)
    {
        SelectedExecutionNodes.Clear();
        CanResumeSelectedExecutionRecord = value?.Record.CanResume == true;

        if (value == null)
        {
            return;
        }

        foreach (var node in value.Record.Nodes.OrderBy(n => n.NodeId, Comparer<string>.Create(GearTaskExecutionDisplayHelper.CompareNodeId)))
        {
            SelectedExecutionNodes.Add(new GearTaskExecutionNodeItemViewModel(node));
        }
    }

    /// <summary>
    /// 任务定义集合变化时，更新顺序并保存。
    /// </summary>
    private async void OnTaskDefinitionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        try
        {
            for (int i = 0; i < TaskDefinitions.Count; i++)
            {
                if (TaskDefinitions[i].Order != i)
                {
                    TaskDefinitions[i].Order = i;
                    TaskDefinitions[i].ModifiedTime = DateTime.Now;
                }
            }

            foreach (var taskDef in TaskDefinitions)
            {
                await _storageService.SaveTaskDefinitionAsync(taskDef);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存任务定义顺序失败");
        }
    }

    /// <summary>
    /// 根级树结构变化后自动保存。
    /// </summary>
    private async void OnCurrentTaskTreeChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedTaskDefinition == null)
        {
            return;
        }

        try
        {
            SelectedTaskDefinition.ModifiedTime = DateTime.Now;
            await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自动保存任务定义失败: {TaskName}", SelectedTaskDefinition.Name);
        }
    }

    /// <summary>
    /// 初始化任务定义和历史记录摘要。
    /// </summary>
    private async void InitializeData()
    {
        try
        {
            var loadedTasks = await _storageService.LoadAllTaskDefinitionsAsync();
            foreach (var task in loadedTasks.OrderBy(t => t.Order))
            {
                TaskDefinitions.Add(task);
                SetupTaskDefinitionPropertyChanged(task);
            }

            if (TaskDefinitions.Count == 0)
            {
                await CreateSampleTaskAsync();
            }

            await RefreshTaskDefinitionExecutionSummariesAsync();
            SelectedTaskDefinition ??= TaskDefinitions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化任务列表失败");
            await CreateSampleTaskAsync();
            await RefreshTaskDefinitionExecutionSummariesAsync();
            SelectedTaskDefinition ??= TaskDefinitions.FirstOrDefault();
        }
    }

    /// <summary>
    /// 创建示例任务。
    /// </summary>
    private async Task CreateSampleTaskAsync()
    {
        var sampleTask = new GearTaskDefinitionViewModel("示例任务组", "这是一个示例任务组");
        if (sampleTask.RootTask != null)
        {
            sampleTask.RootTask.AddChild(new GearTaskViewModel("采集任务1") { TaskType = "采集任务" });
            sampleTask.RootTask.AddChild(new GearTaskViewModel("战斗任务1") { TaskType = "战斗任务" });

            var subGroup = new GearTaskViewModel("子任务组", true);
            subGroup.AddChild(new GearTaskViewModel("传送任务") { TaskType = "传送任务" });
            subGroup.AddChild(new GearTaskViewModel("交互任务1") { TaskType = "交互任务" });
            sampleTask.RootTask.AddChild(subGroup);
        }

        TaskDefinitions.Add(sampleTask);
        SetupTaskDefinitionPropertyChanged(sampleTask);
        await _storageService.SaveTaskDefinitionAsync(sampleTask);
        await RefreshTaskDefinitionExecutionSummaryAsync(sampleTask);
    }

    [RelayCommand]
    private void SelectTaskDefinition(GearTaskDefinitionViewModel? taskDefinition)
    {
        SelectedTaskDefinition = taskDefinition;
    }

    [RelayCommand]
    private async Task AddTaskDefinition()
    {
        var editViewModel = App.GetService<TaskDefinitionEditWindowViewModel>();
        var editWindow = App.GetService<TaskDefinitionEditWindow>();
        if (editViewModel == null || editWindow == null)
        {
            return;
        }

        editViewModel.Name = $"新任务组{TaskDefinitions.Count + 1}";
        editViewModel.Description = string.Empty;

        editWindow.ViewModel.Name = editViewModel.Name;
        editWindow.ViewModel.Description = editViewModel.Description;
        editWindow.Owner = Application.Current.MainWindow;

        if (editWindow.ShowDialog() != true)
        {
            return;
        }

        var newTask = new GearTaskDefinitionViewModel(editWindow.ViewModel.Name, editWindow.ViewModel.Description)
        {
            Order = TaskDefinitions.Count > 0 ? TaskDefinitions.Max(t => t.Order) + 1 : 0,
        };

        TaskDefinitions.Add(newTask);
        SetupTaskDefinitionPropertyChanged(newTask);
        SelectedTaskDefinition = newTask;

        await _storageService.SaveTaskDefinitionAsync(newTask);
        await RefreshTaskDefinitionExecutionSummaryAsync(newTask);
    }

    [RelayCommand]
    private async Task DeleteTaskDefinition(GearTaskDefinitionViewModel? taskDefinition)
    {
        if (taskDefinition == null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"确定要删除任务定义 '{taskDefinition.Name}' 吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var taskName = taskDefinition.Name;
        TaskDefinitions.Remove(taskDefinition);
        if (SelectedTaskDefinition == taskDefinition)
        {
            SelectedTaskDefinition = TaskDefinitions.FirstOrDefault();
        }

        await _storageService.DeleteTaskDefinitionAsync(taskName);
    }

    [RelayCommand]
    private async Task EditSelectedTaskDefinition()
    {
        if (SelectedTaskDefinition == null)
        {
            return;
        }

        var editViewModel = App.GetService<TaskDefinitionEditWindowViewModel>();
        var editWindow = App.GetService<TaskDefinitionEditWindow>();
        if (editViewModel == null || editWindow == null)
        {
            return;
        }

        editViewModel.Name = SelectedTaskDefinition.Name;
        editViewModel.Description = SelectedTaskDefinition.Description;

        editWindow.ViewModel.Name = editViewModel.Name;
        editWindow.ViewModel.Description = editViewModel.Description;
        editWindow.Owner = Application.Current.MainWindow;

        if (editWindow.ShowDialog() != true)
        {
            return;
        }

        SelectedTaskDefinition.Name = editWindow.ViewModel.Name;
        SelectedTaskDefinition.Description = editWindow.ViewModel.Description;
        SelectedTaskDefinition.ModifiedTime = DateTime.Now;

        await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
        await RefreshTaskDefinitionExecutionSummaryAsync(SelectedTaskDefinition);
    }

    [RelayCommand]
    private async Task DeleteSelectedTaskDefinition()
    {
        if (SelectedTaskDefinition == null)
        {
            return;
        }

        await DeleteTaskDefinition(SelectedTaskDefinition);
    }

    [RelayCommand]
    private async Task ExecuteSelectedTaskDefinition()
    {
        await ExecuteTaskDefinition(SelectedTaskDefinition);
    }

    [RelayCommand]
    private async Task ExecuteTaskDefinition(GearTaskDefinitionViewModel? taskDefinition)
    {
        taskDefinition ??= SelectedTaskDefinition;
        if (taskDefinition == null)
        {
            Toast.Warning("请先选择要执行的任务组");
            return;
        }

        if (_taskExecutor.IsExecuting)
        {
            Toast.Warning("已有任务组正在执行，请稍后再试");
            return;
        }

        try
        {
            await _taskExecutor.ExecuteTaskDefinitionAsync(taskDefinition.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行任务定义失败: {TaskName}", taskDefinition.Name);
            Toast.Error($"执行任务组失败：{ex.Message}");
        }
        finally
        {
            await RefreshTaskDefinitionExecutionSummaryAsync(taskDefinition);
            if (SelectedTaskDefinition?.Name == taskDefinition.Name)
            {
                await LoadExecutionRecordsForSelectedTaskDefinitionAsync(SelectedTaskDefinition);
            }
        }
    }

    [RelayCommand]
    private async Task ResumeSelectedExecutionRecord()
    {
        if (SelectedTaskDefinition == null || SelectedExecutionRecord == null)
        {
            Toast.Warning("请先选择一条执行记录");
            return;
        }

        if (!SelectedExecutionRecord.Record.CanResume)
        {
            Toast.Warning("当前记录没有可恢复的断点");
            return;
        }

        if (_taskExecutor.IsExecuting)
        {
            Toast.Warning("已有任务组正在执行，请稍后再试");
            return;
        }

        try
        {
            await _taskExecutor.ResumeTaskDefinitionAsync(SelectedTaskDefinition.Name, SelectedExecutionRecord.RecordId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复任务记录失败: {TaskName} {RecordId}", SelectedTaskDefinition.Name, SelectedExecutionRecord.RecordId);
            Toast.Error($"继续执行失败：{ex.Message}");
        }
        finally
        {
            await RefreshTaskDefinitionExecutionSummaryAsync(SelectedTaskDefinition);
            await LoadExecutionRecordsForSelectedTaskDefinitionAsync(SelectedTaskDefinition);
            SelectedDetailTabIndex = 1;
        }
    }

    [RelayCommand]
    private void StopTaskExecution()
    {
        _taskExecutor.StopExecution();
    }

    [RelayCommand]
    private async Task AddTaskNode(string? taskType = null)
    {
        if (SelectedTaskDefinition?.RootTask == null)
        {
            Toast.Warning("请先选择一个任务定义");
            return;
        }

        if (SelectedTaskNode != null && !SelectedTaskNode.IsDirectory)
        {
            Toast.Warning("只有任务组下才能继续添加任务");
            return;
        }

        taskType ??= "Javascript";

        GearTaskViewModel newTask;
        if (taskType == "Javascript")
        {
            var jsSelectionWindow = new JsScriptSelectionWindow
            {
                Owner = Application.Current.MainWindow,
            };

            jsSelectionWindow.ShowDialog();
            if (!jsSelectionWindow.DialogResult || jsSelectionWindow.ViewModel.SelectedScript == null)
            {
                return;
            }

            var selectedScript = jsSelectionWindow.ViewModel.SelectedScript;
            newTask = new GearTaskViewModel(string.IsNullOrWhiteSpace(selectedScript.Name) ? selectedScript.FolderName : selectedScript.Name)
            {
                TaskType = "Javascript",
                Path = $@"{{jsUserFolder}}\{selectedScript.FolderName}\",
            };
        }
        else if (taskType == "Pathing")
        {
            var pathingSelectionWindow = new PathingTaskSelectionWindow
            {
                Owner = Application.Current.MainWindow,
            };

            pathingSelectionWindow.ShowDialog();
            if (!pathingSelectionWindow.DialogResult || pathingSelectionWindow.SelectedGearTask == null)
            {
                return;
            }

            newTask = pathingSelectionWindow.SelectedGearTask;
        }
        else if (taskType == "KeyMouse")
        {
            var list = LoadAllKmScripts();
            var folder = KeyMouseRecordPageViewModel.ScriptPath;
            var combobox = new ComboBox
            {
                VerticalAlignment = VerticalAlignment.Top,
            };

            foreach (var fileInfo in list)
            {
                var relativePath = Path.GetRelativePath(folder, fileInfo.FullName);
                combobox.Items.Add(relativePath);
            }

            var selected = PromptDialog.Prompt("请选择需要添加的键鼠脚本", "请选择需要添加的键鼠脚本", combobox);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            newTask = new GearTaskViewModel(selected)
            {
                TaskType = "Javascript",
                Path = $@"{{kmUserFolder}}\{selected}\",
            };
        }
        else if (taskType == "Shell")
        {
            var command = PromptDialog.Prompt(
                "执行 shell 操作存在风险，请确认命令内容正确后再执行。",
                "请输入需要执行的 shell 命令");
            if (string.IsNullOrEmpty(command))
            {
                return;
            }

            newTask = new GearTaskViewModel(command)
            {
                TaskType = "Shell",
                Parameters = command,
            };
        }
        else
        {
            var dialogResult = AddTaskNodeDialog.ShowDialog(taskType, Application.Current.MainWindow);
            if (dialogResult == null)
            {
                return;
            }

            newTask = new GearTaskViewModel(dialogResult.TaskName)
            {
                TaskType = dialogResult.TaskType,
            };
        }

        var targetParent = SelectedTaskNode ?? SelectedTaskDefinition.RootTask;
        targetParent.AddChild(newTask);
        targetParent.IsExpanded = true;
        SelectedTaskDefinition.ModifiedTime = DateTime.Now;
        await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
    }

    private List<FileInfo> LoadAllKmScripts()
    {
        var folder = KeyMouseRecordPageViewModel.ScriptPath;
        var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
        return files.Select(file => new FileInfo(file)).ToList();
    }

    [RelayCommand]
    private async Task AddTaskGroup()
    {
        if (SelectedTaskDefinition?.RootTask == null)
        {
            Toast.Warning("请先选择一个任务定义");
            return;
        }

        if (SelectedTaskNode != null && !SelectedTaskNode.IsDirectory)
        {
            Toast.Warning("只有任务组下才能继续添加任务组");
            return;
        }

        var groupName = PromptDialog.Prompt("请输入任务组名称:", "添加任务组", $"新任务组{DateTime.Now:HHmmss}");
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return;
        }

        var newGroup = new GearTaskViewModel(groupName, true);
        var targetParent = SelectedTaskNode ?? SelectedTaskDefinition.RootTask;
        targetParent.AddChild(newGroup);
        targetParent.IsExpanded = true;
        SelectedTaskDefinition.ModifiedTime = DateTime.Now;
        await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
    }

    [RelayCommand]
    private async Task OpenSelectedTaskGroupSettings()
    {
        await OpenTaskGroupSettingsInternalAsync(SelectedTaskNode);
    }

    [RelayCommand]
    private async Task OpenTaskGroupSettings(GearTaskViewModel? taskNode)
    {
        await OpenTaskGroupSettingsInternalAsync(taskNode);
    }

    [RelayCommand]
    private async Task DeleteTaskNode(GearTaskViewModel? taskNode)
    {
        if (taskNode == null || SelectedTaskDefinition?.RootTask == null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"确定要删除任务 '{taskNode.Name}' 吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        RemoveTaskFromTree(SelectedTaskDefinition.RootTask, taskNode);
        SelectedTaskDefinition.ModifiedTime = DateTime.Now;
        await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
    }

    private async Task OpenTaskGroupSettingsInternalAsync(GearTaskViewModel? taskNode)
    {
        if (SelectedTaskDefinition == null)
        {
            Toast.Warning("请先选择一个任务定义");
            return;
        }

        if (taskNode == null || !taskNode.IsDirectory)
        {
            Toast.Warning("请先选择一个任务组");
            return;
        }

        taskNode.GroupConfig ??= new ScriptGroupConfig();

        var dialogWindow = new FluentWindow
        {
            Title = "任务组设置",
            Content = new ScriptGroupConfigView(new ScriptGroupConfigViewModel(TaskContext.Instance().Config, taskNode.GroupConfig)),
            Width = 800,
            Height = 600,
            MinWidth = 800,
            MaxWidth = 800,
            MinHeight = 600,
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendsContentIntoTitleBar = true,
            WindowBackdropType = WindowBackdropType.Auto,
        };
        dialogWindow.SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(dialogWindow);
        dialogWindow.ShowDialog();

        try
        {
            taskNode.ModifiedTime = DateTime.Now;
            SelectedTaskDefinition.ModifiedTime = DateTime.Now;
            await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存任务组设置失败: {TaskName}", taskNode.Name);
            ThemedMessageBox.Error($"保存任务组设置失败：{ex.Message}", "保存失败");
        }
    }

    private bool RemoveTaskFromTree(GearTaskViewModel parent, GearTaskViewModel target)
    {
        if (parent.Children.Contains(target))
        {
            parent.RemoveChild(target);
            return true;
        }

        foreach (var child in parent.Children)
        {
            if (RemoveTaskFromTree(child, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 从 JSON 文件重新加载所有任务定义（内部使用）。
    /// </summary>
    private async Task LoadFromJsonInternal()
    {
        try
        {
            TaskDefinitions.Clear();
            var loadedTasks = await _storageService.LoadAllTaskDefinitionsAsync();

            foreach (var task in loadedTasks.OrderBy(t => t.Order))
            {
                TaskDefinitions.Add(task);
                SetupTaskDefinitionPropertyChanged(task);
            }

            await RefreshTaskDefinitionExecutionSummariesAsync();
            SelectedTaskDefinition ??= TaskDefinitions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新加载任务定义失败");
        }
    }

    /// <summary>
    /// 监听任务定义属性变化，实现自动保存。
    /// </summary>
    private void SetupTaskDefinitionPropertyChanged(GearTaskDefinitionViewModel taskDefinition)
    {
        taskDefinition.PropertyChanged += async (sender, _) =>
        {
            if (sender is not GearTaskDefinitionViewModel task)
            {
                return;
            }

            try
            {
                await _storageService.SaveTaskDefinitionAsync(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动保存任务定义失败: {TaskName}", task.Name);
            }
        };

        if (taskDefinition.RootTask != null)
        {
            SetupTaskPropertyChangeListener(taskDefinition.RootTask, taskDefinition);
        }
    }

    /// <summary>
    /// 递归监听任务节点和子节点变化。
    /// </summary>
    private void SetupTaskPropertyChangeListener(GearTaskViewModel task, GearTaskDefinitionViewModel parentDefinition)
    {
        task.PropertyChanged += async (_, _) =>
        {
            try
            {
                parentDefinition.ModifiedTime = DateTime.Now;
                await _storageService.SaveTaskDefinitionAsync(parentDefinition);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动保存任务定义失败: {TaskName}", parentDefinition.Name);
            }
        };

        foreach (var child in task.Children)
        {
            SetupTaskPropertyChangeListener(child, parentDefinition);
        }

        task.Children.CollectionChanged += async (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (GearTaskViewModel newTask in e.NewItems)
                {
                    SetupTaskPropertyChangeListener(newTask, parentDefinition);
                }
            }

            try
            {
                parentDefinition.ModifiedTime = DateTime.Now;
                await _storageService.SaveTaskDefinitionAsync(parentDefinition);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动保存任务定义失败: {TaskName}", parentDefinition.Name);
            }
        };
    }

    private async Task RefreshTaskDefinitionExecutionSummariesAsync()
    {
        foreach (var taskDefinition in TaskDefinitions)
        {
            await RefreshTaskDefinitionExecutionSummaryAsync(taskDefinition);
        }
    }

    private async Task RefreshTaskDefinitionExecutionSummaryAsync(GearTaskDefinitionViewModel taskDefinition)
    {
        try
        {
            var records = await _historyStore.LoadLatestAsync(GetTaskDefinitionFileKey(taskDefinition.Name), 30);
            ApplyTaskDefinitionExecutionSummary(taskDefinition, records);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取任务历史摘要失败: {TaskName}", taskDefinition.Name);
            ApplyTaskDefinitionExecutionSummary(taskDefinition, []);
        }
    }

    private async Task LoadExecutionRecordsForSelectedTaskDefinitionAsync(GearTaskDefinitionViewModel? taskDefinition)
    {
        if (taskDefinition == null)
        {
            RecentExecutionRecords.Clear();
            SelectedExecutionRecord = null;
            SelectedExecutionNodes.Clear();
            ExecutionRecordTabTitle = "执行记录 (0)";
            UpdateExecutionOverview([]);
            ApplyLatestExecutionProjection(null);
            return;
        }

        try
        {
            var taskName = taskDefinition.Name;
            var records = await _historyStore.LoadLatestAsync(GetTaskDefinitionFileKey(taskName), 30);
            if (!string.Equals(SelectedTaskDefinition?.Name, taskName, StringComparison.Ordinal))
            {
                return;
            }

            RecentExecutionRecords.Clear();
            foreach (var record in records)
            {
                RecentExecutionRecords.Add(new GearTaskExecutionRecordItemViewModel(record));
            }

            ExecutionRecordTabTitle = $"执行记录 ({RecentExecutionRecords.Count})";
            SelectedExecutionRecord = RecentExecutionRecords.FirstOrDefault();
            UpdateExecutionOverview(records);
            ApplyLatestExecutionProjection(records.FirstOrDefault());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载任务执行记录失败: {TaskName}", taskDefinition.Name);
            RecentExecutionRecords.Clear();
            SelectedExecutionRecord = null;
            SelectedExecutionNodes.Clear();
            ExecutionRecordTabTitle = "执行记录 (0)";
            UpdateExecutionOverview([]);
            ApplyLatestExecutionProjection(null);
        }
    }

    private void ApplyTaskDefinitionExecutionSummary(GearTaskDefinitionViewModel taskDefinition, IReadOnlyList<GearTaskExecutionRecord> records)
    {
        if (records.Count == 0)
        {
            taskDefinition.LatestExecutionSummary = "最近：暂无执行记录";
            taskDefinition.ExecutionStatisticsSummary = "统计：最近 30 次暂无记录";
            taskDefinition.ExecutionBadgeText = string.Empty;
            taskDefinition.HasExecutionBadge = false;
            return;
        }

        var latestRecord = records[0];
        taskDefinition.LatestExecutionSummary =
            $"最近：{latestRecord.StartTime:MM-dd HH:mm} {GearTaskExecutionDisplayHelper.GetRecordStatusText(latestRecord.Status)}";

        var successCount = records.Count(r => r.Status == GearTaskExecutionRecordStatus.Succeeded);
        var interruptedCount = records.Count(r =>
            r.Status == GearTaskExecutionRecordStatus.Interrupted ||
            r.Status == GearTaskExecutionRecordStatus.Cancelled);
        var failedCount = records.Count(r => r.Status == GearTaskExecutionRecordStatus.Failed);
        taskDefinition.ExecutionStatisticsSummary =
            $"统计：{records.Count} 次内 成功 {successCount} / 中断 {interruptedCount} / 失败 {failedCount}";

        var resumableRecord = records.FirstOrDefault(r => r.CanResume);
        taskDefinition.HasExecutionBadge = resumableRecord != null;
        taskDefinition.ExecutionBadgeText = resumableRecord == null ? string.Empty : "最近有中断记录";
    }

    private void UpdateExecutionOverview(IReadOnlyList<GearTaskExecutionRecord> records)
    {
        if (records.Count == 0)
        {
            LatestExecutionStatusText = "暂无执行记录";
            LatestResumeNodeText = "-";
            ExecutionSuccessRateText = "-";
            ExecutionAverageDurationText = "-";
            return;
        }

        var latestRecord = records[0];
        LatestExecutionStatusText =
            $"{latestRecord.StartTime:MM-dd HH:mm} · {GearTaskExecutionDisplayHelper.GetRecordStatusText(latestRecord.Status)}";
        LatestResumeNodeText = latestRecord.CanResume
            ? GearTaskExecutionDisplayHelper.BuildResumeNodeText(latestRecord)
            : "-";

        var successCount = records.Count(r => r.Status == GearTaskExecutionRecordStatus.Succeeded);
        ExecutionSuccessRateText = $"{Math.Round(successCount * 100d / records.Count):0}%";

        var durations = records
            .Where(r => r.EndTime.HasValue)
            .Select(r => r.EndTime!.Value - r.StartTime)
            .ToList();
        ExecutionAverageDurationText = durations.Count == 0
            ? "-"
            : GearTaskExecutionDisplayHelper.FormatDuration(TimeSpan.FromSeconds(durations.Average(d => d.TotalSeconds)));
    }

    private void ApplyLatestExecutionProjection(GearTaskExecutionRecord? latestRecord)
    {
        ResetTaskNodeExecutionProjection(CurrentTaskTreeRoot);
        if (latestRecord == null)
        {
            return;
        }

        ApplyNodeExecutionProjection(CurrentTaskTreeRoot, "0", latestRecord);
    }

    private void ResetTaskNodeExecutionProjection(GearTaskViewModel node)
    {
        node.LatestExecutionResult = string.Empty;
        foreach (var child in node.Children)
        {
            ResetTaskNodeExecutionProjection(child);
        }
    }

    private void ApplyNodeExecutionProjection(GearTaskViewModel node, string nodeId, GearTaskExecutionRecord latestRecord)
    {
        var record = latestRecord.Nodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (record != null)
        {
            node.LatestExecutionResult = BuildNodeExecutionResultText(record, latestRecord);
        }

        for (var i = 0; i < node.Children.Count; i++)
        {
            ApplyNodeExecutionProjection(node.Children[i], $"{nodeId}/{i}", latestRecord);
        }
    }

    private static string BuildNodeExecutionResultText(GearTaskExecutionNodeRecord record, GearTaskExecutionRecord latestRecord)
    {
        if (latestRecord.CanResume && string.Equals(latestRecord.ResumeNodeId, record.NodeId, StringComparison.Ordinal))
        {
            return "最近中断点";
        }

        return record.Status switch
        {
            GearTaskExecutionNodeStatus.Succeeded => "最近成功",
            GearTaskExecutionNodeStatus.Failed => "最近失败",
            GearTaskExecutionNodeStatus.Interrupted => "最近中断",
            GearTaskExecutionNodeStatus.Cancelled => "最近取消",
            GearTaskExecutionNodeStatus.Running => "最近执行中",
            GearTaskExecutionNodeStatus.Skipped => "最近跳过",
            GearTaskExecutionNodeStatus.Pending => "未执行到",
            _ => record.Status.ToString(),
        };
    }

    private static string GetTaskDefinitionFileKey(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safeName) ? "unnamed_task" : safeName;
    }
}
