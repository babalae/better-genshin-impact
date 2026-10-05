using System.Windows;
using System.Windows.Controls;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>以 Behavior 将 PasswordBox 的内存值绑定到视图模型，不使用业务代码后置事件。</summary>
public sealed class PasswordBoxBindingBehavior : Behavior<PasswordBox>
{
    /// <summary>双向绑定的密码依赖属性。</summary>
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.Register(nameof(Password), typeof(string),
        typeof(PasswordBoxBindingBehavior), new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    /// <summary>防止控件赋值和绑定回写互相递归。</summary>
    private bool _updating;

    /// <summary>只在控件和视图模型之间传递的密码值。</summary>
    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    /// <summary>附加控件时恢复绑定初值并注册输入回写。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PasswordChanged += OnInputChanged;
        AssociatedObject.Password = Password;
    }

    /// <summary>解除事件订阅，避免关闭窗口后继续持有控件。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.PasswordChanged -= OnInputChanged;
        base.OnDetaching();
    }

    /// <summary>依赖属性改变时同步控件，跳过已经相同或正在回写的值。</summary>
    private static void OnPasswordChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var behavior = (PasswordBoxBindingBehavior)sender;
        if (behavior.AssociatedObject is null || behavior._updating) return;
        behavior.AssociatedObject.Password = (string?)args.NewValue ?? string.Empty;
    }

    /// <summary>用户输入通过当前绑定回写，不替换 Password 属性上的 Binding。</summary>
    private void OnInputChanged(object sender, RoutedEventArgs args)
    {
        _updating = true;
        try { SetCurrentValue(PasswordProperty, AssociatedObject.Password); }
        finally { _updating = false; }
    }
}
