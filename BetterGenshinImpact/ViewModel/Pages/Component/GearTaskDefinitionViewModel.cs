using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Component;

/// <summary>
/// 任务定义模型，用于左侧列表显示
/// </summary>
public partial class GearTaskDefinitionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isSelected = false;

    [ObservableProperty]
    private DateTime _createdTime = DateTime.Now;

    [ObservableProperty]
    private DateTime _modifiedTime = DateTime.Now;

    /// <summary>
    /// 任务根节点
    /// </summary>
    [ObservableProperty]
    private GearTaskViewModel? _rootTask;

    public GearTaskDefinitionViewModel()
    {
    }

    public GearTaskDefinitionViewModel(string name, string description = "")
    {
        Name = name;
        Description = description;
        RootTask = new GearTaskViewModel(name, true);
    }

    /// <summary>
    /// 获取任务总数（包括所有子任务）
    /// </summary>
    /// <returns></returns>
    public int GetTotalTaskCount()
    {
        if (RootTask == null) return 0;
        return 1 + Enumerable.Count<GearTaskViewModel>(RootTask.GetAllChildren());
    }

    /// <summary>
    /// 获取启用的任务数量
    /// </summary>
    /// <returns></returns>
    public int GetEnabledTaskCount()
    {
        if (RootTask == null) return 0;
        var count = RootTask.IsEnabled ? 1 : 0;
        count += Enumerable.Count<GearTaskViewModel>(RootTask.GetAllChildren(), t => t.IsEnabled);
        return count;
    }
}