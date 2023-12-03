using MicaSetup.Helper;
using MicaSetup.Natives;
using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Input;

namespace MicaSetup.Design.Controls;

public sealed class WindowTitleHeaderBehavior : Behavior<FrameworkElement>
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
        AssociatedObject.RegisterAsTitleHeader();
    }
}

file static class RegisterAsTitleHeaderBehaviorExtension
{
    public static void RegisterAsTitleHeader(this UIElement self)
    {
        self.MouseLeftButtonDown += (s, e) =>
        {
            if (s is UIElement titleHeader && Window.GetWindow(titleHeader) is Window window)
            {
                if (e.ClickCount == 2)
                {
                    if (window.ResizeMode == ResizeMode.CanResize || window.ResizeMode == ResizeMode.CanResizeWithGrip)
                    {
                        switch (window.WindowState)
                        {
                            case WindowState.Normal:
                                WindowSystemCommands.MaximizeWindow(window);
                                break;

                            case WindowState.Maximized:
                                WindowSystemCommands.RestoreWindow(window);
                                break;
                        }
                    }
                }
                else
                {
                    if (e.LeftButton == MouseButtonState.Pressed)
                    {
                        WindowSystemCommands.FastRestoreWindow(window);
                    }
                }
            }
        };

        self.MouseRightButtonDown += (s, e) =>
        {
            if (s is UIElement titleHeader && Window.GetWindow(titleHeader) is Window window)
            {
                if (window.Cursor != null && window.Cursor.ToString() == Cursors.None.ToString())
                {
                    return;
                }
                if (e.RightButton == MouseButtonState.Pressed)
                {
                    if (User32.GetCursorPos(out POINT pt))
                    {
                        e.Handled = true;
                        SystemCommands.ShowSystemMenu(window, new Point(DpiHelper.CalcDPiX(pt.X), DpiHelper.CalcDPiY(pt.Y)));
                    }
                }
            }
        };
    }
}
