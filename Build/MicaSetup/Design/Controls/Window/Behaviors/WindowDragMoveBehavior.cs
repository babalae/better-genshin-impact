using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;

namespace MicaSetup.Design.Controls;

public sealed class WindowDragMoveBehavior : Behavior<FrameworkElement>
{
    protected override void OnAttached()
    {
        AssociatedObject.MouseLeftButtonDown += MouseLeftButtonDown;
        base.OnAttached();
    }

    protected override void OnDetaching()
    {
        AssociatedObject.MouseLeftButtonDown -= MouseLeftButtonDown;
        base.OnDetaching();
    }

    private void MouseLeftButtonDown(object sender, EventArgs e)
    {
        if (sender is UIElement ui)
        {
            Window.GetWindow(ui)?.DragMove();
        }
    }
}
