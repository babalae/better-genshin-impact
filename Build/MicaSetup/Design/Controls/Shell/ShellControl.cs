using System;
using System.Windows;
using System.Windows.Controls;

namespace MicaSetup.Design.Controls;

public class ShellControl : ContentControl
{
    public string Route
    {
        get => (string)GetValue(RouteProperty);
        set => SetCurrentValue(RouteProperty, value);
    }

    public static readonly DependencyProperty RouteProperty = DependencyProperty.Register(nameof(Route), typeof(string), typeof(ShellControl), new(string.Empty));

    public ShellControl()
    {
        Routing.Shell = new WeakReference<ShellControl>(this);
        FocusVisualStyle = null!;
        Loaded += (_, _) => Routing.GoTo(Route);
    }
}
