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
    private string _description = string.Empty;

    [ObservableProperty]
    private string _taskType = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isDirectory = false;

    [ObservableProperty]
    private bool _isExpanded = false;

    [ObservableProperty]
    private ObservableCollection<GearTaskViewModel> _children = new();

    /// <summary>
    /// 任务参数，存储为JSON字符串
    /// </summary>
    [ObservableProperty]
    private string _parameters = "{}";

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
    /// 克隆任务
    /// </summary>
    /// <returns></returns>
    public GearTaskViewModel Clone()
    {
        var clone = new GearTaskViewModel
        {
            Name = Name,
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