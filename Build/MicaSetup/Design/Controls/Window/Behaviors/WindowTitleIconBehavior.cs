using MicaSetup.Helper;
using MicaSetup.Natives;
using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Input;

namespace MicaSetup.Design.Controls;

public sealed class WindowTitleIconBehavior : Behavior<FrameworkElement>
{
    protected override void OnAttached()
    {
        AssociatedObject.Loaded += Loaded;
        base.OnAttached();
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= Loaded;
        base.OnDetaching();
    }

    private void Loaded(object sender, EventArgs e)
    {
        AssociatedObject.RegisterAsTitleIcon();
    }
}

public static class RegisterAsTitleIconBehaviorExtension
{
    public static void RegisterAsTitleIcon(this UIElement self)
    {
        self.MouseLeftButtonDown += (s, e) =>
        {
            if (s is UIElement titleHeader && Window.GetWindow(titleHeader) is Window window)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    if (User32.GetCursorPos(out POINT pt))
                    {
                        SystemCommands.ShowSystemMenu(window, new Point(DpiHelper.CalcDPiX(pt.X), DpiHelper.CalcDPiY(pt.Y)));
                    }
                }
            }
        };
    }
}
