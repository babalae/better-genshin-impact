using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows.GearTask;

/// <summary>
/// 地图追踪任务信息
/// </summary>
public partial class PathingTaskInfo : ObservableObject
{
    /// <summary>
    /// 任务名称
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 文件夹名称
    /// </summary>
    [ObservableProperty]
    private string _folderName = string.Empty;

    /// <summary>
    /// 完整路径
    /// </summary>
    [ObservableProperty]
    private string _fullPath = string.Empty;

    /// <summary>
    /// 是否为文件夹
    /// </summary>
    [ObservableProperty]
    private bool _isDirectory;

    /// <summary>
    /// JSON内容（如果是JSON文件）
    /// </summary>
    [ObservableProperty]
    private string _jsonContent = string.Empty;

    /// <summary>
    /// README内容（如果是文件夹且包含README.md）
    /// </summary>
    [ObservableProperty]
    private string _readmeContent = string.Empty;

    /// <summary>
    /// 图标URL
    /// </summary>
    [ObservableProperty]
    private string _iconUrl = string.Empty;

    /// <summary>
    /// 是否使用系统图标
    /// </summary>
    [ObservableProperty]
    private bool _useSystemIcon;

    /// <summary>
    /// 父级路径
    /// </summary>
    [ObservableProperty]
    private string _parentPath = string.Empty;

    /// <summary>
    /// 相对路径（相对于pathing文件夹）
    /// </summary>
    [ObservableProperty]
    private string _relativePath = string.Empty;

    /// <summary>
    /// 子项集合（用于TreeView层级结构）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PathingTaskInfo> _children = new();

    public PathingTaskInfo()
    {
    }

    public PathingTaskInfo(string fullPath, string pathingRootPath)
    {
        FullPath = fullPath;
        Name = Path.GetFileName(fullPath);
        FolderName = Path.GetFileNameWithoutExtension(fullPath);
        IsDirectory = Directory.Exists(fullPath);
        ParentPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
        RelativePath = Path.GetRelativePath(pathingRootPath, fullPath);
    }
}