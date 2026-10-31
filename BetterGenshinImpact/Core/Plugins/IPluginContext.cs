using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Plugins;

/// <summary>
/// 主程序提供给插件的上下文，每个插件一个。插件中的实现可以在构造函数中注入它。
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// 插件清单。
    /// </summary>
    PluginManifest Manifest { get; }

    /// <summary>
    /// 插件安装目录，即 Plugins/&lt;插件标识&gt;。插件资源（如模型文件）从这里读取。
    /// </summary>
    string PluginDirectory { get; }

    /// <summary>
    /// 插件数据目录，即 User/PluginData/&lt;插件标识&gt;，首次访问时创建。卸载插件时保留。
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// 分类名为 Plugin.&lt;插件标识&gt; 的日志，写入主程序的日志文件。
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// 插件设置的读写，文件为 User/PluginData/&lt;插件标识&gt;/settings.json。
    /// </summary>
    IPluginSettingsStore Settings { get; }
}
