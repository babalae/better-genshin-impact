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
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages;
using System.ComponentModel;
using Vanara.PInvoke;
using Wpf.Ui;

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
    /// 悬浮小窗服务，托盘菜单提供显示/隐藏、恢复与打开剪贴板链接。
    /// </summary>
    private readonly IGuideWindowService _guideWindowService;
    /// <summary>
    /// 主窗口导航，用于打开悬浮小窗设置页。
    /// </summary>
    private readonly INavigationService _navigationService;
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

    /// <summary>
    /// 托盘菜单是否展示悬浮小窗入口（只有主实例可用）。
    /// </summary>
    public bool IsGuideWindowAvailable => _guideWindowService.IsAvailable;

    /// <summary>
    /// 悬浮小窗显示/隐藏菜单项的文字，随小窗当前状态变化。
    /// </summary>
    public string GuideToggleText => _guideWindowService.State.IsShown ? "隐藏悬浮小窗" : "显示悬浮小窗";

    public NotifyIconViewModel(
        ChildSessionService childSessionService,
        InstanceService instanceService,
        InstanceVisualIdentity instanceVisualIdentity,
        DownloadCenterViewModel downloadCenter,
        IGuideWindowService guideWindowService,
        INavigationService navigationService)
    {
        _downloadCenter = downloadCenter;
        _childSessionService = childSessionService;
        _instanceService = instanceService;
        _instanceVisualIdentity = instanceVisualIdentity;
        _guideWindowService = guideWindowService;
        _navigationService = navigationService;
        IsInstanceIdentityVisible = !instanceVisualIdentity.IsPrimary
                                    || instanceService.GetConnectedChildInstances().Length > 0;

        if (instanceVisualIdentity.IsPrimary)
        {
            _instanceService.ConnectedInstancesChanged += OnConnectedInstancesChanged;
        }

        // 小窗显示状态变化时刷新托盘菜单文字
        _guideWindowService.State.PropertyChanged += OnGuideStateChanged;
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
        _guideWindowService.State.PropertyChanged -= OnGuideStateChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 小窗显示状态变化时通知托盘菜单文字更新（状态只在界面线程写入）。
    /// </summary>
    private void OnGuideStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuideWindowState.IsShown))
        {
            OnPropertyChanged(nameof(GuideToggleText));
        }
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

    /// <summary>
    /// 显示或隐藏悬浮小窗，未打开时打开。
    /// </summary>
    [RelayCommand]
    private void ToggleGuideWindow() => _guideWindowService.ToggleVisible();

    /// <summary>
    /// 一键恢复小窗：关闭鼠标穿透、不透明度 100% 并移回可见位置。
    /// </summary>
    [RelayCommand]
    private void RestoreGuideWindow() => _guideWindowService.RestoreEmergency();

    /// <summary>
    /// 在小窗中打开剪贴板里的视频链接。
    /// </summary>
    [RelayCommand]
    private void OpenGuideFromClipboard() => _guideWindowService.OpenFromClipboard();

    /// <summary>
    /// 显示主窗口并打开悬浮小窗设置页。
    /// </summary>
    [RelayCommand]
    private void OpenGuideSettings()
    {
        var main = Application.Current.MainWindow;
        if (main is null)
        {
            return;
        }

        main.Show();
        WindowBacktray.Show(main);
        main.Activate();
        _navigationService.Navigate(typeof(GuideWindowSettingsPage));
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
        // 子实例只退出当前进程；主实例退出前需要保护由其提供的共享能力。
        if (!_instanceVisualIdentity.IsPrimary)
        {
            await SaveAndShutdownAsync();
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

        await SaveAndShutdownAsync();
    }

    /// <summary>
    /// 确认下载并保存配音草稿、共享配置后，关闭当前实例进程。
    /// </summary>
    private async Task SaveAndShutdownAsync()
    {
        if (!await QuestVoiceSettingsExitGuard.PrepareAsync()) return;
        // 有下载正在进行时确认退出，已下载的部分保留供下次续传。
        // 不让尚未保存的配音草稿消耗本次下载退出确认。
        if (!_downloadCenter.ConfirmExit())
        {
            return;
        }

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
