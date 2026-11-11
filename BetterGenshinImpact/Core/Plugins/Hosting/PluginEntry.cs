using System.Collections.Generic;
using BetterGenshinImpact.Core.Plugins.Pluggables;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 一个已发现的插件：清单、目录、状态，以及加载后的运行时信息。
/// 属性只在界面线程修改，插件页直接绑定它的变化。
/// </summary>
public sealed partial class PluginEntry : ObservableObject
{
    /// <summary>
    /// 创建插件条目。
    /// </summary>
    /// <param name="directory">插件目录。</param>
    /// <param name="manifest">清单；读取失败时为按目录名生成的占位清单。</param>
    internal PluginEntry(string directory, PluginManifest manifest)
    {
        Directory = directory;
        Manifest = manifest;
    }

    /// <summary>
    /// 插件目录的绝对路径。
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// 插件清单。未加载的插件刷新时可能被替换为新读取的清单。
    /// </summary>
    [ObservableProperty]
    private PluginManifest _manifest = null!;

    /// <summary>
    /// 当前状态。
    /// </summary>
    [ObservableProperty]
    private PluginState _state;

    /// <summary>
    /// 不兼容、加载失败或标识重复的原因。
    /// </summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>
    /// 启用后登记的实现；未启用时为空。
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<PluggableImplDescriptor> _implementations = [];

    /// <summary>
    /// 等待下次启动替换的新版本号，没有时为 null。
    /// </summary>
    [ObservableProperty]
    private string? _pendingVersion;

    /// <summary>
    /// 加载后的运行时信息。程序集一旦加载就保留到进程退出。
    /// </summary>
    internal PluginRuntime? Runtime { get; private set; }

    /// <summary>
    /// 插件程序集是否已载入当前进程。已加载的插件升级、卸载需要重启。
    /// </summary>
    public bool IsLoaded => Runtime is not null;

    /// <summary>
    /// 记录加载后的运行时信息。
    /// </summary>
    internal void SetRuntime(PluginRuntime runtime)
    {
        Runtime = runtime;
        OnPropertyChanged(nameof(IsLoaded));
    }
}
