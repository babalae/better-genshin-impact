using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Controls.Webview;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using BetterGenshinImpact.Model;
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

    [RelayCommand]
    public async Task CheckUpdateAsync()
    {
        // 检查更新
        await App.GetService<IUpdateService>()!.CheckUpdateAsync(new UpdateOption
        {
            Trigger = UpdateTrigger.Manual
        });
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