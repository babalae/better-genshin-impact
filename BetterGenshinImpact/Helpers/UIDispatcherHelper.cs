using System;
using System.Windows;
using System.Windows.Threading;

namespace BetterGenshinImpact.Helpers;

public static class UIDispatcherHelper
{
    public static Window MainWindow => Invoke(() => Application.Current?.MainWindow) ?? throw new InvalidOperationException();

    public static void Invoke(Action callback, params object[] args)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher == null)
        {
            return;
        }

        _ = dispatcher.Invoke(callback, args);
    }

    public static void Invoke(Action<Window> callback)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher == null)
        {
            return;
        }

        _ = dispatcher.Invoke(callback, MainWindow);
    }

    public static T Invoke<T>(Func<T> func)
        where T : class
    {
        var dispatcher = GetDispatcher();
        return dispatcher == null ? default! : dispatcher.Invoke(func);
    }

    public static void BeginInvoke(Action callback, params object[] args)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher == null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(callback, args);
    }

    public static void BeginInvoke(Action<Window> callback)
    {
        var dispatcher = GetDispatcher();
        if (dispatcher == null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(callback, MainWindow);
    }

    private static Dispatcher? GetDispatcher()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not { HasShutdownStarted: false, HasShutdownFinished: false })
        {
            return null;
        }

        return dispatcher;
    }
}
