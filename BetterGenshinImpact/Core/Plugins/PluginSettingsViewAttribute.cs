using System;

namespace BetterGenshinImpact.Core.Plugins;

/// <summary>
/// 把类声明为插件的设置界面 VM，并指定对应的视图。每个插件最多一个。
/// 插件页为该插件显示“设置”按钮，每次点击用插件服务容器新建 VM（构造函数可以注入 <see cref="IPluginContext"/>），
/// 在弹窗中显示；视图用无参构造函数创建，DataContext 为 VM。窗口关闭后，实现了 IDisposable 的 VM 会被释放。
/// </summary>
/// <remarks>
/// 视图只能使用自己合并的资源字典和应用级资源（WPF-UI 主题等），不能依赖插件页的资源。
/// 局部样式通过 BasedOn 继承 WPF-UI 默认样式。
/// </remarks>
/// <param name="viewType">视图类型，必须派生自 FrameworkElement 并有无参构造函数。</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PluginSettingsViewAttribute(Type viewType) : Attribute
{
    /// <summary>
    /// 视图类型。
    /// </summary>
    public Type ViewType { get; } = viewType;
}
