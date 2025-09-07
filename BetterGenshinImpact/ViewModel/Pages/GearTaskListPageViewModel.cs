using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Linq;
using System;
using BetterGenshinImpact.ViewModel.Pages.Component;
using BetterGenshinImpact.Service;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Windows;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.GearTask;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 任务列表页面ViewModel
/// </summary>
public partial class GearTaskListPageViewModel : ViewModel
{
    private readonly ILogger<GearTaskListPageViewModel> _logger;
    private readonly GearTaskStorageService _storageService;

    /// <summary>
    /// 任务定义列表（左侧）
    /// </summary>
    [ObservableProperty] private ObservableCollection<GearTaskDefinitionViewModel> _taskDefinitions = new();

    /// <summary>
    /// 当前选中的任务定义
    /// </summary>
    [ObservableProperty] private GearTaskDefinitionViewModel? _selectedTaskDefinition;

    /// <summary>
    /// 当前任务树根节点（右侧）
    /// </summary>
    [ObservableProperty] private GearTaskViewModel _currentTaskTreeRoot = new();

    /// <summary>
    /// 当前选中的任务节点
    /// </summary>
    [ObservableProperty] private GearTaskViewModel? _selectedTaskNode;

    public GearTaskListPageViewModel(ILogger<GearTaskListPageViewModel> logger, GearTaskStorageService storageService)
    {
        _logger = logger;
        _storageService = storageService;
        InitializeData();

        // 监听集合变化，实现自动保存
        TaskDefinitions.CollectionChanged += OnTaskDefinitionsChanged;

        // 监听当前任务树根节点的子集合变化，用于拖拽后自动保存
        CurrentTaskTreeRoot.Children.CollectionChanged += OnCurrentTaskTreeChanged;
    }

    /// <summary>
    /// 任务定义集合变化时的处理
    /// </summary>
    private async void OnTaskDefinitionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 当集合发生变化时（包括拖拽重排序），更新Order属性并保存
        try
        {
            // 更新所有任务定义的Order属性以反映当前顺序
            for (int i = 0; i < TaskDefinitions.Count; i++)
            {
                if (TaskDefinitions[i].Order != i)
                {
                    TaskDefinitions[i].Order = i;
                    TaskDefinitions[i].ModifiedTime = DateTime.Now;
                }
            }

            // 保存所有受影响的任务定义
            foreach (var taskDef in TaskDefinitions)
            {
                await _storageService.SaveTaskDefinitionAsync(taskDef);
            }

            _logger.LogInformation("任务定义列表顺序已更新并保存");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存任务定义列表顺序时发生错误");
        }
    }

    /// <summary>
    /// 当前任务树集合变化时的处理（用于拖拽后自动保存）
    /// </summary>
    private async void OnCurrentTaskTreeChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedTaskDefinition != null)
        {
            try
            {
                SelectedTaskDefinition.ModifiedTime = DateTime.Now;
                await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
                _logger.LogInformation("任务树根级别结构变化，已自动保存任务定义 '{TaskName}'", SelectedTaskDefinition.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动保存任务定义 {TaskName} 时发生错误", SelectedTaskDefinition.Name);
            }
        }
    }

    /// <summary>
    /// 初始化数据
    /// </summary>
    private async void InitializeData()
    {
        try
        {
            // 从 JSON 文件加载任务定义
            var loadedTasks = await _storageService.LoadAllTaskDefinitionsAsync();

            // 按order字段排序
            var sortedTasks = loadedTasks.OrderBy(t => t.Order).ToList();

            foreach (var task in sortedTasks)
            {
                TaskDefinitions.Add(task);
                // 为每个任务定义设置属性变化监听
                SetupTaskDefinitionPropertyChanged(task);
            }

            // 如果没有加载到任何任务，创建一个示例任务
            if (TaskDefinitions.Count == 0)
            {
                await CreateSampleTaskAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化任务数据时发生错误");
            // 发生错误时创建示例任务
            await CreateSampleTaskAsync();
        }
    }

    /// <summary>
    /// 创建示例任务
    /// </summary>
    private async Task CreateSampleTaskAsync()
    {
        var sampleTask = new GearTaskDefinitionViewModel("示例任务组", "这是一个示例任务组");
        if (sampleTask.RootTask != null)
        {
            sampleTask.RootTask.AddChild(new GearTaskViewModel("采集任务1") { TaskType = "采集任务" });
            sampleTask.RootTask.AddChild(new GearTaskViewModel("战斗任务1") { TaskType = "战斗任务" });

            var subGroup = new GearTaskViewModel("子任务组", true);
            subGroup.AddChild(new GearTaskViewModel("传送任务1") { TaskType = "传送任务" });
            subGroup.AddChild(new GearTaskViewModel("交互任务1") { TaskType = "交互任务" });
            sampleTask.RootTask.AddChild(subGroup);
        }

        TaskDefinitions.Add(sampleTask);
        SetupTaskDefinitionPropertyChanged(sampleTask);

        // 保存示例任务到文件
        await _storageService.SaveTaskDefinitionAsync(sampleTask);
    }

    /// <summary>
    /// 选中任务定义时的处理
    /// </summary>
    partial void OnSelectedTaskDefinitionChanged(GearTaskDefinitionViewModel? value)
    {
        // 清除之前选中项的状态
        foreach (var task in TaskDefinitions)
        {
            task.IsSelected = false;
        }

        // 设置当前选中项
        if (value != null)
        {
            value.IsSelected = true;
        }

        // 先解除之前的事件绑定
        CurrentTaskTreeRoot.Children.CollectionChanged -= OnCurrentTaskTreeChanged;

        // 设置当前任务树根节点
        if (value?.RootTask != null)
        {
            CurrentTaskTreeRoot = value.RootTask;
        }
        else
        {
            CurrentTaskTreeRoot = new GearTaskViewModel();
        }

        // 重新绑定事件
        CurrentTaskTreeRoot.Children.CollectionChanged += OnCurrentTaskTreeChanged;
    }

    /// <summary>
    /// 选择任务定义命令
    /// </summary>
    [RelayCommand]
    private void SelectTaskDefinition(GearTaskDefinitionViewModel? taskDefinition)
    {
        SelectedTaskDefinition = taskDefinition;
    }

    /// <summary>
    /// 添加新的任务定义
    /// </summary>
    [RelayCommand]
    private async Task AddTaskDefinition()
    {
        var editViewModel = App.GetService<TaskDefinitionEditWindowViewModel>();
        if (editViewModel == null) return;

        editViewModel.Name = $"新任务组{TaskDefinitions.Count + 1}";
        editViewModel.Description = "";

        var editWindow = App.GetService<TaskDefinitionEditWindow>();
        if (editWindow == null) return;

        editWindow.ViewModel.Name = editViewModel.Name;
        editWindow.ViewModel.Description = editViewModel.Description;
        editWindow.Owner = Application.Current.MainWindow;

        if (editWindow.ShowDialog() == true)
        {
            var newTask = new GearTaskDefinitionViewModel(editWindow.ViewModel.Name, editWindow.ViewModel.Description);
            // 设置新任务的order为当前最大值+1
            newTask.Order = TaskDefinitions.Count > 0 ? TaskDefinitions.Max(t => t.Order) + 1 : 0;
            TaskDefinitions.Add(newTask);
            SetupTaskDefinitionPropertyChanged(newTask);
            SelectedTaskDefinition = newTask;

            // 自动保存到文件
            await _storageService.SaveTaskDefinitionAsync(newTask);
        }
    }

    /// <summary>
    /// 删除任务定义
    /// </summary>
    [RelayCommand]
    private async Task DeleteTaskDefinition(GearTaskDefinitionViewModel? taskDefinition)
    {
        if (taskDefinition == null) return;

        var result = MessageBox.Show($"确定要删除任务定义 '{taskDefinition.Name}' 吗？", "确认删除",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var taskName = taskDefinition.Name;
            TaskDefinitions.Remove(taskDefinition);
            if (SelectedTaskDefinition == taskDefinition)
            {
                SelectedTaskDefinition = TaskDefinitions.FirstOrDefault();
            }

            // 删除对应的 JSON 文件
            await _storageService.DeleteTaskDefinitionAsync(taskName);
        }
    }

    /// <summary>
    /// 编辑选中的任务定义
    /// </summary>
    [RelayCommand]
    private async Task EditSelectedTaskDefinition()
    {
        if (SelectedTaskDefinition == null) return;

        var editViewModel = App.GetService<TaskDefinitionEditWindowViewModel>();
        if (editViewModel == null) return;

        editViewModel.Name = SelectedTaskDefinition.Name;
        editViewModel.Description = SelectedTaskDefinition.Description;

        var editWindow = App.GetService<TaskDefinitionEditWindow>();
        if (editWindow == null) return;

        editWindow.ViewModel.Name = editViewModel.Name;
        editWindow.ViewModel.Description = editViewModel.Description;
        editWindow.Owner = Application.Current.MainWindow;

        if (editWindow.ShowDialog() == true)
        {
            SelectedTaskDefinition.Name = editWindow.ViewModel.Name;
            SelectedTaskDefinition.Description = editWindow.ViewModel.Description;
            SelectedTaskDefinition.ModifiedTime = DateTime.Now;

            // 自动保存到文件
            await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);

            _logger.LogInformation("编辑了任务定义: {Name}", SelectedTaskDefinition.Name);
        }
    }

    /// <summary>
    /// 删除选中的任务定义
    /// </summary>
    [RelayCommand]
    private async Task DeleteSelectedTaskDefinition()
    {
        if (SelectedTaskDefinition == null) return;

        await DeleteTaskDefinition(SelectedTaskDefinition);
    }


    /// <summary>
    /// 添加任务节点
    /// </summary>
    [RelayCommand]
    private async Task AddTaskNode(string? taskType = null)
    {
        if (SelectedTaskDefinition?.RootTask == null)
        {
            Toast.Warning("请先选择一个任务定义");
            return;
        }
        // 检查选中的节点是否为任务组，任务节点下不允许添加任务节点
        if (SelectedTaskNode != null && !SelectedTaskNode.IsDirectory)
        {
            Toast.Warning("只有任务组下能够添加任务！");
            return;
        }

        // 如果没有指定任务类型，默认为Javascript
        taskType ??= "Javascript";

        GearTaskViewModel newTask;

        // 如果是JS脚本类型，使用JS脚本选择窗口
        if (taskType == "Javascript")
        {
            var jsSelectionWindow = new JsScriptSelectionWindow
            {
                Owner = Application.Current.MainWindow
            };

            jsSelectionWindow.ShowDialog();
            
            if (jsSelectionWindow.DialogResult && jsSelectionWindow.ViewModel.SelectedScript != null)
            {
                var selectedScript = jsSelectionWindow.ViewModel.SelectedScript;
                newTask = new GearTaskViewModel(selectedScript.Manifest.Name)
                {
                    TaskType = "Javascript",
                    Path = @$"{{jsUserFolder}}\{selectedScript.FolderName}\"
                };
            }
            else
            {
                return; // 用户取消了操作
            }
        }
        // 如果是地图追踪类型，使用地图追踪任务选择窗口
        else if (taskType == "Pathing")
        {
            var pathingSelectionWindow = new PathingTaskSelectionWindow
            {
                Owner = Application.Current.MainWindow
            };

            pathingSelectionWindow.ShowDialog();
            
            if (pathingSelectionWindow.DialogResult && pathingSelectionWindow.SelectedTask != null)
            {
                var selectedTask = pathingSelectionWindow.SelectedTask;
                newTask = new GearTaskViewModel(selectedTask.Name)
                {
                    TaskType = "Pathing",
                    Path = selectedTask.RelativePath
                };
            }
            else
            {
                return; // 用户取消了操作
            }
        }
        else
        {
            // 其他类型使用原有的对话框
            var dialogResult = AddTaskNodeDialog.ShowDialog(taskType, Application.Current.MainWindow);
            if (dialogResult == null)
            {
                return; // 用户取消了操作
            }

            newTask = new GearTaskViewModel(dialogResult.TaskName)
            {
                TaskType = dialogResult.TaskType,
            };
        }



        // 如果有选中的节点，则在选中节点下新增
        // 如果未选择节点，则在根节点下直接新增
        var targetParent = SelectedTaskNode ?? SelectedTaskDefinition.RootTask;
        targetParent.AddChild(newTask);

        // 展开父节点
        targetParent.IsExpanded = true;

        SelectedTaskDefinition.ModifiedTime = DateTime.Now;

        // 自动保存到文件
        await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
    }

    /// <summary>
    /// 添加任务组
    /// </summary>
    [RelayCommand]
    private async Task AddTaskGroup()
    {
        if (SelectedTaskDefinition?.RootTask == null)
        {
            Toast.Warning("请先选择一个任务定义");
            return;
        }

        // 检查选中的节点是否为任务组，任务节点下不允许添加任务组
        if (SelectedTaskNode != null && !SelectedTaskNode.IsDirectory)
        {
            Toast.Warning("任务节点下不允许添加任务组，只有任务组下能够添加任务组");
            return;
        }

        // 弹出对话框输入任务组名称
        var groupName = PromptDialog.Prompt("请输入任务组名称:", "添加任务组", $"新任务组{DateTime.Now:HHmmss}");
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return; // 用户取消了操作
        }

        var newGroup = new GearTaskViewModel(groupName, true);

        // 如果有选中的节点，则在选中节点下新增
        // 如果未选择节点，则在根节点下直接新增
        var targetParent = SelectedTaskNode ?? SelectedTaskDefinition.RootTask;
        targetParent.AddChild(newGroup);

        // 展开父节点
        targetParent.IsExpanded = true;

        SelectedTaskDefinition.ModifiedTime = DateTime.Now;

        // 自动保存到文件
        await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
    }

    /// <summary>
    /// 删除任务节点
    /// </summary>
    [RelayCommand]
    private async Task DeleteTaskNode(GearTaskViewModel? taskNode)
    {
        if (taskNode == null || SelectedTaskDefinition?.RootTask == null) return;

        var result = MessageBox.Show($"确定要删除任务 '{taskNode.Name}' 吗？", "确认删除",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            RemoveTaskFromTree(SelectedTaskDefinition.RootTask, taskNode);
            SelectedTaskDefinition.ModifiedTime = DateTime.Now;

            // 自动保存到文件
            await _storageService.SaveTaskDefinitionAsync(SelectedTaskDefinition);
        }
    }

    /// <summary>
    /// 从树中移除任务节点
    /// </summary>
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
    /// 从JSON文件重新加载所有任务定义（内部使用）
    /// </summary>
    private async Task LoadFromJsonInternal()
    {
        try
        {
            TaskDefinitions.Clear();
            var loadedTasks = await _storageService.LoadAllTaskDefinitionsAsync();

            foreach (var task in loadedTasks)
            {
                TaskDefinitions.Add(task);
                SetupTaskDefinitionPropertyChanged(task);
            }

            _logger.LogInformation("从JSON文件重新加载了 {Count} 个任务定义", loadedTasks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从JSON文件加载任务定义时发生错误");
        }
    }

    /// <summary>
    /// 为任务定义设置属性变化监听器，实现自动保存
    /// </summary>
    private void SetupTaskDefinitionPropertyChanged(GearTaskDefinitionViewModel taskDefinition)
    {
        taskDefinition.PropertyChanged += async (sender, e) =>
        {
            if (sender is GearTaskDefinitionViewModel task)
            {
                try
                {
                    await _storageService.SaveTaskDefinitionAsync(task);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "自动保存任务定义 {TaskName} 时发生错误", task.Name);
                }
            }
        };

        // 为根任务及其所有子任务设置监听器
        if (taskDefinition.RootTask != null)
        {
            SetupTaskPropertyChangeListener(taskDefinition.RootTask, taskDefinition);
        }
    }

    /// <summary>
    /// 递归为任务及其子任务设置属性变化监听器
    /// </summary>
    private void SetupTaskPropertyChangeListener(GearTaskViewModel task, GearTaskDefinitionViewModel parentDefinition)
    {
        task.PropertyChanged += async (sender, e) =>
        {
            try
            {
                parentDefinition.ModifiedTime = DateTime.Now;
                await _storageService.SaveTaskDefinitionAsync(parentDefinition);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动保存任务定义 {TaskName} 时发生错误", parentDefinition.Name);
            }
        };

        // 为子任务设置监听器
        foreach (var child in task.Children)
        {
            SetupTaskPropertyChangeListener(child, parentDefinition);
        }

        // 监听子任务集合变化
        task.Children.CollectionChanged += async (sender, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (GearTaskViewModel newTask in e.NewItems)
                {
                    SetupTaskPropertyChangeListener(newTask, parentDefinition);
                }
            }

            // 任何集合变化都触发保存（包括拖拽重排序）
            try
            {
                parentDefinition.ModifiedTime = DateTime.Now;
                await _storageService.SaveTaskDefinitionAsync(parentDefinition);
                _logger.LogInformation("任务树结构变化，已自动保存任务定义 '{TaskName}'", parentDefinition.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动保存任务定义 {TaskName} 时发生错误", parentDefinition.Name);
            }
        };
    }

    /// <summary>
    /// 刷新当前任务树显示
    /// </summary>
}