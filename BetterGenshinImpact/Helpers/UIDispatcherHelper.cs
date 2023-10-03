using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.Helpers;

public static class UIDispatcherHelper
{
    public static Window MainWindow => Application.Current.Dispatcher.Invoke(() => Application.Current.MainWindow) ?? throw new InvalidOperationException();

    public static void Invoke(Action callback, params object[] args)
    {
        _ = Application.Current?.Dispatcher.Invoke(callback, args);
    }

    public static void Invoke(Action<Window> callback)
    {
        _ = Application.Current?.Dispatcher.Invoke(callback, MainWindow);
    }

    public static T Invoke<T>(Func<T> action)
    {
        T t = default;
        Application.Current?.Dispatcher.Invoke(() => t = action());
        return t;
    }

    public static void BeginInvoke(Action callback, params object[] args)
    {
        _ = Application.Current?.Dispatcher.BeginInvoke(callback, args);
    }

    public static void BeginInvoke(Action<Window> callback)
    {
        _ = Application.Current?.Dispatcher.BeginInvoke(callback, MainWindow);
    }
}