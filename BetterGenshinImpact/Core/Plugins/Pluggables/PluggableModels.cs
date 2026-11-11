using System;

namespace BetterGenshinImpact.Core.Plugins.Pluggables;

// 可插拔机制对外提供的只读数据：描述、选择状态和事件参数。

/// <summary>
/// 一项可插拔能力的描述，来自接口上的 <see cref="PluggableAttribute"/>。
/// </summary>
/// <param name="Id">可插拔能力标识，如 ocr。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Description">说明，可为空。</param>
/// <param name="ContractType">可插拔能力接口。</param>
public sealed record PluggableDescriptor(string Id, string DisplayName, string? Description, Type ContractType);

/// <summary>
/// 一个实现的描述，来自实现类上的 <see cref="PluggableImplAttribute"/>。
/// </summary>
/// <param name="Id">完整实现标识，如 builtin.ppocr_v6、ppocr_v5.model。</param>
/// <param name="PluggableId">所属可插拔能力的标识。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Description">说明，可为空。</param>
/// <param name="ImplementationType">实现类。</param>
/// <param name="IsDefault">是否为默认实现。</param>
/// <param name="PluginId">来源插件的标识，内置实现为 null。</param>
/// <param name="PluginName">来源插件的名称，内置实现为 null。</param>
public sealed record PluggableImplDescriptor(
    string Id,
    string PluggableId,
    string DisplayName,
    string? Description,
    Type ImplementationType,
    bool IsDefault,
    string? PluginId,
    string? PluginName)
{
    /// <summary>
    /// 是否为主程序中的内置实现。
    /// </summary>
    public bool IsBuiltin => PluginId is null;

    /// <summary>
    /// 创建实例时解析构造参数的服务容器。内置实现为 null，表示使用主程序的服务容器。
    /// </summary>
    internal IServiceProvider? Services { get; init; }
}

/// <summary>
/// 一项可插拔能力当前的选择状态，供界面显示。
/// </summary>
/// <param name="Pluggable">可插拔能力。</param>
/// <param name="SavedImplementationId">配置中保存的实现标识，没有保存时为 null。</param>
/// <param name="Saved">保存的实现当前已登记时为其描述，否则为 null（例如来源插件未启用）。</param>
/// <param name="Effective">实际使用的实现。</param>
/// <param name="IsFallback">保存的实现不可用，正在使用默认实现。</param>
/// <param name="SavedFailed">保存的实现已登记，但创建实例失败。</param>
public sealed record PluggableSelectionState(
    PluggableDescriptor Pluggable,
    string? SavedImplementationId,
    PluggableImplDescriptor? Saved,
    PluggableImplDescriptor Effective,
    bool IsFallback,
    bool SavedFailed);

/// <summary>
/// 可插拔能力的实现增减、选择变化或实例重建。
/// </summary>
/// <param name="pluggableId">发生变化的可插拔能力标识。</param>
public sealed class PluggableChangedEventArgs(string pluggableId) : EventArgs
{
    /// <summary>
    /// 发生变化的可插拔能力标识。
    /// </summary>
    public string PluggableId { get; } = pluggableId;
}

/// <summary>
/// 插件实现在加载或调用时出现二进制不兼容（插件用到的主程序成员已不存在）。
/// </summary>
/// <param name="pluginId">插件标识。</param>
/// <param name="exception">原始异常。</param>
public sealed class PluggableIncompatibleEventArgs(string pluginId, Exception exception) : EventArgs
{
    /// <summary>
    /// 插件标识。
    /// </summary>
    public string PluginId { get; } = pluginId;

    /// <summary>
    /// 原始异常。
    /// </summary>
    public Exception Exception { get; } = exception;
}
