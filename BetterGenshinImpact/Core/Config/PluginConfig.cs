using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 插件与可插拔能力配置。插件自己的设置不放在这里。
/// 集合与条目都可观察，ConfigChangeTracker 跟踪增删和修改后自动保存。
/// </summary>
[Serializable]
public partial class PluginConfig : ObservableObject
{
    /// <summary>
    /// 已启用的插件标识，启动时按此列表启用插件。
    /// </summary>
    public ObservableCollection<string> EnabledPlugins { get; set; } = [];

    /// <summary>
    /// 各可插拔能力选中的实现，没有记录的可插拔能力使用默认实现。
    /// </summary>
    public ObservableCollection<PluggableSelection> Selections { get; set; } = [];
}

/// <summary>
/// 一项可插拔能力的选择。
/// </summary>
[Serializable]
public partial class PluggableSelection : ObservableObject
{
    /// <summary>
    /// 可插拔能力标识，如 ocr。
    /// </summary>
    [ObservableProperty]
    private string _pluggableId = string.Empty;

    /// <summary>
    /// 实现标识，如 ppocr_v5.model。
    /// </summary>
    [ObservableProperty]
    private string _implementationId = string.Empty;
}
