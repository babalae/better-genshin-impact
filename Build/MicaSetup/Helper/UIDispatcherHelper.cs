using System;
using System.Windows;

namespace MicaSetup.Helper;

public static class UIDispatcherHelper
{
    public static Window MainWindow => Application.Current.Dispatcher.Invoke(() => Application.Current.MainWindow);

    public static void Invoke(Action callback, params object[] args)
    {
        _ = Application.Current?.Dispatcher.Invoke(callback, args);
    }

    public static void Invoke(Action<Window> callback)
    {
        Application.Current?.Dispatcher.Invoke(callback, MainWindow);
    }

    public static void BeginInvoke(Action callback, params object[] args)
    {
        _ = Application.Current?.Dispatcher.BeginInvoke(callback, args);
    }

    public static void BeginInvoke(Action<Window> callback)
    {
        Application.Current?.Dispatcher.BeginInvoke(callback, MainWindow);
    }
}
