using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 地图追踪资源选择树中的一个目录或路线文件节点。
/// </summary>
public partial class PuloniaTaskResourceTreeNodeViewModel : ObservableObject
{
    /// <summary>
    /// 当前节点对应的轻量资源描述。
    /// </summary>
    public PuloniaTaskResourceDescriptor Resource { get; }

    /// <summary>
    /// 当前节点的子目录和路线文件。
    /// </summary>
    public ObservableCollection<PuloniaTaskResourceTreeNodeViewModel> Children { get; } = [];

    /// <summary>
    /// 树中展示的资源名称。
    /// </summary>
    public string DisplayName => Resource.DisplayName;

    /// <summary>
    /// 树中用于提示的相对路径。
    /// </summary>
    public string RelativePath => Resource.RelativePath;

    /// <summary>
    /// 当前节点是否为目录。
    /// </summary>
    public bool IsDirectory => Resource.IsDirectory;

    /// <summary>
    /// 当前节点是否展开。
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// 建立一个资源树节点。
    /// </summary>
    public PuloniaTaskResourceTreeNodeViewModel(PuloniaTaskResourceDescriptor resource, bool isExpanded)
    {
        Resource = resource;
        _isExpanded = isExpanded;
    }
}
