using BetterGenshinImpact.Service.Interface;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;

namespace BetterGenshinImpact.ViewModel;

public partial class NotifyIconViewModel : ObservableObject
{
    [RelayCommand]
    public void ShowOrHide()
    {
        if (Application.Current.MainWindow.Visibility == Visibility.Visible)
        {
            Application.Current.MainWindow.Hide();
        }
        else
        {
            Application.Current.MainWindow.Activate();
            Application.Current.MainWindow.Focus();
            Application.Current.MainWindow.Show();
            WindowBacktray.Show(Application.Current.MainWindow);
        }
    }

    [RelayCommand]
    public void Exit()
    {
        App.GetService<IConfigService>()?.Save();
        Application.Current.Shutdown();
    }
}

file static class WindowBacktray
{
    public static void Hide(Window window)
    {
        if (window != null)
        {
            window.Visibility = Visibility.Hidden;
            window.WindowState = WindowState.Minimized;
        }
    }

    public static void Show(Window window)
    {
        if (window != null)
        {
            if (window.Visibility != Visibility.Visible)
            {
                window.Visibility = Visibility.Visible;
            }
            if (window.WindowState == WindowState.Minimized)
            {
                nint hWnd = new WindowInteropHelper(Application.Current.MainWindow).Handle;
                _ = User32.SendMessage(hWnd, User32.WindowMessage.WM_SYSCOMMAND, User32.SysCommand.SC_RESTORE, IntPtr.Zero);
            }
        }
    }
}