using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Component;

/// <summary>
/// 齿轮任务模型，支持无限树嵌套层级
/// </summary>
public partial class GearTaskViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;
    
    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _taskType = string.Empty;

    private bool _isEnabled = true;
    
    /// <summary>
    /// 是否启用，支持父子节点联动
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                // 当父节点状态改变时，同步更新所有子节点
                UpdateChildrenEnabledState(value);
            }
        }
    }

    [ObservableProperty]
    private bool _isDirectory = false;

    [ObservableProperty]
    private bool _isExpanded = false;

    [ObservableProperty]
    private ObservableCollection<GearTaskViewModel> _children = new();

    /// <summary>
    /// 任务参数，存储为JSON字符串
    /// shell 脚本存储 shell 命令
    /// </summary>
    [ObservableProperty]
    private string _parameters = "";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 修改时间
    /// </summary>
    public DateTime ModifiedTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 任务优先级
    /// </summary>
    [ObservableProperty]
    private int _priority = 0;

    /// <summary>
    /// 任务标签
    /// </summary>
    [ObservableProperty]
    private string _tags = string.Empty;

    public GearTaskViewModel()
    {
    }

    public GearTaskViewModel(string name, bool isDirectory = false)
    {
        Name = name;
        IsDirectory = isDirectory;
    }

    /// <summary>
    /// 添加子任务
    /// </summary>
    /// <param name="child"></param>
    public void AddChild(GearTaskViewModel child)
    {
        Children.Add(child);
        IsDirectory = true;
    }

    /// <summary>
    /// 移除子任务
    /// </summary>
    /// <param name="child"></param>
    public void RemoveChild(GearTaskViewModel child)
    {
        Children.Remove(child);
        if (Children.Count == 0)
        {
            IsDirectory = false;
        }
    }

    /// <summary>
    /// 获取所有子任务（递归）
    /// </summary>
    /// <returns></returns>
    public IEnumerable<GearTaskViewModel> GetAllChildren()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var grandChild in child.GetAllChildren())
            {
                yield return grandChild;
            }
        }
    }

    /// <summary>
    /// 更新所有子节点的启用状态
    /// </summary>
    /// <param name="enabled">启用状态</param>
    private void UpdateChildrenEnabledState(bool enabled)
    {
        foreach (var child in Children)
        {
            // 直接设置私有字段，避免触发递归更新
            child._isEnabled = enabled;
            child.OnPropertyChanged(nameof(IsEnabled));
            
            // 递归更新子节点的子节点
            child.UpdateChildrenEnabledState(enabled);
        }
    }

    /// <summary>
    /// 克隆任务
    /// </summary>
    /// <returns></returns>
    public GearTaskViewModel Clone()
    {
        var clone = new GearTaskViewModel
        {
            Name = Name,
            Path = Path,
            Description = Description,
            TaskType = TaskType,
            IsEnabled = IsEnabled,
            IsDirectory = IsDirectory,
            Parameters = Parameters,
            Priority = Priority,
            Tags = Tags,
            CreatedTime = CreatedTime,
            ModifiedTime = DateTime.Now
        };

        foreach (var child in Children)
        {
            clone.Children.Add(child.Clone());
        }

        return clone;
    }
}