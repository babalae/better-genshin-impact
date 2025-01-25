using System.Diagnostics;
using System.Windows.Media;
using BetterGenshinImpact.GameTask;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.Helpers.Ui;

public class WindowHelper
{
    public static void TryApplySystemBackdrop(System.Windows.Window window)
    {
        if (OsVersionHelper.IsWindows11_OrGreater)
        {
            if (WindowBackdrop.IsSupported(TaskContext.Instance().Config.CommonConfig.CurrentBackdropType))
            {
                if (TaskContext.Instance().Config.CommonConfig.CurrentBackdropType == WindowBackdropType.Acrylic)
                {
                    window.Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
                }
                else
                {
                    window.Background = new SolidColorBrush(Colors.Transparent);
                }

                WindowBackdrop.ApplyBackdrop(window, TaskContext.Instance().Config.CommonConfig.CurrentBackdropType);
                return;
            }
            if (WindowBackdrop.IsSupported(WindowBackdropType.Mica))
            {
                window.Background = new SolidColorBrush(Colors.Transparent);
                WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);
            }
            else if (WindowBackdrop.IsSupported(WindowBackdropType.Acrylic))
            {
                window.Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
                WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Acrylic);
            }
        }
    }
}