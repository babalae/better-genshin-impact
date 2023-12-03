using MicaSetup.Natives;
using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Interop;

namespace MicaSetup.Design.Controls;

public sealed class WindowHideTitleButtonBehavior : Behavior<Window>
{
    protected override void OnAttached()
    {
        AssociatedObject.SourceInitialized += OnSourceInitialized;
        base.OnAttached();
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Loaded -= OnSourceInitialized;
        base.OnDetaching();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        NativeMethods.HideAllWindowButton(new WindowInteropHelper(AssociatedObject).Handle);
    }
}
