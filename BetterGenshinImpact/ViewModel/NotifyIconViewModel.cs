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
using System.Windows.Media;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Windows;
using Vanara.PInvoke;

namespace BetterGenshinImpact.ViewModel;

public partial class NotifyIconViewModel : ObservableObject, IDisposable
{
    private readonly ChildSessionService _childSessionService;
    /// <summary>
    /// 为当前托盘入口提供实例名称、角色和强调色。
    /// </summary>
    private readonly InstanceVisualIdentity _instanceVisualIdentity;
    /// <summary>
    /// 提供主实例当前连接的 WebView2 子实例状态。
    /// </summary>
    private readonly InstanceService _instanceService;
    /// <summary>
    /// 退出前确认是否有下载正在进行。
    /// </summary>
    private readonly DownloadCenterViewModel _downloadCenter;
    /// <summary>
    /// 标记 ViewModel 已释放，防止退出阶段处理迟到的实例事件。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 子实例始终展示身份；主实例仅在至少一个子实例在线时展示身份。
    /// </summary>
    [ObservableProperty]
    private bool _isInstanceIdentityVisible;

    /// <summary>
    /// 当前托盘菜单是否可以展示主实例专属的维护与实例管理入口。
    /// </summary>
    public bool IsPrimaryInstance => _instanceVisualIdentity.IsPrimary;

    /// <summary>
    /// 当前实例在托盘菜单顶部展示的名称。
    /// </summary>
    public string InstanceDisplayName => _instanceVisualIdentity.DisplayName;

    /// <summary>
    /// 根据实例角色明确退出动作的作用范围。
    /// </summary>
    public string ExitMenuText => _instanceVisualIdentity.IsPrimary ? "退出 BetterGI" : "退出此实例";

    /// <summary>
    /// 当前实例在托盘菜单中使用的强调色画刷。
    /// </summary>
    public Brush InstanceAccentBrush => _instanceVisualIdentity.AccentBrush;

    public NotifyIconViewModel(
        ChildSessionService childSessionService,
        InstanceService instanceService,
        InstanceVisualIdentity instanceVisualIdentity,
        DownloadCenterViewModel downloadCenter)
    {
        _downloadCenter = downloadCenter;
        _childSessionService = childSessionService;
        _instanceService = instanceService;
        _instanceVisualIdentity = instanceVisualIdentity;
        IsInstanceIdentityVisible = !instanceVisualIdentity.IsPrimary
                                    || instanceService.GetConnectedChildInstances().Length > 0;

        if (instanceVisualIdentity.IsPrimary)
        {
            _instanceService.ConnectedInstancesChanged += OnConnectedInstancesChanged;
        }
    }

    /// <summary>
    /// 释放主实例托盘菜单持有的实例状态事件订阅。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _instanceService.ConnectedInstancesChanged -= OnConnectedInstancesChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 在实例连接变化后刷新托盘菜单中的主实例身份入口。
    /// </summary>
    private void OnConnectedInstancesChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (_disposed || dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            RefreshInstanceIdentityVisibility();
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(RefreshInstanceIdentityVisibility));
    }

    /// <summary>
    /// 根据主实例当前的子实例连接快照刷新身份入口可见性。
    /// </summary>
    private void RefreshInstanceIdentityVisibility()
    {
        if (!_disposed)
        {
            IsInstanceIdentityVisible = _instanceService.GetConnectedChildInstances().Length > 0;
        }
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
    public void Exit()
    {
        // 子实例只退出当前进程；主实例退出前需要保护由其提供的共享能力。
        if (!_instanceVisualIdentity.IsPrimary)
        {
            // 有下载正在进行时确认退出，已下载的部分保留供下次续传
            if (_downloadCenter.ConfirmExit())
            {
                SaveAndShutdown();
            }

            return;
        }

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

        // 有下载正在进行时确认退出，已下载的部分保留供下次续传
        if (!_downloadCenter.ConfirmExit())
        {
            return;
        }

        SaveAndShutdown();
    }

    /// <summary>
    /// 保存共享配置并关闭当前实例进程。
    /// </summary>
    private static void SaveAndShutdown()
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
