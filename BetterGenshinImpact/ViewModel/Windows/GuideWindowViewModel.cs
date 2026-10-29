using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;
using FormsScreen = System.Windows.Forms.Screen;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 悬浮小窗的 ViewModel：窗口栏、菜单、地址框、热键反馈的展示逻辑。
/// 显示、穿透、焦点等状态由 <see cref="IGuideWindowService"/> 计算，这里只负责悬停计时、窗口栏自动隐藏和菜单内容
/// </summary>
public partial class GuideWindowViewModel : ObservableObject, IViewModel
{
    /// <summary>鼠标进入后显示窗口栏的延迟，避免划过时闪烁</summary>
    private static readonly TimeSpan HoverEnterDelay = TimeSpan.FromMilliseconds(120);

    /// <summary>鼠标离开后隐藏窗口栏的延迟</summary>
    private static readonly TimeSpan HoverLeaveDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>鼠标静止多久后隐藏窗口栏</summary>
    private static readonly TimeSpan IdleHideDelay = TimeSpan.FromMilliseconds(2500);

    // 窗口栏宽度预算（DIP）：常驻部分（拖动把手、分集、链接菜单、播放、设置、更多、隐藏、关闭）约 253，
    // 各分组按优先级依次放入，每档都给标题留出至少约 60 的宽度

    /// <summary>窗口栏放下全部按钮（含后退、刷新）需要的宽度（DIP）</summary>
    private const double FullBarWidth = 640;

    /// <summary>窗口栏放下交互模式、鼠标穿透按钮需要的宽度（DIP）</summary>
    private const double WindowBarWidth = 560;

    /// <summary>窗口栏放下收藏区需要的宽度（DIP）</summary>
    private const double LibraryBarWidth = 500;

    /// <summary>窗口栏放下倍速、音量、字幕、沉浸按钮需要的宽度（DIP）</summary>
    private const double PlayBarWidth = 430;

    private readonly IGuideWindowService _service;
    private readonly GuideLibraryStore _library;
    private readonly INavigationService _navigationService;
    private readonly HotKeyConfig _hotKeyConfig;

    /// <summary>鼠标进入计时</summary>
    private readonly DispatcherTimer _enterTimer;

    /// <summary>鼠标离开计时</summary>
    private readonly DispatcherTimer _leaveTimer;

    /// <summary>鼠标静止计时</summary>
    private readonly DispatcherTimer _idleTimer;

    /// <summary>热键反馈显示计时</summary>
    private readonly DispatcherTimer _osdTimer;

    /// <summary>当前打开的菜单数量，菜单打开期间保持窗口栏显示</summary>
    private int _openMenuCount;

    /// <summary>鼠标静止，窗口栏已自动隐藏</summary>
    private bool _idleHidden;

    /// <summary>鼠标在窗口栏上</summary>
    private bool _pointerOverBar;

    /// <summary>窗口是否处于激活状态</summary>
    private bool _windowActive;

    /// <summary>
    /// 窗口栏是否显示
    /// </summary>
    [ObservableProperty]
    private bool _isBarVisible;

    /// <summary>
    /// 是否正在编辑地址
    /// </summary>
    [ObservableProperty]
    private bool _isEditingAddress;

    /// <summary>
    /// 地址框文本
    /// </summary>
    [ObservableProperty]
    private string _addressText = string.Empty;

    /// <summary>
    /// 热键反馈是否显示
    /// </summary>
    [ObservableProperty]
    private bool _isOsdVisible;

    /// <summary>
    /// 窗口栏是否显示网页区（后退、刷新）
    /// </summary>
    [ObservableProperty]
    private bool _showNavigationGroup = true;

    /// <summary>
    /// 窗口栏是否显示收藏区
    /// </summary>
    [ObservableProperty]
    private bool _showLibraryGroup = true;

    /// <summary>
    /// 窗口栏是否显示倍速、音量、字幕、沉浸按钮
    /// </summary>
    [ObservableProperty]
    private bool _showPlayExtras = true;

    /// <summary>
    /// 窗口栏是否显示交互模式、鼠标穿透按钮
    /// </summary>
    [ObservableProperty]
    private bool _showWindowGroup = true;

    /// <summary>
    /// 窗口栏是否显示“更多”菜单（有按钮被收起时）
    /// </summary>
    [ObservableProperty]
    private bool _showOverflow;

    /// <summary>
    /// 可见的鼠标指针是否位于窗口上（鼠标穿透时由行为轮询写入）
    /// </summary>
    [ObservableProperty]
    private bool _isCursorInside;

    /// <param name="service">小窗服务</param>
    /// <param name="configService">配置服务</param>
    /// <param name="library">收藏与观看记录</param>
    /// <param name="navigationService">主窗口导航</param>
    public GuideWindowViewModel(IGuideWindowService service, IConfigService configService, GuideLibraryStore library,
        INavigationService navigationService)
    {
        _service = service;
        _library = library;
        _navigationService = navigationService;
        _hotKeyConfig = configService.Get().HotKeyConfig;
        var dispatcher = Application.Current.Dispatcher;
        _enterTimer = new DispatcherTimer(HoverEnterDelay, DispatcherPriority.Normal, OnEnterTimer, dispatcher);
        _leaveTimer = new DispatcherTimer(HoverLeaveDelay, DispatcherPriority.Normal, OnLeaveTimer, dispatcher);
        _idleTimer = new DispatcherTimer(IdleHideDelay, DispatcherPriority.Background, OnIdleTimer, dispatcher);
        _osdTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(1200), DispatcherPriority.Normal, OnOsdTimer, dispatcher);
        _enterTimer.Stop();
        _leaveTimer.Stop();
        _idleTimer.Stop();
        _osdTimer.Stop();
        State.PropertyChanged += OnStatePropertyChanged;
        Config.PropertyChanged += OnConfigPropertyChanged;
    }

    /// <summary>
    /// 小窗状态
    /// </summary>
    public GuideWindowState State => _service.State;

    /// <summary>
    /// 小窗配置
    /// </summary>
    public GuideWindowConfig Config => _service.Config;

    /// <summary>
    /// 倍速菜单
    /// </summary>
    public ObservableCollection<GuideMenuOption> RateOptions { get; } = [];

    /// <summary>
    /// 收藏菜单
    /// </summary>
    public ObservableCollection<GuideMenuOption> FavoriteOptions { get; } = [];

    /// <summary>
    /// 最近观看菜单
    /// </summary>
    public ObservableCollection<GuideMenuOption> HistoryOptions { get; } = [];

    /// <summary>
    /// 移到显示器菜单
    /// </summary>
    public ObservableCollection<GuideMenuOption> MonitorOptions { get; } = [];

    /// <summary>
    /// 网页缩放菜单
    /// </summary>
    public ObservableCollection<GuideMenuOption> ZoomOptions { get; } = [];

    #region 按钮提示（带快捷键）

    /// <summary>播放按钮提示</summary>
    public string PlayToolTip => WithHotkey("播放/暂停", _hotKeyConfig.GuidePlayPauseHotkey);

    /// <summary>倍速按钮提示</summary>
    public string RateToolTip => WithHotkey("倍速（点击选择，滚轮调整）", _hotKeyConfig.GuideSpeedUpHotkey);

    /// <summary>音量按钮提示</summary>
    public string VolumeToolTip => WithHotkey("静音（滚轮调整音量）", _hotKeyConfig.GuideMuteHotkey);

    /// <summary>字幕按钮提示</summary>
    public string SubtitleToolTip => WithHotkey("字幕浮窗", _hotKeyConfig.GuideSubtitleHotkey);

    /// <summary>沉浸按钮提示</summary>
    public string ImmersiveToolTip => WithHotkey("沉浸模式（B 站网页全屏）", _hotKeyConfig.GuideImmersiveHotkey);

    /// <summary>交互按钮提示</summary>
    public string InteractionToolTip => WithHotkey("交互模式：可以在网页中输入文字，移开鼠标后自动回到游戏", _hotKeyConfig.GuideInteractHotkey);

    /// <summary>穿透按钮提示</summary>
    public string ClickThroughToolTip => WithHotkey("鼠标穿透：点击直接落到游戏上", _hotKeyConfig.GuideClickThroughHotkey);

    /// <summary>隐藏按钮提示</summary>
    public string HideToolTip => WithHotkey("隐藏小窗（保留网页）", _hotKeyConfig.GuideToggleHotkey);

    /// <summary>
    /// 提示文本后附上快捷键
    /// </summary>
    private static string WithHotkey(string text, string? hotkey) =>
        string.IsNullOrWhiteSpace(hotkey) ? text : $"{text}（{hotkey.Replace(" ", string.Empty)}）";

    #endregion

    #region 悬停与窗口栏

    /// <summary>
    /// 是否为跟随游戏的位置模式
    /// </summary>
    public bool IsFollowGameMode => Config.PositionMode == GuidePositionMode.FollowGame;

    /// <summary>
    /// 是否为自由放置的位置模式
    /// </summary>
    public bool IsFreeMode => Config.PositionMode == GuidePositionMode.Free;

    /// <summary>
    /// 穿透时鼠标指针位置变化：交给服务计算智能透明
    /// </summary>
    partial void OnIsCursorInsideChanged(bool value)
    {
        _service.SetCursorInside(value);
    }

    /// <summary>
    /// 配置变化：同步依赖配置的展示属性
    /// </summary>
    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GuideWindowConfig.PositionMode))
        {
            OnPropertyChanged(nameof(IsFollowGameMode));
            OnPropertyChanged(nameof(IsFreeMode));
        }
    }

    /// <summary>
    /// 窗口关闭：停止计时并取消订阅，避免已关闭的窗口继续响应状态变化
    /// </summary>
    public void Detach()
    {
        _enterTimer.Stop();
        _leaveTimer.Stop();
        _idleTimer.Stop();
        _osdTimer.Stop();
        State.PropertyChanged -= OnStatePropertyChanged;
        Config.PropertyChanged -= OnConfigPropertyChanged;
    }

    /// <summary>
    /// 鼠标进入小窗
    /// </summary>
    [RelayCommand]
    private void PointerEnter()
    {
        _leaveTimer.Stop();
        if (!State.IsClickThrough)
        {
            _enterTimer.Start();
        }

        RestartIdle();
    }

    /// <summary>
    /// 鼠标离开小窗；菜单打开期间不算离开
    /// </summary>
    [RelayCommand]
    private void PointerLeave()
    {
        _enterTimer.Stop();
        if (_openMenuCount == 0)
        {
            _leaveTimer.Start();
        }
    }

    /// <summary>
    /// 鼠标在小窗上移动：重新显示被自动隐藏的窗口栏
    /// </summary>
    [RelayCommand]
    private void PointerMove()
    {
        _leaveTimer.Stop();
        RestartIdle();
    }

    /// <summary>
    /// 鼠标进入窗口栏：在窗口栏上时不自动隐藏
    /// </summary>
    [RelayCommand]
    private void BarEnter()
    {
        _pointerOverBar = true;
        UpdateBarVisibility();
    }

    /// <summary>
    /// 鼠标离开窗口栏
    /// </summary>
    [RelayCommand]
    private void BarLeave()
    {
        _pointerOverBar = false;
        RestartIdle();
    }

    /// <summary>
    /// 菜单打开
    /// </summary>
    [RelayCommand]
    private void MenuOpened()
    {
        _openMenuCount++;
        _leaveTimer.Stop();
        UpdateBarVisibility();
    }

    /// <summary>
    /// 菜单关闭：鼠标已不在小窗上时开始离开计时；窗口被菜单意外激活时把焦点还给游戏
    /// </summary>
    [RelayCommand]
    private void MenuClosed()
    {
        _openMenuCount = Math.Max(0, _openMenuCount - 1);
        if (_openMenuCount == 0)
        {
            _leaveTimer.Start();
            RestartIdle();
            if (_windowActive && !State.IsInteraction && !IsEditingAddress)
            {
                _service.OnUnexpectedActivation();
            }
        }

        UpdateBarVisibility();
    }

    /// <summary>
    /// 窗口被激活：不在交互模式、没有打开菜单时视为意外激活，鼠标松开后把焦点还给游戏
    /// </summary>
    public void OnWindowActivated()
    {
        _windowActive = true;
        if (!State.IsInteraction && !IsEditingAddress && _openMenuCount == 0)
        {
            _service.OnUnexpectedActivation();
        }
    }

    /// <summary>
    /// 窗口失去激活
    /// </summary>
    public void OnWindowDeactivated()
    {
        _windowActive = false;
    }

    /// <summary>
    /// 用户拖动或缩放结束：保存位置和尺寸
    /// </summary>
    public void CommitBounds() => _service.CommitBounds();

    /// <summary>
    /// 窗口收到关闭请求（例如交互模式下按 Alt+F4）：改为隐藏小窗
    /// </summary>
    public void OnCloseRequested() => _service.Hide();

    /// <summary>
    /// 窗口宽度变化：按宽度收起窗口栏中优先级较低的按钮
    /// </summary>
    public void UpdateBarLayout(double width)
    {
        ShowNavigationGroup = width >= FullBarWidth;
        ShowWindowGroup = width >= WindowBarWidth;
        ShowLibraryGroup = width >= LibraryBarWidth;
        ShowPlayExtras = width >= PlayBarWidth;
        ShowOverflow = !ShowNavigationGroup || !ShowWindowGroup || !ShowLibraryGroup || !ShowPlayExtras;
    }

    /// <summary>
    /// 窗口栏滚轮：调整不透明度
    /// </summary>
    public void OnBarWheel(int delta)
    {
        _service.ChangeOpacity(Math.Sign(delta));
    }

    /// <summary>
    /// 音量按钮滚轮：调整音量
    /// </summary>
    public void OnVolumeWheel(int delta)
    {
        _service.ChangeVolume(Math.Sign(delta));
    }

    /// <summary>
    /// 倍速按钮滚轮：调整倍速
    /// </summary>
    public void OnRateWheel(int delta)
    {
        _service.ChangeRate(Math.Sign(delta));
    }

    /// <summary>
    /// 重新开始静止计时，并显示被自动隐藏的窗口栏
    /// </summary>
    private void RestartIdle()
    {
        _idleTimer.Stop();
        _idleTimer.Start();
        if (_idleHidden)
        {
            _idleHidden = false;
            UpdateBarVisibility();
        }
    }

    /// <summary>
    /// 进入延迟到：报告悬停
    /// </summary>
    private void OnEnterTimer(object? sender, EventArgs e)
    {
        _enterTimer.Stop();
        _service.SetPointerOver(true);
    }

    /// <summary>
    /// 离开延迟到：报告不再悬停
    /// </summary>
    private void OnLeaveTimer(object? sender, EventArgs e)
    {
        _leaveTimer.Stop();
        if (_openMenuCount > 0 || IsEditingAddress)
        {
            return;
        }

        _service.SetPointerOver(false);
    }

    /// <summary>
    /// 鼠标静止：自动隐藏窗口栏（鼠标在窗口栏上或菜单打开时除外）
    /// </summary>
    private void OnIdleTimer(object? sender, EventArgs e)
    {
        _idleTimer.Stop();
        if (_pointerOverBar || _openMenuCount > 0 || IsEditingAddress)
        {
            return;
        }

        _idleHidden = true;
        UpdateBarVisibility();
    }

    /// <summary>
    /// 窗口栏显示条件：服务允许，并且鼠标没有静止（交互模式、编辑地址、菜单打开时总是显示）
    /// </summary>
    private void UpdateBarVisibility()
    {
        IsBarVisible = State.IsBarAllowed
                       && (!_idleHidden || _pointerOverBar || _openMenuCount > 0 || IsEditingAddress || State.IsInteraction);
    }

    /// <summary>
    /// 状态变化：同步窗口栏与热键反馈
    /// </summary>
    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GuideWindowState.IsBarAllowed):
            case nameof(GuideWindowState.IsInteraction):
                if (State.IsBarAllowed)
                {
                    _idleHidden = false;
                    _idleTimer.Stop();
                    _idleTimer.Start();
                }

                UpdateBarVisibility();
                break;
            case nameof(GuideWindowState.OsdSerial):
                _osdTimer.Stop();
                _osdTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, State.OsdDurationMs));
                IsOsdVisible = !string.IsNullOrEmpty(State.OsdText);
                _osdTimer.Start();
                break;
            case nameof(GuideWindowState.IsShown):
                if (!State.IsShown)
                {
                    _enterTimer.Stop();
                    _leaveTimer.Stop();
                    _openMenuCount = 0;
                    if (IsEditingAddress)
                    {
                        IsEditingAddress = false;
                    }
                }

                break;
        }
    }

    /// <summary>
    /// 热键反馈显示时长到
    /// </summary>
    private void OnOsdTimer(object? sender, EventArgs e)
    {
        _osdTimer.Stop();
        IsOsdVisible = false;
    }

    /// <summary>
    /// 编辑地址状态变化时同步窗口栏
    /// </summary>
    partial void OnIsEditingAddressChanged(bool value)
    {
        UpdateBarVisibility();
    }

    #endregion

    #region 地址框

    /// <summary>
    /// 开始编辑地址：进入交互模式以接收键盘输入
    /// </summary>
    [RelayCommand]
    private void BeginEditAddress()
    {
        AddressText = State.Url;
        _service.BeginInteraction();
        IsEditingAddress = true;
    }

    /// <summary>
    /// 打开地址框中的内容
    /// </summary>
    [RelayCommand]
    private void CommitAddress()
    {
        var text = AddressText;
        IsEditingAddress = false;
        if (!string.IsNullOrWhiteSpace(text) && text.Trim() != State.Url)
        {
            _service.NavigateFromInput(text);
        }

        _service.EndInteraction();
    }

    /// <summary>
    /// 取消编辑地址
    /// </summary>
    [RelayCommand]
    private void CancelEditAddress()
    {
        if (!IsEditingAddress)
        {
            return;
        }

        IsEditingAddress = false;
        _service.EndInteraction();
    }

    /// <summary>
    /// 粘贴剪贴板中的链接并打开
    /// </summary>
    [RelayCommand]
    private void PasteAndOpen()
    {
        _service.OpenFromClipboard();
    }

    /// <summary>
    /// 复制当前网址
    /// </summary>
    [RelayCommand]
    private void CopyLink()
    {
        if (string.IsNullOrEmpty(State.Url))
        {
            return;
        }

        try
        {
            Clipboard.SetText(GuideLinkResolver.Normalize(State.Url));
            State.ShowOsd("已复制链接");
        }
        catch (Exception)
        {
            State.ShowOsd("复制失败，剪贴板被其他程序占用");
        }
    }

    /// <summary>
    /// 用系统浏览器打开当前网址
    /// </summary>
    [RelayCommand]
    private void OpenInBrowser()
    {
        if (!Uri.TryCreate(State.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            State.ShowOsd("无法打开系统浏览器");
        }
    }

    #endregion

    #region 播放与窗口命令

    /// <summary>后退</summary>
    [RelayCommand]
    private void GoBack() => _service.GoBack();

    /// <summary>刷新</summary>
    [RelayCommand]
    private void Reload() => _service.Reload();

    /// <summary>播放或暂停</summary>
    [RelayCommand]
    private void TogglePlay() => _service.TogglePlay();

    /// <summary>选择倍速菜单项</summary>
    [RelayCommand]
    private void SelectRate(GuideMenuOption? option)
    {
        if (option?.Value is double rate)
        {
            _service.SetRate(rate);
        }
    }

    /// <summary>静音或取消静音</summary>
    [RelayCommand]
    private void ToggleMute() => _service.ToggleMute();

    /// <summary>字幕浮窗开关</summary>
    [RelayCommand]
    private void ToggleSubtitle() => _service.ToggleSubtitle();

    /// <summary>沉浸模式开关</summary>
    [RelayCommand]
    private void ToggleImmersive() => _service.ToggleImmersive();

    /// <summary>上一集</summary>
    [RelayCommand]
    private void PrevEpisode() => _service.SwitchEpisode(-1);

    /// <summary>下一集</summary>
    [RelayCommand]
    private void NextEpisode() => _service.SwitchEpisode(1);

    /// <summary>弹幕开关</summary>
    [RelayCommand]
    private void ToggleDanmaku() => _service.ToggleDanmaku();

    /// <summary>收藏或取消收藏</summary>
    [RelayCommand]
    private void ToggleFavorite() => _service.ToggleFavorite();

    /// <summary>交互模式开关</summary>
    [RelayCommand]
    private void ToggleInteraction() => _service.ToggleInteraction();

    /// <summary>鼠标穿透开关</summary>
    [RelayCommand]
    private void ToggleClickThrough() => _service.ToggleClickThrough();

    /// <summary>始终置顶开关</summary>
    [RelayCommand]
    private void ToggleTopmost() => _service.ToggleTopmost();

    /// <summary>智能透明开关</summary>
    [RelayCommand]
    private void ToggleSmartFade() => Config.SmartFadeEnabled = !Config.SmartFadeEnabled;

    /// <summary>隐藏小窗</summary>
    [RelayCommand]
    private void HideWindow() => _service.Hide();

    /// <summary>关闭小窗</summary>
    [RelayCommand]
    private void CloseWindow() => _service.Close();

    /// <summary>切换位置模式，参数为 FollowGame 或 Free</summary>
    [RelayCommand]
    private void SetPositionMode(string? mode)
    {
        if (Enum.TryParse<GuidePositionMode>(mode, out var value))
        {
            _service.SetPositionMode(value);
        }
    }

    /// <summary>停靠到一个角，参数为 TopLeft、TopRight、BottomLeft 或 BottomRight</summary>
    [RelayCommand]
    private void Dock(string? anchor)
    {
        if (Enum.TryParse<GuideAnchor>(anchor, out var value))
        {
            _service.Dock(value);
        }
    }

    /// <summary>选择显示器菜单项</summary>
    [RelayCommand]
    private void SelectMonitor(GuideMenuOption? option)
    {
        if (option?.Value is int index)
        {
            _service.MoveToMonitor(index);
        }
    }

    /// <summary>选择网页缩放菜单项</summary>
    [RelayCommand]
    private void SelectZoom(GuideMenuOption? option)
    {
        if (option?.Value is double factor)
        {
            _service.SetZoom(factor);
        }
    }

    /// <summary>调整字幕浮窗位置</summary>
    [RelayCommand]
    private void AdjustSubtitle()
    {
        if (!Config.SubtitleWindowEnabled)
        {
            Config.SubtitleWindowEnabled = true;
        }

        _service.BeginSubtitleAdjust();
    }

    /// <summary>网页异常后重新加载</summary>
    [RelayCommand]
    private void ReloadAfterCrash() => _service.ReloadAfterCrash();

    /// <summary>
    /// 打开 BetterGI 主界面的“悬浮小窗”设置页
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        var main = Application.Current.MainWindow;
        if (main is null)
        {
            return;
        }

        main.Show();
        if (main.WindowState == WindowState.Minimized)
        {
            main.WindowState = WindowState.Normal;
        }

        main.Activate();
        _navigationService.Navigate(typeof(GuideWindowSettingsPage));
    }

    /// <summary>
    /// 打开收藏或观看记录中的视频
    /// </summary>
    [RelayCommand]
    private void OpenLibraryItem(GuideMenuOption? option)
    {
        if (option?.Url is not { Length: > 0 } url)
        {
            return;
        }

        if (option.FavoriteId is { } id)
        {
            _library.MarkFavoriteOpened(id);
            _service.Open(new GuideOpenRequest(url, GuideOpenSource.Favorite));
            return;
        }

        _service.Open(new GuideOpenRequest(url, GuideOpenSource.History));
    }

    #endregion

    #region 菜单内容

    /// <summary>
    /// 倍速菜单打开前：生成选项并标记当前倍速
    /// </summary>
    [RelayCommand]
    private void PrepareRateMenu()
    {
        FillRateOptions();
        MenuOpened();
    }

    /// <summary>
    /// 收藏菜单打开前：读取最近的收藏与观看记录
    /// </summary>
    [RelayCommand]
    private void PrepareLibraryMenu()
    {
        FillLibraryOptions();
        MenuOpened();
    }

    /// <summary>
    /// “更多”菜单打开前：被收起的按钮里有倍速和收藏，两类选项都要准备
    /// </summary>
    [RelayCommand]
    private void PrepareOverflowMenu()
    {
        FillRateOptions();
        FillLibraryOptions();
        MenuOpened();
    }

    /// <summary>
    /// 生成倍速选项并标记当前倍速
    /// </summary>
    private void FillRateOptions()
    {
        RateOptions.Clear();
        foreach (var rate in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0 })
        {
            RateOptions.Add(new GuideMenuOption($"{rate:0.##}x", SelectRateCommand, rate)
            {
                IsChecked = Math.Abs(State.PlaybackRate - rate) < 0.01,
            });
        }
    }

    /// <summary>
    /// 读取最近的收藏与观看记录
    /// </summary>
    private void FillLibraryOptions()
    {
        FavoriteOptions.Clear();
        foreach (var favorite in _library.GetFavorites().Take(10))
        {
            var record = _library.FindRecord(favorite.VideoKey);
            FavoriteOptions.Add(new GuideMenuOption(Trim(favorite.Title), OpenLibraryItemCommand, null)
            {
                Url = favorite.Url,
                FavoriteId = favorite.Id,
                Detail = DescribeProgress(record),
            });
        }

        if (FavoriteOptions.Count == 0)
        {
            FavoriteOptions.Add(new GuideMenuOption("暂无收藏，点 ★ 收藏当前视频", null, null) { IsEnabled = false });
        }

        HistoryOptions.Clear();
        foreach (var record in _library.GetHistory().Take(10))
        {
            HistoryOptions.Add(new GuideMenuOption(Trim(record.Title), OpenLibraryItemCommand, null)
            {
                Url = record.Url,
                Detail = DescribeProgress(record),
            });
        }

        if (HistoryOptions.Count == 0)
        {
            HistoryOptions.Add(new GuideMenuOption("暂无观看记录", null, null) { IsEnabled = false });
        }
    }

    /// <summary>
    /// 设置菜单打开前：列出显示器和网页缩放选项
    /// </summary>
    [RelayCommand]
    private void PrepareSettingsMenu()
    {
        MonitorOptions.Clear();
        var screens = FormsScreen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var name = $"显示器 {i + 1}（{screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? "，主显示器" : string.Empty)}）";
            MonitorOptions.Add(new GuideMenuOption(name, SelectMonitorCommand, i));
        }

        ZoomOptions.Clear();
        foreach (var zoom in new[] { 0.67, 0.8, 0.9, 1.0, 1.1, 1.25 })
        {
            ZoomOptions.Add(new GuideMenuOption($"{Math.Round(zoom * 100)}%", SelectZoomCommand, zoom)
            {
                IsChecked = Math.Abs(Config.ZoomFactor - zoom) < 0.01,
            });
        }

        MenuOpened();
    }

    /// <summary>
    /// 进度说明，例如“看到 12:34 / 20:10”“已看完”
    /// </summary>
    private static string DescribeProgress(GuideWatchRecord? record)
    {
        if (record is null)
        {
            return string.Empty;
        }

        if (record.Finished)
        {
            return "已看完";
        }

        return record.Duration > 0
            ? $"看到 {GuideWindowState.FormatTime(record.Position)} / {GuideWindowState.FormatTime(record.Duration)}"
            : $"看到 {GuideWindowState.FormatTime(record.Position)}";
    }

    /// <summary>
    /// 截断过长的标题
    /// </summary>
    private static string Trim(string title) => title.Length > 36 ? title[..35] + "…" : title;

    #endregion
}

/// <summary>
/// 小窗菜单中的一项。命令参数总是菜单项本身，命令从 <see cref="Value"/> 读取取值
/// </summary>
/// <param name="header">显示文本</param>
/// <param name="command">点击时执行的命令</param>
/// <param name="value">选项的取值，例如倍速、显示器序号</param>
public sealed class GuideMenuOption(string header, ICommand? command, object? value)
{
    /// <summary>显示文本</summary>
    public string Header { get; } = header;

    /// <summary>点击时执行的命令</summary>
    public ICommand? Command { get; } = command;

    /// <summary>选项的取值</summary>
    public object? Value { get; } = value;

    /// <summary>附加说明，例如观看进度</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>是否显示勾选</summary>
    public bool IsChecked { get; init; }

    /// <summary>是否可用</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>收藏或观看记录的网址</summary>
    public string? Url { get; init; }

    /// <summary>收藏 Id，观看记录为 null</summary>
    public string? FavoriteId { get; init; }

    /// <summary>菜单中显示的完整文本：有说明时附在后面</summary>
    public string DisplayText => string.IsNullOrEmpty(Detail) ? Header : $"{Header}　{Detail}";
}
