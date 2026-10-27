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
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages;
using Vanara.PInvoke;

namespace BetterGenshinImpact.ViewModel;

public partial class NotifyIconViewModel : ObservableObject
{
    private readonly ChildSessionService _childSessionService;
    /// <summary>
    /// 提供主实例当前连接的 WebView2 子实例状态。
    /// </summary>
    private readonly InstanceService _instanceService;

    public bool IsChildSessionEntryVisible => InstanceBootstrap.Current.Context.IsRoot;

    public NotifyIconViewModel(
        ChildSessionService childSessionService,
        InstanceService instanceService)
    {
        _childSessionService = childSessionService;
        _instanceService = instanceService;
    }

    [RelayCommand]
    private void OpenChildSessionWindow()
    {
        _childSessionService.ShowWindow();
    }

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
    public async Task ExitAsync()
    {
        // WebView2 实例依赖主实例提供 IPC 服务，应先关闭网页版实例再退出主实例。
        if (_instanceService.HasConnectedWebViewInstance())
        {
            ThemedMessageBox.Warning(
                "WebView2 云原神实例仍在运行，请先关闭所有 WebView2 实例，再退出 BetterGI。",
                "WebView2 实例未关闭");
            return;
        }

        if (_childSessionService.HasActiveChildSession())
        {
            ThemedMessageBox.Warning(
                "桌面分身仍在运行，请先关闭桌面分身，再退出 BetterGI。",
                "桌面分身未关闭");
            return;
        }

        if (!await QuestVoiceSettingsExitGuard.PrepareAsync()) return;
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
