using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace MicaSetup.Design.Controls;

public class SetupProgressBar : ProgressBar
{
    public bool SyncToWindowTaskbar
    {
        get => (bool)GetValue(LinkToWindowTaskbarProperty);
        set => SetValue(LinkToWindowTaskbarProperty, value);
    }

    public static readonly DependencyProperty LinkToWindowTaskbarProperty = DependencyProperty.Register("SyncToWindowTaskbar", typeof(bool), typeof(SetupProgressBar), new PropertyMetadata(true));

    public SetupProgressBar()
    {
        ValueChanged += OnValueChanged;
    }

    private void OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!SyncToWindowTaskbar)
        {
            return;
        }

        if (this.FindTaskbar() is TaskbarItemInfo taskbar)
        {
            if (Value >= 0d)
            {
                taskbar.ProgressValue = Value / 100d;

                if (Value >= 100d)
                {
                    taskbar.ProgressState = TaskbarItemProgressState.None;
                }
                else
                {
                    taskbar.ProgressState = TaskbarItemProgressState.Normal;
                }
            }
            else
            {
                taskbar.ProgressValue = 0d;
                taskbar.ProgressState = TaskbarItemProgressState.Indeterminate;
            }
        }
    }
}
