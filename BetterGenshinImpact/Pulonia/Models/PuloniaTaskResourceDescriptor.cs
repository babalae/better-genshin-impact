using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 任务创建资源索引中的一项本地能力资源，不包含路线正文或脚本运行时对象。
/// </summary>
public sealed class PuloniaTaskResourceDescriptor
{
    /// <summary>
    /// 列表中展示的简短名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 相对能力资源根目录的持久化路径。
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// 仅用于读取预览和建立目录引用的完整本地路径。
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// 列表中展示的目录或相对位置。
    /// </summary>
    public string Location { get; }

    /// <summary>
    /// 资源是目录还是单个文件。
    /// </summary>
    public bool IsDirectory { get; }

    /// <summary>
    /// 当前目录是不是能力资源根目录。
    /// </summary>
    public bool IsRootDirectory { get; }

    /// <summary>
    /// 文件大小；目录使用零。
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// 资源最后修改时间。
    /// </summary>
    public DateTime LastWriteTime { get; }

    /// <summary>
    /// 目录下可添加的资源文件数量；普通文件和 JS 项目使用零。
    /// </summary>
    public int ChildResourceCount { get; }

    /// <summary>
    /// 搜索使用的组合文本。
    /// </summary>
    public string SearchText => DisplayName + "\n" + RelativePath;

    /// <summary>
    /// 建立一项不可变资源描述。
    /// </summary>
    public PuloniaTaskResourceDescriptor(string displayName, string relativePath, string fullPath,
        string location, bool isDirectory, bool isRootDirectory, long size, DateTime lastWriteTime,
        int childResourceCount)
    {
        DisplayName = displayName;
        RelativePath = relativePath;
        FullPath = fullPath;
        Location = location;
        IsDirectory = isDirectory;
        IsRootDirectory = isRootDirectory;
        Size = size;
        LastWriteTime = lastWriteTime;
        ChildResourceCount = childResourceCount;
    }
}
