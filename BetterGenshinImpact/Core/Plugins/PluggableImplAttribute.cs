using System;

namespace BetterGenshinImpact.Core.Plugins;

/// <summary>
/// 把类声明为某个可插拔能力的实现。类必须是非抽象类，且只实现一个可插拔能力接口。
/// 完整的实现标识由主程序按来源加前缀：主程序中为 builtin.&lt;名称&gt;，插件中为 &lt;插件标识&gt;.&lt;名称&gt;。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PluggableImplAttribute(string name, string displayName) : Attribute
{
    /// <summary>
    /// 实现名称，小写 snake_case，不带前缀。
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// 选择列表中显示的名称。
    /// </summary>
    public string DisplayName { get; } = displayName;

    /// <summary>
    /// 选择列表中的说明，可为空。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否为默认实现。只允许主程序中的实现设置，每个可插拔能力恰好一个；选中的实现不可用时回退到它。
    /// </summary>
    public bool IsDefault { get; set; }
}
