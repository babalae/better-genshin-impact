using Microsoft.Xaml.Behaviors;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace MicaSetup.Design.Controls;

public sealed class WindowClearOwnerOnClosingBehavior : Behavior<Window>
{
    protected override void OnAttached()
    {
        AssociatedObject.Closing += OnWindowClosing;
        base.OnAttached();
    }

    protected override void OnDetaching()
    {
        AssociatedObject.Closing -= OnWindowClosing;
        base.OnDetaching();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!e.Cancel)
        {
            try
            {
                if (AssociatedObject.Owner != null)
                {
                    AssociatedObject.Owner = null!;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Don't attach this {nameof(WindowClearOwnerOnClosingBehavior)} for {nameof(Window)} called from {nameof(Window.ShowDialog)}.", ex);
                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }
            }
        }
    }
}
