namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 插件状态，见 Docs/design/plugin-system.md 第 11.1 节。
/// </summary>
public enum PluginState
{
    /// <summary>
    /// 已发现，未启用（可能已加载过）。
    /// </summary>
    Disabled,

    /// <summary>
    /// 已启用，实现已交给注册表。
    /// </summary>
    Enabled,

    /// <summary>
    /// 正在停用，等待该插件实现上进行中的调用结束。
    /// </summary>
    Disabling,

    /// <summary>
    /// 清单或版本检查未通过，不加载。
    /// </summary>
    Incompatible,

    /// <summary>
    /// 加载、校验失败，或运行时出现二进制不兼容。可以再次尝试启用。
    /// </summary>
    LoadFailed,

    /// <summary>
    /// 与先扫描到的插件标识相同，不加载。
    /// </summary>
    Duplicate,

    /// <summary>
    /// 已卸载，文件在下次启动时删除。
    /// </summary>
    Uninstalled,
}
