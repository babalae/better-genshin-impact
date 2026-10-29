using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.UseRedeemCode;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeviceId;
using Fischless.GameCapture;
using Fischless.GameCapture.BitBlt;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Infrastructure.Network;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.ViewModel.Windows;
using Newtonsoft.Json;
using Wpf.Ui;
using Wpf.Ui.Controls;

using BetterGenshinImpact.Platform.Wine;

namespace BetterGenshinImpact.ViewModel;

public partial class MainWindowViewModel : ObservableObject, IViewModel, IDisposable
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IConfigService _configService;
    private readonly INavigationService _navigationService;
    private readonly ChildSessionService _childSessionService;
    /// <summary>
    /// 为当前实例提供跨窗口与托盘一致的名称、颜色和图标。
    /// </summary>
    private readonly InstanceVisualIdentity _instanceVisualIdentity;
    /// <summary>
    /// 提供在线子实例快照、状态变化通知与窗口激活能力。
    /// </summary>
    private readonly InstanceService _instanceService;
    private readonly IInferenceModelDiagnostics _inferenceDiagnostics;
    private readonly InferenceRuntimeService _inferenceRuntime;
    /// <summary>
    /// 悬浮小窗服务，主窗口激活时询问是否在小窗打开剪贴板中的视频链接。
    /// </summary>
    private readonly IGuideWindowService _guideWindowService;
    /// <summary>
    /// 上一次询问过的剪贴板视频链接，同一链接只询问一次。
    /// </summary>
    private string? _lastGuideClipboardUrl;
    private bool _inferenceStartupHandled;
    /// <summary>
    /// 标记 ViewModel 已释放，防止退出阶段处理迟到的实例事件。
    /// </summary>
    private bool _disposed;
    /// <summary>正常关闭的异步保存守卫只放行一次，避免递归关闭。</summary>
    private bool _questVoiceCloseApproved;
    /// <summary>多个关闭请求共用一次排空，不重复弹出提示。</summary>
    private bool _questVoiceClosePending;
    /// <summary>
    /// 不包含实例身份的应用标题和版本信息。
    /// </summary>
    public string Title => _instanceVisualIdentity.ApplicationTitle;

    /// <summary>
    /// 当前实例在 Windows 标题、任务栏和 Alt+Tab 中展示的完整标题。
    /// </summary>
    public string WindowTitle => IsInstanceIdentityVisible
        ? _instanceVisualIdentity.WindowTitle
        : _instanceVisualIdentity.ApplicationTitle;

    /// <summary>
    /// 子实例始终展示身份；主实例仅在至少一个子实例在线时展示身份。
    /// </summary>
    public bool IsInstanceIdentityVisible =>
        !_instanceVisualIdentity.IsPrimary || HasConnectedChildInstances;

    /// <summary>
    /// 当前实例在标题栏标签中展示的名称。
    /// </summary>
    public string InstanceTagText => _instanceVisualIdentity.DisplayName;

    /// <summary>
    /// 当前实例的稳定强调色画刷。
    /// </summary>
    public Brush InstanceAccentBrush => _instanceVisualIdentity.AccentBrush;

    /// <summary>
    /// 当前实例在窗口、标题栏和托盘中共用的图标。
    /// </summary>
    public ImageSource InstanceIconSource => IsInstanceIdentityVisible
        ? _instanceVisualIdentity.IconSource
        : _instanceVisualIdentity.DefaultIconSource;

    /// <summary>
    /// 当前实例的托盘提示文本。
    /// </summary>
    public string TrayTooltipText => IsInstanceIdentityVisible
        ? _instanceVisualIdentity.TrayTooltipText
        : "BetterGI";

    public bool IsChildSessionInstance =>
        InstanceBootstrap.Current.Context.InstanceType == BetterGiInstanceType.ChildSession;

    [ObservableProperty] private bool _isVisible = true;

    /// <summary>
    /// 主实例当前已连接的全部子实例展示项。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleConnectedChildInstances))]
    [NotifyPropertyChangedFor(nameof(ConnectedInstanceOverflowCount))]
    [NotifyPropertyChangedFor(nameof(HasConnectedChildInstances))]
    [NotifyPropertyChangedFor(nameof(HasConnectedInstanceOverflow))]
    [NotifyPropertyChangedFor(nameof(IsInstanceIdentityVisible))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(InstanceIconSource))]
    [NotifyPropertyChangedFor(nameof(TrayTooltipText))]
    private IReadOnlyList<ConnectedInstanceItem> _connectedChildInstances = [];

    /// <summary>
    /// 标题栏中直接展示的前三个在线子实例。
    /// </summary>
    public IReadOnlyList<ConnectedInstanceItem> VisibleConnectedChildInstances =>
        ConnectedInstanceItem.GetVisibleItems(ConnectedChildInstances);

    /// <summary>
    /// 未直接显示在标题栏中的在线子实例数量。
    /// </summary>
    public int ConnectedInstanceOverflowCount =>
        ConnectedInstanceItem.GetOverflowCount(ConnectedChildInstances);

    /// <summary>
    /// 当前是否存在需要展示的在线子实例。
    /// </summary>
    public bool HasConnectedChildInstances => ConnectedChildInstances.Count > 0;

    /// <summary>
    /// 当前在线子实例是否超过标题栏直接展示上限。
    /// </summary>
    public bool HasConnectedInstanceOverflow => ConnectedInstanceOverflowCount > 0;

    [ObservableProperty] private WindowState _windowState = WindowState.Normal;

    [ObservableProperty] private WindowBackdropType _currentBackdropType = WindowBackdropType.Auto;

    [ObservableProperty] private bool _isWin11Later = OsVersionHelper.IsWindows11_OrGreater;
    
    [ObservableProperty] private Brush _redeemCodeButtonForeground = Brushes.White;

    private string? _redeemCodeCnUpdateNewVersion;

    private string? _redeemCodeGlobalUpdateNewVersion;

    private CancellationTokenSource? _redeemCodeDismissCts;

    private bool HasPendingRedeemCodeUpdate => _redeemCodeCnUpdateNewVersion != null || _redeemCodeGlobalUpdateNewVersion != null;

    [ObservableProperty] private bool _isRedeemCodeInfoBarOpen;

    /// <summary>
    /// 主窗口自定义背景图源，null 表示未加载或加载失败
    /// </summary>
    [ObservableProperty] private ImageSource? _mainBackgroundSource;

    /// <summary>
    /// 主窗口自定义背景图是否可见（开关开启且图片加载成功）
    /// </summary>
    [ObservableProperty] private bool _isMainBackgroundVisible;

    partial void OnIsRedeemCodeInfoBarOpenChanged(bool value)
    {
        if (!value)
        {
            // 取消自动消失计时器
            _redeemCodeDismissCts?.Cancel();
            _redeemCodeDismissCts?.Dispose();
            _redeemCodeDismissCts = null;
        }
    }

    private bool _firstActivated = true;

    public AllConfig Config { get; set; }

    /// <summary>
    /// 标题栏下载中心
    /// </summary>
    public DownloadCenterViewModel DownloadCenter { get; }

    public MainWindowViewModel(
        INavigationService navigationService,
        IConfigService configService,
        ChildSessionService childSessionService,
        InstanceService instanceService,
        InstanceVisualIdentity instanceVisualIdentity,
        IInferenceModelDiagnostics inferenceDiagnostics,
        InferenceRuntimeService inferenceRuntime,
        DownloadCenterViewModel downloadCenter,
        IGuideWindowService guideWindowService)
    {
        DownloadCenter = downloadCenter;
        _guideWindowService = guideWindowService;
        _inferenceDiagnostics = inferenceDiagnostics;
        _inferenceRuntime = inferenceRuntime;
        _navigationService = navigationService;
        _configService = configService;
        _childSessionService = childSessionService;
        _instanceService = instanceService;
        _instanceVisualIdentity = instanceVisualIdentity;
        Config = _configService.Get();
        _logger = App.GetLogger<MainWindowViewModel>();
        // 订阅通用配置变更：设置页修改背景图路径或开关后，主窗口背景实时刷新
        Config.CommonConfig.PropertyChanged += OnCommonConfigPropertyChanged;
        LoadMainBackground();

        if (_instanceService.Context.IsRoot)
        {
            _instanceService.ConnectedInstancesChanged += OnConnectedInstancesChanged;
            RefreshConnectedChildInstances();
        }
    }

    /// <summary>
    /// 释放主窗口 ViewModel 持有的事件订阅和可取消资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Config.CommonConfig.PropertyChanged -= OnCommonConfigPropertyChanged;
        _instanceService.ConnectedInstancesChanged -= OnConnectedInstancesChanged;
        _redeemCodeDismissCts?.Cancel();
        _redeemCodeDismissCts?.Dispose();
        _redeemCodeDismissCts = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 将实例连接变化切回 UI 线程后刷新标题栏集合。
    /// </summary>
    private void OnConnectedInstancesChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            RefreshConnectedChildInstances();
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(RefreshConnectedChildInstances));
    }

    /// <summary>
    /// 从实例服务读取一致快照并生成标题栏展示项。
    /// </summary>
    private void RefreshConnectedChildInstances()
    {
        if (_disposed || !_instanceService.Context.IsRoot)
        {
            return;
        }

        ConnectedChildInstances = ConnectedInstanceItem.CreateItems(
            _instanceService.GetConnectedChildInstances());
    }

    /// <summary>
    /// 打开桌面分身容器并唤起指定子实例窗口。
    /// </summary>
    /// <param name="item">用户点击的在线子实例展示项。</param>
    [RelayCommand]
    private async Task ActivateConnectedInstanceAsync(ConnectedInstanceItem? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            if (item.Endpoint.InstanceType == BetterGiInstanceType.ChildSession)
            {
                _childSessionService.ShowWindow();
            }

            await _instanceService.ActivateChildInstanceAsync(item.Endpoint);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or IOException
                                          or TimeoutException
                                          or OperationCanceledException)
        {
            _logger.LogWarning(exception, "唤起子实例失败：{Instance}", item.DisplayName);
            RefreshConnectedChildInstances();
            ThemedMessageBox.Warning(
                $"无法唤起「{item.DisplayName}」，该实例可能已经离线。",
                "实例不可用");
        }
    }

    /// <summary>
    /// 请求在线子实例正常退出；桌面分身会在关闭其中的 BetterGI 后注销远程桌面会话。
    /// </summary>
    /// <param name="item">用户选择关闭的在线子实例展示项。</param>
    [RelayCommand]
    private async Task CloseConnectedInstanceAsync(ConnectedInstanceItem? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            if (item.Endpoint.InstanceType == BetterGiInstanceType.ChildSession)
            {
                await CloseChildSessionInstanceAsync(item);
            }
            else
            {
                await _instanceService.CloseChildInstanceAsync(item.Endpoint);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "关闭子实例失败：{Instance}", item.DisplayName);
            RefreshConnectedChildInstances();
            var action = item.Endpoint.InstanceType == BetterGiInstanceType.ChildSession
                ? "关闭并注销"
                : "关闭";
            ThemedMessageBox.Warning(
                $"无法{action}「{item.DisplayName}」：{exception.GetBaseException().Message}",
                "实例不可用");
        }
    }

    /// <summary>
    /// 先请求桌面分身中的 BetterGI 正常退出，再断开 RDP、注销 Child Session 并隐藏分身窗口。
    /// 即使 BetterGI 已经离线，也继续注销会话，保证“关闭实例”不会遗留远程桌面会话。
    /// </summary>
    /// <param name="item">需要关闭并注销的桌面分身展示项。</param>
    private async Task CloseChildSessionInstanceAsync(ConnectedInstanceItem item)
    {
        try
        {
            await _instanceService.CloseChildInstanceAsync(item.Endpoint);
        }
        catch (Exception exception)
        {
            // 子进程可能恰好先退出；注销 Session 仍能完成用户要求的最终关闭动作。
            _logger.LogWarning(
                exception,
                "请求桌面分身 BetterGI 正常退出失败，将继续注销会话：{Instance}",
                item.DisplayName);
        }

        await _childSessionService.LogoffAndHideAsync(
            checked((uint)item.Endpoint.WindowsSessionId));
    }

    private void OnCommonConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 仅在背景图相关配置变化时重新加载，避免无关配置变更触发图片 IO
        if (e.PropertyName is nameof(CommonConfig.MainBackgroundImagePath)
            or nameof(CommonConfig.MainBackgroundEnabled))
        {
            LoadMainBackground();
        }
    }

    /// <summary>
    /// 加载主窗口自定义背景图。
    /// 使用 CacheOption.OnLoad 立即读入内存，不锁定图片文件，方便用户随时替换或删除图片。
    /// 加载失败（文件被删/格式损坏）时静默降级为隐藏背景，不弹窗打断用户。
    /// </summary>
    private void LoadMainBackground()
    {
        ImageSource? source = null;
        var path = Config.CommonConfig.MainBackgroundImagePath;
        if (Config.CommonConfig.MainBackgroundEnabled && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                // 以虚拟屏幕（多显示器总范围）宽度为解码上限：
                // 背景最多铺满屏幕，超过该宽度再高的分辨率也无处可显示，
                // 限制解码尺寸可避免超大照片同步解码卡 UI 并占用数百 MB 内存；
                // DecodePixelWidth 不会放大小图（小于该宽度的图片按原始尺寸解码）
                bitmap.DecodePixelWidth = (int)SystemParameters.VirtualScreenWidth;
                bitmap.EndInit();
                bitmap.Freeze();
                source = bitmap;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"加载主窗口背景图失败：{path}，{ex.Message}");
            }
        }

        MainBackgroundSource = source;
        IsMainBackgroundVisible = source != null;
    }

    [RelayCommand]
    private async Task OnActivated()
    {
        // 首次激活时不处理
        if (_firstActivated)
        {
            _firstActivated = false;
            return;
        }

        // 激活时候获取剪切板内容 用于脚本导入、兑换码自动兑换等
        try
        {
            if (Clipboard.ContainsText())
            {
                string clipboardText = Clipboard.GetText();

                if (string.IsNullOrEmpty(clipboardText)
                    || clipboardText.Length > 1000)
                {
                    return;
                }


                // 1. 导入脚本
                await ScriptRepoUpdater.Instance.ImportScriptFromClipboard(clipboardText);
                // 2. 自动兑换码
                await RedeemCodeManager.ImportFromClipboard(clipboardText);
                // 3. B 站视频链接：询问是否在悬浮小窗打开
                await PromptOpenGuideFromClipboardAsync(clipboardText);
            }
        }
        catch
        {
            // 忽略异常，可能是因为没有权限访问剪切板
        }
    }

    /// <summary>
    /// 剪贴板中有 B 站视频链接时询问是否在悬浮小窗打开；同一链接只询问一次，小窗正在播放该视频时不询问。
    /// </summary>
    private async Task PromptOpenGuideFromClipboardAsync(string clipboardText)
    {
        if (!_guideWindowService.IsAvailable || !_guideWindowService.Config.ClipboardPromptEnabled)
        {
            return;
        }

        if (!GuideLinkResolver.TryResolve(clipboardText, allowAnyUrl: false, out var link)
            || string.Equals(link.Url, _lastGuideClipboardUrl, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastGuideClipboardUrl = link.Url;
        var videoKey = GuideLinkResolver.GetVideoKey(link.Url);
        var state = _guideWindowService.State;
        if (videoKey is not null && state.IsOpen && videoKey == GuideLinkResolver.GetVideoKey(state.Url))
        {
            return;
        }

        var result = await ThemedMessageBox.QuestionAsync(
            $"剪贴板中有 B 站视频链接：\n{link.Url}\n\n要在悬浮小窗中打开吗？\n\n可以在“悬浮小窗”设置页关闭这个提示。",
            "悬浮小窗");
        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _guideWindowService.Open(new GuideOpenRequest(link.Url, GuideOpenSource.Clipboard));
        }
    }

    [RelayCommand]
    private void OnHide()
    {
        IsVisible = false;
    }

    [RelayCommand]
    private void OnSwitchBackdrop()
    {
        // Windows11_22523以下版本支持基本深浅主题切换，以上版本额外还支持Mica/Acrylic主题切换
        if (!OsVersionHelper.IsWindows11_22523_OrGreater)
        {
            Config.CommonConfig.CurrentThemeType = Config.CommonConfig.CurrentThemeType switch
            {
                ThemeType.DarkNone => ThemeType.LightNone,
                ThemeType.LightNone => ThemeType.DarkNone,
                _ => ThemeType.DarkNone
            };
        }
        else
        {
            Config.CommonConfig.CurrentThemeType = Config.CommonConfig.CurrentThemeType switch
            {
                ThemeType.DarkMica => ThemeType.DarkAcrylic,
                ThemeType.DarkAcrylic => ThemeType.LightMica,
                ThemeType.LightMica => ThemeType.LightAcrylic,
                ThemeType.LightAcrylic => ThemeType.DarkMica,
                _ => ThemeType.DarkMica
            };
        }

        ApplyTheme(Config.CommonConfig.CurrentThemeType);
        _configService.Save();
    }

    private void ApplyTheme(ThemeType themeType)
    {
        var originalThemeType = themeType;

        // 根据主题类型设置应用程序主题（深色/浅色）和背景效果类型（Mica/Acrylic/None）
        if (!OsVersionHelper.IsWindows11_22523_OrGreater)
        {
            // 22523以下版本只支持深浅色切换,修正背景材质为纯色
            if (themeType == ThemeType.DarkMica || themeType == ThemeType.DarkAcrylic)
            {
                themeType = ThemeType.DarkNone;
            }
            else if (themeType == ThemeType.LightMica || themeType == ThemeType.LightAcrylic)
            {
                themeType = ThemeType.LightNone;
            }
        }

        // 如果主题类型被修正，更新配置并保存
        if (themeType != originalThemeType)
        {
            Config.CommonConfig.CurrentThemeType = themeType;
            _configService.Save();
            _logger.LogInformation($"主题类型已从 {originalThemeType} 修正为 {themeType}，因为当前系统不支持该主题效果");
        }

        if (WinePlatformAddon.IsRunningOnWine)
        {
            // Wine 平台下不应用主题
            _logger.LogInformation("检测到运行在 Wine 平台，跳过主题应用");
            return;
        }

        switch (themeType)
        {
            case ThemeType.DarkNone:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                CurrentBackdropType = WindowBackdropType.None;
                break;
            case ThemeType.DarkMica:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                CurrentBackdropType = WindowBackdropType.Mica;
                break;
            case ThemeType.DarkAcrylic:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                CurrentBackdropType = WindowBackdropType.Acrylic;
                break;
            case ThemeType.LightNone:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
                CurrentBackdropType = WindowBackdropType.None;
                break;
            case ThemeType.LightMica:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
                CurrentBackdropType = WindowBackdropType.Mica;
                break;
            case ThemeType.LightAcrylic:
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
                CurrentBackdropType = WindowBackdropType.Acrylic;
                break;
        }

        Wpf.Ui.Violeta.Appearance.SystemMenuThemeManager.Apply(
            themeType is ThemeType.DarkNone or ThemeType.DarkMica or ThemeType.DarkAcrylic
                ? Wpf.Ui.Violeta.Appearance.SystemMenuTheme.Dark
                : Wpf.Ui.Violeta.Appearance.SystemMenuTheme.Light);

        // 立即应用主题到当前窗口
        if (Application.Current.MainWindow != null)
        {
            WindowHelper.ApplyThemeToWindow(Application.Current.MainWindow, themeType);
        }

        // 根据当前主题更新兑换码按钮的默认前景色（若无更新高亮）
        if (!HasPendingRedeemCodeUpdate)
        {
            UpdateRedeemCodeButtonDefaultForeground();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task OnClosingAsync(CancelEventArgs e)
    {
        // “关闭到托盘”只隐藏当前实例，不应因为其他实例仍在运行而阻止隐藏窗口。
        if (Config.CommonConfig.ExitToTray)
        {
            e.Cancel = true;
            OnHide();
            return;
        }

        // 子实例只负责自己的生命周期；只有主实例退出时需要保护其提供的共享能力。
        // WebView2 实例依赖主实例提供 IPC 服务，应先关闭网页版实例再退出主实例。
        if (_instanceVisualIdentity.IsPrimary && _instanceService.HasConnectedWebViewInstance())
        {
            e.Cancel = true;
            ThemedMessageBox.Warning(
                "WebView2 云原神实例仍在运行，请先关闭所有 WebView2 实例，再关闭主窗口。",
                "WebView2 实例未关闭");
            return;
        }

        if (_instanceVisualIdentity.IsPrimary && _childSessionService.HasActiveChildSession())
        {
            e.Cancel = true;
            ThemedMessageBox.Warning(
                "桌面分身仍在运行，请先关闭桌面分身，再关闭主窗口。",
                "桌面分身未关闭");
            return;
        }

        if (_questVoiceCloseApproved) return;
        e.Cancel = true;
        if (_questVoiceClosePending) return;
        _questVoiceClosePending = true;
        try
        {
            if (!await QuestVoiceSettingsExitGuard.PrepareAsync()) return;
            // 有下载正在进行时确认退出，已下载的部分保留供下次续传。
            // 配音草稿保存成功后再确认，避免保存失败却保留下载退出授权。
            if (!DownloadCenter.ConfirmExit()) return;
            _questVoiceCloseApproved = true;
            Application.Current.MainWindow.Close();
        }
        finally { _questVoiceClosePending = false; }
    }

    [RelayCommand]
    private void OnOpenFeed()
    {
        if (HasPendingRedeemCodeUpdate)
        {
            if (_redeemCodeCnUpdateNewVersion != null)
            {
                Config.CommonConfig.RedeemCodeFeedsUpdateVersion = _redeemCodeCnUpdateNewVersion;
                _redeemCodeCnUpdateNewVersion = null;
            }

            if (_redeemCodeGlobalUpdateNewVersion != null)
            {
                Config.CommonConfig.RedeemCodeGlobalFeedsUpdateVersion = _redeemCodeGlobalUpdateNewVersion;
                _redeemCodeGlobalUpdateNewVersion = null;
            }

            // 重置为主题默认前景色，避免浅色主题下显示为白色
            UpdateRedeemCodeButtonDefaultForeground();
        }

        // 关闭通知卡片
        IsRedeemCodeInfoBarOpen = false;

        var feedWindow = new FeedWindow(new FeedWindowViewModel());
        feedWindow.Show();
    }

    [RelayCommand]
    private void OnDismissRedeemCode()
    {
        // 仅关闭通知卡片并标记已读，不打开窗口
        IsRedeemCodeInfoBarOpen = false;
    }

    private async Task AutoDismissRedeemCodeCardAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            if (IsRedeemCodeInfoBarOpen)
            {
                IsRedeemCodeInfoBarOpen = false;
            }
        }
        catch (OperationCanceledException)
        {
            // 计时器被取消（用户手动关闭卡片），无需处理
        }
    }

    [RelayCommand]
    private async Task OnLoaded()
    {

        // 应用上次保存的主题
        ApplyTheme(Config.CommonConfig.CurrentThemeType);

        // 非主实例仅应用自身窗口主题；后续迁移、检查更新等启动操作由主实例统一执行。
        if (!InstanceBootstrap.Current.Context.IsRoot)
        {
            return;
        }

        // 版本是否运行过
        if (Config.CommonConfig.RunForVersion != Global.Version)
        {
            ModifyFolderSecurity();

            // alpha 版本用户每次升级后默认切换到 V6 OCR 模型
            if (Global.Version.Contains("alpha", StringComparison.OrdinalIgnoreCase)
                && Config.OtherConfig.OcrConfig.PaddleOcrModelConfig != PaddleOcrModelConfig.V6)
            {
                Config.OtherConfig.OcrConfig.PaddleOcrModelConfig = PaddleOcrModelConfig.V6;
            }

            Config.CommonConfig.RunForVersion = Global.Version;
        }

        // 仅在用户应用新设置后执行友好验证，日常启动不执行古树预热或速度检查。
        await ValidateInferenceAsync();
        if (_inferenceRuntime.IsStopping) return;

        // 预热OCR
        await OcrPreheating();

        if (CommandLineOptions.Instance.HasTaskArgs)
        {
            return;
        }

        // 自动处理目录配置
        await Patch1();

        // 删除多余特征点
        Patch2();

        // 启动时关闭布局编辑模式
        if (Config.MaskWindowConfig.OverlayLayoutEditEnabled)
        {
            Config.MaskWindowConfig.OverlayLayoutEditEnabled = false;
        }

        // 首次运行
        if (Config.CommonConfig.IsFirstRun)
        {
            // 自动初始化键位绑定
            // InitKeyBinding();
            Config.AutoFightConfig.TeamNames = ""; // 此配置以后无用
            Config.CommonConfig.IsFirstRun = false;
        }

        OnceRun();

        // 检查更新
        await App.GetService<IUpdateService>()!.CheckUpdateAsync(new UpdateOption());
        
        // 检查兑换码更新
        await CheckRedeemCodeFeedsUpdateAsync();

        //  Win11下 BitBlt截图方式不可用，需要关闭窗口优化功能
        if (OsVersionHelper.IsWindows11_OrGreater
            && Config.AutoFixWin11BitBlt
            && Config.CaptureMode == nameof(CaptureModes.BitBlt))
        {
            BitBltRegistryHelper.SetDirectXUserGlobalSettings();
        }

        // 更新仓库
        // ScriptRepoUpdater.Instance.AutoUpdate();

        // 提示上次未恢复的脚本数据备份（更新中断等情况会保留备份），不阻塞后续启动流程
        _ = PromptUnrestoredScriptBackupsAsync();

        // 自动更新已订阅的脚本 会先更新仓库；任务开始前会等待这次更新完成
        ScriptRepoUpdater.Instance.StartAutoUpdate();

        // 临时目录由 ITempFileService 在退出时清理，异常结束留下的目录在启动时回收
    }

    /// <summary>
    /// 发现本次启动前留下、尚未恢复的脚本数据备份时提示用户，并可打开备份目录
    /// </summary>
    private async Task PromptUnrestoredScriptBackupsAsync()
    {
        try
        {
            var backups = ScriptRepoUpdater.FindUnrestoredBackups(InstanceBootstrap.Current.Context.StartedAt);
            if (backups.Count == 0)
            {
                return;
            }

            var backupPath = ScriptRepoUpdater.ScriptUpdateBackupPath;
            _logger.LogWarning("发现 {Count} 份未恢复的脚本数据备份：{Path}", backups.Count, backupPath);
            var result = await ThemedMessageBox.WarningAsync(
                $"发现 {backups.Count} 份未恢复的脚本数据备份。\n" +
                "可能是上次订阅脚本更新中途中断，或新版本脚本不再保留这些文件。\n\n" +
                $"备份位于：{backupPath}\n" +
                "确认不再需要后删除该目录即可不再提示。\n\n是否打开备份目录？",
                "脚本数据备份",
                System.Windows.MessageBoxButton.YesNo);
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{backupPath}\"") { UseShellExecute = true });
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "检查脚本数据备份失败");
        }
    }

    private void ModifyFolderSecurity()
    {
        // 检查程序是否位于C盘
        if (Global.StartUpPath.StartsWith(@"C:", StringComparison.OrdinalIgnoreCase))
        {
            // 修改文件夹权限
            SecurityControlHelper.AllowFullFolderSecurity(Global.StartUpPath);
        }
    }

    /*
    private void InitKeyBinding()
    {
        try
        {
            var kbVm = App.GetService<KeyBindingsSettingsPageViewModel>();
            if (kbVm != null)
            {
                kbVm.FetchFromRegistryCommand.Execute(null);
            }
        }
        catch (Exception e)
        {
            _logger.LogError("首次运行自动初始化按键绑定异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);

            await ThemedMessageBox.ErrorAsync("读取原神键位并设置键位绑定数据时发生异常：" + e.Message + "，后续可以手动设置");
        }
    }
    */

    /**
     * 不同的安装目录处理
     * 可能当前目录下存在 BetterGI 的文件，需要移动到新的目录
     */
    private async Task Patch1()
    {
        var embeddedPath = Global.Absolute("BetterGI");
        var embeddedUserPath = Global.Absolute("BetterGI/User");
        var exePath = Global.Absolute("BetterGI/BetterGI.exe");
        if (Directory.Exists(embeddedPath)
            && File.Exists(exePath)
            && Directory.Exists(embeddedUserPath)
           )
        {
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(exePath);
            // 低版本才需要迁移
            if (fileVersionInfo.FileVersion != null && !Global.IsNewVersion(fileVersionInfo.FileVersion))
            {
                var res = await ThemedMessageBox.ShowAsync("检测到旧的 BetterGI 配置，是否迁移配置并清理旧目录？", "BetterGI",
                    System.Windows.MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Question);
                if (res == System.Windows.MessageBoxResult.Yes)
                {
                    // 迁移配置，拷贝整个目录并覆盖
                    DirectoryHelper.CopyDirectory(embeddedUserPath, Global.Absolute("User"));
                    // 删除旧目录
                    DirectoryHelper.DeleteReadOnlyDirectory(embeddedPath);
                    await ThemedMessageBox.InformationAsync("迁移配置成功, 软件将自动退出，请手动重新启动 BetterGI！");
                    Application.Current.Shutdown();
                }
            }
        }
    }

    /**
     * 0.45版本开始
     * 地图特征的存储格式变化
     */
    private void Patch2()
    {
        List<string> files =
        [
            Global.Absolute(@"Assets\Map\mainMap256Block_SIFT.kp"),
            Global.Absolute(@"Assets\Map\mainMap256Block_SIFT.mat"),
            Global.Absolute(@"Assets\Map\mainMap2048Block_SIFT.kp"),
            Global.Absolute(@"Assets\Map\mainMap2048Block_SIFT.mat"),
            Global.Absolute(@"Assets\Map\Teyvat\map_info.json"),
        ];

        // 循环删除
        foreach (var file in files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private async Task ValidateInferenceAsync()
    {
        if (_inferenceStartupHandled || !InstanceBootstrap.Current.Context.IsRoot) return;
        _inferenceStartupHandled = true;
        try
        {
            await _inferenceRuntime.ValidatePendingAsync();
            if (!_inferenceRuntime.IsStopping && !string.IsNullOrWhiteSpace(_inferenceRuntime.StartupMessage))
                await ThemedMessageBox.WarningAsync(_inferenceRuntime.StartupMessage);
        }
        catch (Exception e)
        {
            // 这是主窗口加载后的友好提示，不应让验证流程异常阻止软件继续使用。
            _logger.LogWarning(e, "推理引擎启动验证未完成");
            if (!_inferenceRuntime.IsStopping)
                await ThemedMessageBox.WarningAsync("推理引擎验证未能正常完成。\r\n\r\n" +
                                                    "软件可以继续使用；详细错误已写入日志，请在首页的“硬件加速设置”中重新检测后再试。");
        }
    }

    private async Task OcrPreheating()
    {
        try
        {
            await Task.Run(async () =>
            {
                try
                {
                    // 现在OCR创建的时候会自己读设置了
                    // string gameCultureInfoName = Config.OtherConfig.GameCultureInfoName;
                    // await OcrFactory.ChangeCulture(gameCultureInfoName);
                    await _inferenceDiagnostics.PreheatOcrAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    _logger.LogError("PaddleOcr预热异常，解决方案：【https://www.bettergi.com/faq.html】\r\n" + e.Source + "\r\n--" +
                                     Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
                    var innerException = e.InnerException;
                    if (innerException != null)
                    {
                        _logger.LogError("PaddleOcr预热内部异常，解决方案：【https://www.bettergi.com/faq.html】\r\n" +
                                         innerException.Source + "\r\n--" + Environment.NewLine +
                                         innerException.StackTrace + "\r\n---" + Environment.NewLine +
                                         innerException.Message);
                        throw innerException;
                    }
                    else
                    {
                        throw;
                    }
                }
            });
        }
        catch (OperationCanceledException) when (_inferenceRuntime.IsStopping) { }
        catch (Exception e)
        {
            if (_inferenceRuntime.IsStopping) return;
            ThemedMessageBox.Warning("PaddleOcr预热失败，解决方案：【https://www.bettergi.com/faq.html】   \r\n" + e.Source + "\r\n--" +
                               Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
            Process.Start(
                new ProcessStartInfo(
                        "https://www.bettergi.com/faq.html#%E2%9D%93-%E6%8F%90%E7%A4%BA-paddleocr-%E9%A2%84%E7%83%AD%E5%A4%B1%E8%B4%A5-%E5%BA%94%E8%AF%A5%E5%A6%82%E4%BD%95%E8%A7%A3%E5%86%B3")
                { UseShellExecute = true });
        }
    }

    private void OnceRun()
    {
        string deviceId = DeviceIdHelper.DeviceId;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            deviceId = "default"; // 如果获取设备ID失败，使用默认值
        }

        // 每个设备只运行一次 | 在Wine上会崩溃
        if (!Config.CommonConfig.OnceHadRunDeviceIdList.Contains(deviceId) && !WinePlatformAddon.IsRunningOnWine)
        {
            WelcomeDialog prompt = new WelcomeDialog
            {
                Owner = Application.Current.MainWindow
            };
            prompt.ShowDialog();
            prompt.Focus();

            Config.CommonConfig.OnceHadRunDeviceIdList.Add(deviceId);
            _configService.Save();
        }
    }
    
    private async Task CheckRedeemCodeFeedsUpdateAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, FeedWindowViewModel.CodesJsonUrl);
            var response = await App.GetService<IHttpClientFactory>()!.CreateClient(HttpClientNames.Default).SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var items = JsonConvert.DeserializeObject<List<FeedItem>>(json) ?? [];
            var (cnFeeds, globalFeeds) = FeedWindowViewModel.SplitByServer(items);

            var hasUpdate = false;
            var cnVersion = FeedWindowViewModel.GetFeedVersion(cnFeeds);
            if (Config.CommonConfig.RedeemCodeCnFeedsNotificationEnabled
                && !string.IsNullOrEmpty(cnVersion)
                && cnVersion != Config.CommonConfig.RedeemCodeFeedsUpdateVersion)
            {
                _redeemCodeCnUpdateNewVersion = cnVersion;
                hasUpdate = true;
            }

            var globalVersion = FeedWindowViewModel.GetFeedVersion(globalFeeds);
            if (Config.CommonConfig.RedeemCodeGlobalFeedsNotificationEnabled
                && !string.IsNullOrEmpty(globalVersion)
                && globalVersion != Config.CommonConfig.RedeemCodeGlobalFeedsUpdateVersion)
            {
                _redeemCodeGlobalUpdateNewVersion = globalVersion;
                hasUpdate = true;
            }

            if (hasUpdate)
            {
                RedeemCodeButtonForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E9BFA"));
                // 显示通知卡片
                IsRedeemCodeInfoBarOpen = true;
                // 取消旧计时器，启动新的自动消失计时器
                _redeemCodeDismissCts?.Cancel();
                _redeemCodeDismissCts?.Dispose();
                _redeemCodeDismissCts = new CancellationTokenSource();
                _ = AutoDismissRedeemCodeCardAsync(_redeemCodeDismissCts.Token);
            }
            
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, $"获取兑换码是否存在更新失败");
        }
    }

    // 更新兑换码按钮在当前主题下的默认前景色
    private void UpdateRedeemCodeButtonDefaultForeground()
    {
        try
        {
            var brush = Application.Current.TryFindResource("TextFillColorPrimaryBrush") as Brush;
            if (brush != null)
            {
                RedeemCodeButtonForeground = brush;
                return;
            }
        }
        catch
        {
            // 忽略资源查找异常，走回退逻辑
        }

        // 回退：根据当前主题类型使用黑/白色
        var isLightTheme = Config.CommonConfig.CurrentThemeType == ThemeType.LightNone
                           || Config.CommonConfig.CurrentThemeType == ThemeType.LightMica
                           || Config.CommonConfig.CurrentThemeType == ThemeType.LightAcrylic;
        RedeemCodeButtonForeground = isLightTheme ? Brushes.Black : Brushes.White;
    }
}
