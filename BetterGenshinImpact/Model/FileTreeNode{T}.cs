using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BetterGenshinImpact.Model;

public partial class FileTreeNode<T> : ObservableObject
{
    // 统一展示的属性
    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private string? _version;

    [ObservableProperty]
    private string? _author;

    [ObservableProperty]
    private bool _isExpanded = false;

    /// <summary>
    /// 界面上显示是文件夹
    /// </summary>
    [ObservableProperty]
    private bool _isDirectory;

    /// <summary>
    /// 文件名
    /// </summary>
    [ObservableProperty]
    private string? _fileName;

    /// <summary>
    /// 完整路径
    /// </summary>
    [ObservableProperty]
    private string? _filePath;

    /// <summary>
    /// 展示图标路径
    /// </summary>
    [ObservableProperty]
    private string? _iconFilePath;

    // 节点的值
    [ObservableProperty]
    private T? _value;

    // 子节点列表
    [ObservableProperty]
    private ObservableCollection<FileTreeNode<T>> _children = [];

    public T BuildValue()
    {
        Value = default!;
        return Value;
    }
}
