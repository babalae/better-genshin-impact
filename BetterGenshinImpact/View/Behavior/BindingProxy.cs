using System.Windows;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 在 ContextMenu 等独立可视树中转发页面数据上下文。
/// </summary>
public sealed class BindingProxy : Freezable
{
    /// <summary>
    /// 被转发的数据上下文依赖属性。
    /// </summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(object), typeof(BindingProxy));

    /// <summary>
    /// 被转发的数据上下文。
    /// </summary>
    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>
    /// 为资源字典中的每个使用位置建立独立代理实例。
    /// </summary>
    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
