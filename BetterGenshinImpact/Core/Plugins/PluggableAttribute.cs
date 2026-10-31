using System;

namespace BetterGenshinImpact.Core.Plugins;

/// <summary>
/// 把接口声明为可插拔能力。只在主程序中使用，插件不能新增可插拔能力。
/// 启动时主程序扫描带此特性的接口：在服务容器中把接口注册为代理，并在插件页“功能选择”中列出一行。
/// 接口规则见 Docs/design/plugin-system.md 第 7.5 节。
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class PluggableAttribute(string id, string displayName) : Attribute
{
    /// <summary>
    /// 可插拔能力标识，小写 snake_case，如 ocr。
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// 插件页“功能选择”中显示的名称。
    /// </summary>
    public string DisplayName { get; } = displayName;

    /// <summary>
    /// 显示在名称下方的说明，可为空。
    /// </summary>
    public string? Description { get; set; }
}
