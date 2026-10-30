using System.Windows;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>由控件显式选择原生滚动，避免主窗口平滑滚动接管其虚拟化布局。</summary>
public static class NativeScrolling
{
    /// <summary>向控件模板内的滚动区域继承原生滚动偏好，默认不改变现有行为。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NativeScrolling),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>读取控件或其父级指定的原生滚动偏好。</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>指定此控件及其内部滚动区域使用原生滚动。</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
}
