using System.Windows;
using System.Windows.Shell;

namespace MicaSetup.Design.Controls;

public static class TaskbarExtension
{
    public static TaskbarItemInfo FindTaskbar(this FrameworkElement owner)
    {
        if ((owner is Window ? owner as Window : Window.GetWindow(owner)) is Window win)
        {
            return win.TaskbarItemInfo ??= new();
        }
        return null!;
    }

    public static TaskbarItemInfo FindTaskbar(this Window owner)
    {
        return owner.TaskbarItemInfo ??= new();
    }

    public static void SetProgress(this TaskbarItemInfo taskbarItem, double value, TaskbarItemProgressState state = TaskbarItemProgressState.Normal)
    {
        taskbarItem.ProgressState = state;
        taskbarItem.ProgressValue = value;
    }
}
