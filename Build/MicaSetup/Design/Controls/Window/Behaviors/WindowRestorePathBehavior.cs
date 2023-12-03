using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MicaSetup.Design.Controls;

public sealed class WindowRestorePathBehavior : Behavior<FrameworkElement>
{
    private WindowState windowStatePrev = WindowState.Normal;

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
        if (sender is UIElement maximizeButtonContent && Window.GetWindow(maximizeButtonContent) is Window window)
        {
            window.SizeChanged += (s, e) =>
            {
                if (windowStatePrev != window.WindowState)
                {
                    if (maximizeButtonContent is Path path)
                    {
                        if (window.WindowState == WindowState.Maximized)
                        {
                            path.Data = Geometry.Parse("M0 0.2 L0.8 0.2 M0 01 L0.8 1 M0.8 1 L0.8 0.2 M0 0.2 L0 1 M0.3 0 L0.95 0 M01 0.05 L1 0.7");
                        }
                        else
                        {
                            path.Data = Geometry.Parse("M0.025 0 L0.975 0 M0.025 1 L0.975 1 M1 0.975 L1 0.025 M0 0.025 L0 0.975");
                        }
                    }
                    windowStatePrev = window.WindowState;
                }
            };
        }
    }
}
