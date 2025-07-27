using System.Collections.ObjectModel;
using System.Text.Json;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.ViewModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;
using System.Linq;
using System;
using BetterGenshinImpact.ViewModel.Pages.Component;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 任务列表页面ViewModel
/// </summary>
public partial class GearTaskListPageViewModel : ViewModel
{
    private readonly ILogger<GearTaskListPageViewModel> _logger;

    /// <summary>
    /// 任务定义列表（左侧）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GearTaskDefinitionViewModel> _taskDefinitions = new();

    /// <summary>
    /// 当前选中的任务定义
    /// </summary>
    [ObservableProperty]
    private GearTaskDefinitionViewModel? _selectedTaskDefinition;

    /// <summary>
    /// 当前任务树（右侧）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GearTaskViewModel> _currentTaskTree = new();

    /// <summary>
    /// 当前选中的任务节点
    /// </summary>
    [ObservableProperty]
    private GearTaskViewModel? _selectedTaskNode;

    /// <summary>
    /// 可用的任务类型
    /// </summary>
    public ObservableCollection<string> AvailableTaskTypes { get; } = new()
    {
        "采集任务",
        "战斗任务",
        "传送任务",
        "交互任务",
        "等待任务",
        "脚本任务",
        "条件任务",
        "循环任务",
        "组合任务"
    };

    public GearTaskListPageViewModel(ILogger<GearTaskListPageViewModel> logger)
    {
        _logger = logger;
        InitializeData();
    }

    /// <summary>
    /// 初始化数据
    /// </summary>
    private void InitializeData()
    {
        // 创建示例数据
        var sampleTask = new GearTaskDefinitionViewModel("示例任务组", "这是一个示例任务组");
        if (sampleTask.RootTask != null)
        {
            sampleTask.RootTask.AddChild(new GearTaskViewModel("采集任务1") { TaskType = "采集任务", Description = "采集莲花" });
            sampleTask.RootTask.AddChild(new GearTaskViewModel("战斗任务1") { TaskType = "战斗任务", Description = "击败史莱姆" });
            
            var subGroup = new GearTaskViewModel("子任务组", true);
            subGroup.AddChild(new GearTaskViewModel("传送任务1") { TaskType = "传送任务", Description = "传送到蒙德" });
            subGroup.AddChild(new GearTaskViewModel("交互任务1") { TaskType = "交互任务", Description = "与NPC对话" });
            sampleTask.RootTask.AddChild(subGroup);
        }
        
        TaskDefinitions.Add(sampleTask);
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
        
        CurrentTaskTree.Clear();
        if (value?.RootTask != null)
        {
            CurrentTaskTree.Add(value.RootTask);
        }
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
    private void AddTaskDefinition()
    {
        var newTask = new GearTaskDefinitionViewModel($"新任务组 {TaskDefinitions.Count + 1}", "新创建的任务组");
        TaskDefinitions.Add(newTask);
        SelectedTaskDefinition = newTask;
    }

    /// <summary>
    /// 删除任务定义
    /// </summary>
    [RelayCommand]
    private void DeleteTaskDefinition(GearTaskDefinitionViewModel? taskDefinition)
    {
        if (taskDefinition == null) return;
        
        var result = MessageBox.Show($"确定要删除任务定义 '{taskDefinition.Name}' 吗？", "确认删除", 
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            TaskDefinitions.Remove(taskDefinition);
            if (SelectedTaskDefinition == taskDefinition)
            {
                SelectedTaskDefinition = TaskDefinitions.FirstOrDefault();
            }
        }
    }

    /// <summary>
    /// 重命名任务定义
    /// </summary>
    [RelayCommand]
    private void RenameTaskDefinition(GearTaskDefinitionViewModel? taskDefinition)
    {
        if (taskDefinition == null) return;
        
        // 这里可以弹出重命名对话框，暂时简单处理
        var newName = "新名称"; // 简化处理，实际应该弹出输入框
        
        if (!string.IsNullOrWhiteSpace(newName))
        {
            taskDefinition.Name = newName;
            taskDefinition.ModifiedTime = DateTime.Now;
            if (taskDefinition.RootTask != null)
            {
                taskDefinition.RootTask.Name = newName;
            }
        }
    }

    /// <summary>
    /// 添加任务节点
    /// </summary>
    [RelayCommand]
    private void AddTaskNode(string? taskType = null)
    {
        if (SelectedTaskDefinition?.RootTask == null) return;

        var newTask = new GearTaskViewModel($"新任务 {DateTime.Now:HHmmss}")
        {
            TaskType = taskType ?? AvailableTaskTypes.First(),
            Description = "新创建的任务"
        };

        if (SelectedTaskNode != null)
        {
            SelectedTaskNode.AddChild(newTask);
        }
        else
        {
            SelectedTaskDefinition.RootTask.AddChild(newTask);
        }

        SelectedTaskDefinition.ModifiedTime = DateTime.Now;
    }

    /// <summary>
    /// 添加任务组
    /// </summary>
    [RelayCommand]
    private void AddTaskGroup()
    {
        if (SelectedTaskDefinition?.RootTask == null) return;

        var newGroup = new GearTaskViewModel($"新任务组 {DateTime.Now:HHmmss}", true)
        {
            Description = "新创建的任务组"
        };

        if (SelectedTaskNode != null)
        {
            SelectedTaskNode.AddChild(newGroup);
        }
        else
        {
            SelectedTaskDefinition.RootTask.AddChild(newGroup);
        }

        SelectedTaskDefinition.ModifiedTime = DateTime.Now;
    }

    /// <summary>
    /// 删除任务节点
    /// </summary>
    [RelayCommand]
    private void DeleteTaskNode(GearTaskViewModel? taskNode)
    {
        if (taskNode == null || SelectedTaskDefinition?.RootTask == null) return;

        var result = MessageBox.Show($"确定要删除任务 '{taskNode.Name}' 吗？", "确认删除", 
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            RemoveTaskFromTree(SelectedTaskDefinition.RootTask, taskNode);
            SelectedTaskDefinition.ModifiedTime = DateTime.Now;
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
    /// 保存到JSON（预留功能）
    /// </summary>
    [RelayCommand]
    private void SaveToJson()
    {
        try
        {
            var json = JsonSerializer.Serialize(TaskDefinitions, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            
            // 这里可以保存到配置文件
            _logger.LogInformation("任务定义已序列化为JSON: {Json}", json);
            MessageBox.Show("保存成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存任务定义到JSON时发生错误");
            MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 从JSON加载（预留功能）
    /// </summary>
    [RelayCommand]
    private void LoadFromJson()
    {
        try
        {
            // 这里可以从配置文件加载
            _logger.LogInformation("从JSON加载任务定义功能待实现");
            MessageBox.Show("加载功能待实现！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从JSON加载任务定义时发生错误");
            MessageBox.Show($"加载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}