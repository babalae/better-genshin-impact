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

    [ObservableProperty]
    private int _order = 0;

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
}