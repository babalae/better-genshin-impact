using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Music.Model;
using BetterGenshinImpact.GameTask.Music.Service;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.DpiAwareness;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View;

/// <summary>
/// 引导式跟弹的置顶覆盖窗口。
///
/// 窗口贴在游戏画面上、本身点击穿透（不抢游戏鼠标与焦点），只在画面上画引导圈：
/// 本段要按的琴键上套一个高饱和亮色的圈（锚圈停在琴键边缘不动、一圈涟漪往外扩散提示「该按了」）——
/// 圈的位置直接照原神演奏界面的真实琴键算（见 <see cref="MusicKeyLayout"/>），
/// 不再自己画一块提示板（用户明确要求「在原神的演奏按键上面加提示」）。
/// 判定由 <see cref="MusicGuidedSession"/> 直接读真实键盘事件完成，本窗口只负责显示。
///
/// 覆盖层拿不到键盘焦点，所以所有「按键」都用异步键状态轮询：Esc 退出，
/// Ctrl + 方向键等现场微调引导圈位置（微调结果自动写回配置）。
/// </summary>
public partial class MusicGuidedWindow : Window, IMusicGuidedOverlay
{
    /// <summary>引导圈涟漪从贴住琴键到扩散消失一轮的时间</summary>
    private static readonly TimeSpan ContractionPeriod = TimeSpan.FromMilliseconds(1500);

    /// <summary>按错时的红闪时间</summary>
    private static readonly TimeSpan ErrorFlashDuration = TimeSpan.FromMilliseconds(420);

    /// <summary>现场微调的按键（虚拟键码；覆盖层用 GetAsyncKeyState 轮询，不能用 WPF 键盘事件）</summary>
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkReturn = 0x0D;
    private const int VkPrior = 0x21;
    private const int VkNext = 0x22;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkOemMinus = 0xBD;
    private const int VkOemPlus = 0xBB;
    private const int VkOemComma = 0xBC;
    private const int VkOemPeriod = 0xBE;
    private const int VkOem4 = 0xDB;
    private const int VkOem6 = 0xDD;
    private const int VkR = 0x52;

    private const string DefaultHint = "照着圈起来的琴键弹，整段弹对后自动显示下一段";

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly nint HwndTopmost = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x63, 0xC7, 0xFF));
    private static readonly Brush SatisfiedBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x84));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
    private static readonly Brush HintBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));

    /// <summary>待按：高饱和青色，套在米白色的琴键按钮上最跳</summary>
    private static readonly Brush PendingRingBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0xFF));

    /// <summary>和弦里已经按住、但这次按键还没按全：亮金色，和青色的「待按」区分度最大</summary>
    private static readonly Brush HeldRingBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));

    /// <summary>这次按键已经弹对</summary>
    private static readonly Brush SatisfiedRingBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x84));

    /// <summary>按错时闪的红色</summary>
    private static readonly Brush ErrorRingBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));

    /// <summary>引导圈底下的深色描边：亮色圈压在米白琴键或天空亮处也不会糊掉</summary>
    private static readonly Brush RingHaloBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x00, 0x1B, 0x2E));

    private static readonly Brush ChipPendingBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ChipSatisfiedBrush = new SolidColorBrush(Color.FromArgb(0x59, 0x3D, 0xDC, 0x84));
    private static readonly Brush ChipErrorBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0x3B, 0x30));
    private static readonly Brush ChipTextPendingBrush = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ChipTextSatisfiedBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xFF, 0xE4));
    private static readonly Brush ChipTextErrorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xDD));

    private readonly Dictionary<char, KeyVisual> _keys = [];
    private readonly ObservableCollection<ChipVisual> _chips = [];
    private readonly HashSet<int> _downKeys = [];
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _followTimer;
    private DispatcherTimer? _hintTimer;

    private MusicGuidedPress[] _segmentPresses = [];
    private nint _gameHandle;
    private double _dpiScaleX = 1;
    private double _dpiScaleY = 1;
    private long _contractionStart;
    private bool _escapeHeld;
    private bool _isClosing;
    private bool _isPumping;
    private MusicGuidedSession? _session;

    /// <summary>游戏是否在前台的查询间隔（毫秒）</summary>
    private const int GameActiveCheckInterval = 250;

    /// <summary>
    /// 「游戏是否在前台」的缓存时刻：覆盖层每 16ms 刷一帧，而查一次进程名要毫秒级，
    /// 每帧都查不值当，所以 <see cref="IsGameActive"/> 把结果缓存 <see cref="GameActiveCheckInterval"/> 毫秒。
    /// </summary>
    private long _gameActiveCheckedAt;

    /// <inheritdoc cref="_gameActiveCheckedAt" />
    private bool _gameActiveCached;

    public MusicGuidedWindow()
    {
        InitializeComponent();

        DpiAwarenessExtension.InitializeDpiAwareness(this);
        KeyChips.ItemsSource = _chips;

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += (_, _) => RenderFrame();

        _followTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _followTimer.Tick += (_, _) => FollowGameWindow();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>点右上角 ✕ 时触发（窗口是点击穿透的，通常用 Esc 退出）</summary>
    public event Action? ExitRequested;

    /// <summary>
    /// 关联的引导会话：窗口尺寸变化时由渲染帧驱动会话重新换算键位位置。
    /// </summary>
    public MusicGuidedSession? Session
    {
        get => _session;
        set => _session = value;
    }

    /// <summary>
    /// 键位参数（与引导会话共用同一个配置实例）。
    /// 现场微调改的就是这份配置，改完立刻让会话按新参数重画，并自动写回配置文件。
    /// </summary>
    public MusicGuidedKeyLayoutConfig? KeyLayout { get; set; }

    /// <summary>
    /// 已经贴住游戏窗口（Show() 之后 IsLoaded 不会同步变 true，所以只看句柄）。
    /// </summary>
    public bool IsAttached => _gameHandle != 0 && !_isClosing;

    public (double Width, double Height) CanvasSize
        => (Math.Max(1, OverlayCanvas.ActualWidth), Math.Max(1, OverlayCanvas.ActualHeight));

    /// <summary>画面高度相对 1080P 的缩放比（引导圈参数都是 1080P 基准）</summary>
    private double LayoutScale
    {
        get
        {
            var height = OverlayCanvas.ActualHeight;
            return height > 1 ? height / MusicKeyLayout.ReferenceHeight : 1;
        }
    }

    /// <summary>
    /// 打开窗口并把它贴到游戏窗口上。
    /// </summary>
    public bool Attach(nint gameHandle)
    {
        if (gameHandle == 0)
        {
            return false;
        }

        _gameHandle = gameHandle;

        if (!IsVisible)
        {
            Show();
        }

        UpdateBounds();
        SetClickThrough(true);

        FitStatusPanel();
        _renderTimer.Start();
        _followTimer.Start();
        return true;
    }

    /// <summary>
    /// 引导会话在键盘钩子线程 / 启动线程上回调，这些方法会碰 WPF 元素，统一转到 UI 线程。
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (_isClosing)
        {
            return;
        }

        var dispatcher = Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    public void UpdateDetach()
    {
        OnUiThread(UpdateDetachCore);
    }

    private void UpdateDetachCore()
    {
        _renderTimer.Stop();
        _followTimer.Stop();
        ClearRings();
        _chips.Clear();
        KeyChips.Items.Refresh();
    }

    public void UpdateSegment(int index, int total, IReadOnlyList<MusicGuidedPress> presses,
        IReadOnlyDictionary<char, GuidedKeyPosition> positions, string trackName,
        IReadOnlyList<int> satisfiedPressIndices)
    {
        OnUiThread(() => UpdateSegmentCore(index, total, presses, positions, trackName, satisfiedPressIndices));
    }

    private void UpdateSegmentCore(int index, int total, IReadOnlyList<MusicGuidedPress> presses,
        IReadOnlyDictionary<char, GuidedKeyPosition> positions, string trackName,
        IReadOnlyList<int> satisfiedPressIndices)
    {
        if (_isClosing)
        {
            return;
        }

        _segmentPresses = presses.ToArray();
        RebuildRings(positions);
        _contractionStart = Environment.TickCount64;

        var missing = new List<char>();
        foreach (var key in _segmentPresses.SelectMany(p => p.Keys).Distinct())
        {
            if (!positions.ContainsKey(key))
            {
                missing.Add(key);
            }
        }

        SegmentText.Text = total <= 0
            ? "本段键位"
            : $"第 {index + 1} / {total} 段 · 本段 {presses.Count} 次按键";
        TrackText.Text = trackName;
        UnmappedText.Text = missing.Count == 0
            ? string.Empty
            : $"当前乐器没有这些键：{string.Join(' ', missing)}";
        UnmappedText.Visibility = missing.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        RefreshChips(_segmentPresses);
        HintText.Foreground = HintBrush;
        HintText.Text = DefaultHint;

        ApplyProgress(satisfiedPressIndices, []);
    }

    public void UpdateProgress(IReadOnlyList<int> satisfiedPressIndices, IReadOnlyList<char> heldKeys)
    {
        OnUiThread(() => ApplyProgress(satisfiedPressIndices, heldKeys));
    }

    /// <summary>
    /// 段内进度变化：状态面板给已弹完的按键打勾，画面上的引导圈换成
    /// 「已弹完（绿、缩小变淡）／已按下（亮金、停在琴键上呼吸）／待按（青色锚圈 + 往外扩散的涟漪）」。
    /// 每次都按当前状态整体重算，所以按错清空本段之后也能正确回到「待按」。
    /// </summary>
    private void ApplyProgress(IReadOnlyList<int> satisfiedPressIndices, IReadOnlyList<char> heldKeys)
    {
        if (_isClosing)
        {
            return;
        }

        var satisfied = new HashSet<int>(satisfiedPressIndices);
        var held = heldKeys.Count == 0 ? null : new HashSet<char>(heldKeys);

        for (var i = 0; i < _chips.Count; i++)
        {
            var done = satisfied.Contains(i);
            var chip = _chips[i];
            chip.Background = done ? ChipSatisfiedBrush : ChipPendingBrush;
            chip.BorderBrush = done ? SatisfiedBrush : PendingRingBrush;
            chip.Foreground = done ? ChipTextSatisfiedBrush : ChipTextPendingBrush;
        }

        if (_chips.Count > 0)
        {
            KeyChips.Items.Refresh();
        }

        foreach (var visual in _keys.Values)
        {
            var order = FindCurrentOrder(visual.Key, satisfied);
            if (order < 0)
            {
                // 这个键所参与的那次按键已经弹完了：留一个缩小的绿圈表示「按过了」
                visual.SetSatisfied();
            }
            else if (held != null && held.Contains(visual.Key))
            {
                visual.SetHeld();
            }
            else
            {
                visual.SetActive();
            }
        }
    }

    public void UpdateError(string wrongKeys)
    {
        OnUiThread(() => UpdateErrorCore(wrongKeys));
    }

    private void UpdateErrorCore(string wrongKeys)
    {
        if (_isClosing)
        {
            return;
        }

        HintText.Foreground = ErrorBrush;
        HintText.Text = string.IsNullOrEmpty(wrongKeys)
            ? "按错了，本段重新来一次"
            : $"按错（{wrongKeys}），本段重新来一次";

        foreach (var chip in _chips)
        {
            chip.Background = ChipErrorBrush;
            chip.BorderBrush = ErrorBrush;
            chip.Foreground = ChipTextErrorBrush;
        }

        KeyChips.Items.Refresh();

        var now = Environment.TickCount64;
        foreach (var visual in _keys.Values)
        {
            if (visual.IsActive)
            {
                visual.FlashError(now);
            }
        }

        ScheduleHintRestore();
    }

    /// <summary>玩家抢拍（段内顺序不对）：不清段、不报错，只提示当前该按的是哪一次按键</summary>
    public void UpdateOrderHint(string expectedKeys)
    {
        OnUiThread(() => UpdateOrderHintCore(expectedKeys));
    }

    private void UpdateOrderHintCore(string expectedKeys)
    {
        if (_isClosing)
        {
            return;
        }

        ShowTransientHint(HeldRingBrush, string.IsNullOrEmpty(expectedKeys)
            ? "按顺序弹：先弹前面还没弹完的那次按键"
            : $"按顺序弹：先按 {expectedKeys}");
    }

    public void UpdateCompleted(string trackName)
    {
        OnUiThread(() => UpdateCompletedCore(trackName));
    }

    private void UpdateCompletedCore(string trackName)
    {
        if (_isClosing)
        {
            return;
        }

        HintText.Foreground = SatisfiedBrush;
        HintText.Text = string.IsNullOrEmpty(trackName)
            ? "整首曲子都弹完了！"
            : $"《{trackName}》整首都弹完了！";

        ClearRings();
    }

    public void UpdateStopped(string reason)
    {
        OnUiThread(() => UpdateStoppedCore(reason));
    }

    /// <summary>显示键位来源（自动识别到的分辨率/参数，或回退到配置参数的原因）</summary>
    public void UpdateDetection(string text)
    {
        OnUiThread(() => UpdateDetectionCore(text));
    }

    private void UpdateDetectionCore(string text)
    {
        if (_isClosing)
        {
            return;
        }

        DetectionText.Text = text ?? string.Empty;
    }

    private void UpdateStoppedCore(string reason)
    {
        if (_isClosing)
        {
            return;
        }

        HintText.Foreground = HintBrush;
        HintText.Text = string.IsNullOrEmpty(reason) ? "引导式跟弹已结束" : $"引导式跟弹已结束：{reason}";

        ClearRings();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetClickThrough(true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosing = true;
        _renderTimer.Stop();
        _followTimer.Stop();
        _hintTimer?.Stop();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke();
    }

    /// <summary>
    /// 找出这个键「还没弹完」的那次按键的段内序号；都弹完了（或本段里没有这个键）返回 -1。
    /// </summary>
    private int FindCurrentOrder(char key, HashSet<int> satisfied)
    {
        for (var i = 0; i < _segmentPresses.Length; i++)
        {
            if (!satisfied.Contains(i) && _segmentPresses[i].Keys.Contains(key))
            {
                return i;
            }
        }

        return -1;
    }

    private void ClearRings()
    {
        foreach (var visual in _keys.Values)
        {
            OverlayCanvas.Children.Remove(visual.Root);
        }

        _keys.Clear();
    }

    /// <summary>
    /// 按当前段落（以及现场微调后的位置）重建引导圈：本段要按的每个键一个圈。
    /// </summary>
    private void RebuildRings(IReadOnlyDictionary<char, GuidedKeyPosition> positions)
    {
        ClearRings();

        var thickness = Math.Max(1.5, (KeyLayout?.RingThickness ?? 3.2) * LayoutScale);
        foreach (var key in _segmentPresses.SelectMany(p => p.Keys).Distinct())
        {
            if (!positions.TryGetValue(key, out var position))
            {
                // 当前乐器没有这个键：不画圈，由状态面板里的提示列出来
                continue;
            }

            var visual = new KeyVisual(key, position, thickness);
            _keys[key] = visual;
            OverlayCanvas.Children.Add(visual.Root);
        }
    }

    /// <summary>
    /// 把窗口贴到游戏客户区上（与 HtmlMaskWindow 的定位方式一致）。
    /// </summary>
    private void UpdateBounds()
    {
        if (_gameHandle == 0)
        {
            return;
        }

        var rect = SystemControl.GetCaptureRect(_gameHandle);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var scale = DpiHelper.GetScale(_gameHandle);
        _dpiScaleX = scale.X <= 0 ? 1 : scale.X;
        _dpiScaleY = scale.Y <= 0 ? 1 : scale.Y;

        Left = rect.Left / _dpiScaleX;
        Top = rect.Top / _dpiScaleY;
        Width = rect.Width / _dpiScaleX;
        Height = rect.Height / _dpiScaleY;
    }

    /// <summary>
    /// 游戏窗口被移动 / 缩放时跟随，并保证自己始终在最上层。
    /// </summary>
    private void FollowGameWindow()
    {
        if (_gameHandle == 0 || _isClosing)
        {
            return;
        }

        var rect = SystemControl.GetCaptureRect(_gameHandle);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var expectedWidth = rect.Width / _dpiScaleX;
        var expectedHeight = rect.Height / _dpiScaleY;
        if (Math.Abs(Width - expectedWidth) > 0.5 || Math.Abs(Height - expectedHeight) > 0.5
            || Math.Abs(Left - rect.Left / _dpiScaleX) > 0.5 || Math.Abs(Top - rect.Top / _dpiScaleY) > 0.5)
        {
            UpdateBounds();
        }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != 0)
        {
            _ = SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    /// <summary>
    /// 点击穿透 + 永不抢焦点：不接收鼠标，激活时也不会把游戏窗口顶下去
    /// （用户要求「进入演奏后自动回到游戏界面」，覆盖层自己不能抢走前台）。
    /// </summary>
    public void SetClickThrough(bool enabled)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == 0)
        {
            return;
        }

        var style = User32.GetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE);
        var value = (long)style;
        if (enabled)
        {
            value |= (long)User32.WindowStylesEx.WS_EX_TRANSPARENT;
            value |= (long)User32.WindowStylesEx.WS_EX_LAYERED;
            value |= (long)User32.WindowStylesEx.WS_EX_TOOLWINDOW;
            value |= (long)User32.WindowStylesEx.WS_EX_NOACTIVATE;
        }
        else
        {
            value &= ~(long)User32.WindowStylesEx.WS_EX_TRANSPARENT;
            value &= ~(long)User32.WindowStylesEx.WS_EX_NOACTIVATE;
        }

        User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, (nint)value);
    }

    /// <summary>
    /// 面板宽度按窗口宽度收窄，避免在窄游戏窗口里溢出。
    /// </summary>
    private void FitStatusPanel()
    {
        StatusPanel.MaxWidth = Math.Max(240, Width - 80);
    }

    private void RefreshChips(IReadOnlyList<MusicGuidedPress> presses)
    {
        _chips.Clear();
        foreach (var press in presses)
        {
            _chips.Add(new ChipVisual(press.Display));
        }

        KeyChips.Items.Refresh();
    }

    private void ScheduleHintRestore()
    {
        var restore = new DispatcherTimer { Interval = ErrorFlashDuration };
        restore.Tick += (_, _) =>
        {
            restore.Stop();
            if (!_isClosing)
            {
                HintText.Foreground = HintBrush;
            }
        };
        restore.Start();
    }

    /// <summary>
    /// 游戏是否在前台（结果缓存 <see cref="GameActiveCheckInterval"/> 毫秒，避免每帧查一次进程名）。
    ///
    /// 覆盖层是置顶且点击穿透的，玩家随时可能切到别的程序；这时按下的 Esc / Ctrl 组合键都不该
    /// 作用到引导会话上，否则在别处关个对话框就把整个跟弹会话退掉了。
    /// </summary>
    private bool IsGameActive()
    {
        var now = Environment.TickCount64;
        if (now - _gameActiveCheckedAt < GameActiveCheckInterval)
        {
            return _gameActiveCached;
        }

        _gameActiveCheckedAt = now;
        _gameActiveCached = SystemControl.IsGenshinImpactActiveByProcess();
        return _gameActiveCached;
    }

    /// <summary>
    /// 每帧刷新：让每个待按的键上的锚圈呼吸、涟漪往外扩散淡出。
    /// </summary>
    private void RenderFrame()
    {
        if (_isClosing)
        {
            return;
        }

        // 窗口是点击穿透的、拿不到键盘焦点，所以用异步键状态检测来支持 Esc 退出与现场微调。
        // 只在游戏处于前台时才认这些热键：切到别的程序后按 Esc / Ctrl 不该影响引导会话。
        var gameActive = IsGameActive();
        if (gameActive && IsKeyDown((int)User32.VK.VK_ESCAPE))
        {
            if (!_escapeHeld)
            {
                _escapeHeld = true;
                ExitRequested?.Invoke();
            }
        }
        else
        {
            _escapeHeld = false;
        }

        PollCalibration(gameActive);

        // 游戏窗口在会话进行中被移动 / 缩放时，让会话把键位重新算一遍。
        PumpSession();

        if (_keys.Count == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        var period = Math.Max(200, ContractionPeriod.TotalMilliseconds);
        var phase = (now - _contractionStart) / period;

        foreach (var visual in _keys.Values)
        {
            visual.Render(phase, now);
        }
    }

    /// <summary>
    /// 画布尺寸变化时让会话重新换算键位；会话回调本窗口，加个重入保护。
    /// </summary>
    private void PumpSession()
    {
        if (_isPumping || _session == null)
        {
            return;
        }

        _isPumping = true;
        try
        {
            _session.Pump();
        }
        finally
        {
            _isPumping = false;
        }
    }

    /// <summary>
    /// 现场微调引导圈位置：按住 Ctrl 再按方向键 / [ ] / , . / PgUp PgDn。
    /// 只有「游戏在前台 + 按住 Ctrl」才响应，避免正常弹奏或切到别的程序时误触把键位改跑。
    /// </summary>
    private void PollCalibration(bool gameActive)
    {
        if (KeyLayout == null || _isClosing || _session == null)
        {
            return;
        }

        if (!gameActive || !IsKeyDown(VkControl))
        {
            // 不在游戏前台、或者 Ctrl 一松开就清掉按键记录，
            // 避免下次按下被当成「还按着」而漏掉一次微调
            if (_downKeys.Count > 0)
            {
                _downKeys.Clear();
            }

            return;
        }

        var layout = KeyLayout;
        var step = IsKeyDown(VkShift) ? 10d : 1d;
        var changed = false;

        if (PressedOnce(VkLeft))
        {
            layout.BaseX -= step;
            changed = true;
        }

        if (PressedOnce(VkRight))
        {
            layout.BaseX += step;
            changed = true;
        }

        if (PressedOnce(VkUp))
        {
            layout.BaseY -= step;
            changed = true;
        }

        if (PressedOnce(VkDown))
        {
            layout.BaseY += step;
            changed = true;
        }

        if (PressedOnce(VkOemMinus))
        {
            layout.ColumnSpacing = Math.Max(10, layout.ColumnSpacing - step);
            changed = true;
        }

        if (PressedOnce(VkOemPlus))
        {
            layout.ColumnSpacing += step;
            changed = true;
        }

        if (PressedOnce(VkOem4))
        {
            layout.RowIndent = Math.Max(0, layout.RowIndent - step);
            changed = true;
        }

        if (PressedOnce(VkOem6))
        {
            layout.RowIndent += step;
            changed = true;
        }

        if (PressedOnce(VkOemComma))
        {
            layout.RowSpacing = Math.Max(10, layout.RowSpacing - step);
            changed = true;
        }

        if (PressedOnce(VkOemPeriod))
        {
            layout.RowSpacing += step;
            changed = true;
        }

        if (PressedOnce(VkPrior))
        {
            layout.RingRadius = Math.Max(8, layout.RingRadius - step);
            changed = true;
        }

        if (PressedOnce(VkNext))
        {
            layout.RingRadius += step;
            changed = true;
        }

        if (changed)
        {
            ApplyCalibration();
        }
        else if (PressedOnce(VkR))
        {
            // Ctrl+R：重新从游戏画面里识别琴键的实际位置（换分辨率、改窗口大小之后不用重开）
            RedetectKeys();
        }
        else if (PressedOnce(VkReturn))
        {
            ShowCalibrationStatus();
        }
    }

    /// <summary>
    /// 重新识别键位：会话会先把覆盖层从屏幕上撤下来（免得把引导圈自己截进去），
    /// 抓一帧游戏画面重新量，量到就写回键位参数并立刻重画。
    ///
    /// 抓帧与识别在会话里被放到后台线程（见 <see cref="MusicGuidedSession.RedetectKeysAsync"/>）：
    /// 这里是键盘钩子回调所在的 UI 线程，同步等着识别完成会把按键回调堵住，也会把覆盖层的定时器卡住。
    /// </summary>
    private async void RedetectKeys()
    {
        if (_isPumping || _session == null)
        {
            return;
        }

        _isPumping = true;
        try
        {
            if (!await _session.RedetectKeysAsync())
            {
                HintText.Foreground = HintBrush;
                HintText.Text = "没能从画面里认出琴键，仍按当前参数画圈";
                ScheduleHintRestore();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"重新识别键位失败：{ex}");
        }
        finally
        {
            _isPumping = false;
        }
    }

    /// <summary>
    /// 微调生效：让会话按新参数重算位置并重画当前段（圈立刻跟着走），
    /// 并把当前数值显示在状态面板上。布局配置是 ObservableObject，
    /// 它的属性变更会被全局配置追踪器捕获并自动保存到 config.json。
    /// </summary>
    private void ApplyCalibration()
    {
        if (_isPumping || _session == null)
        {
            return;
        }

        // 记下「这套数字是玩家自己调的」：下次开始引导就不要再自动识别把它们覆盖掉
        // （用户要求：手动微调保存后，下次打开还是上次调好的位置）。
        if (KeyLayout != null)
        {
            KeyLayout.ManualCalibrated = true;
        }

        _isPumping = true;
        try
        {
            _session.RefreshLayout();
        }
        finally
        {
            _isPumping = false;
        }

        ShowCalibrationStatus();
    }

    private void ShowCalibrationStatus()
    {
        var layout = KeyLayout;
        if (layout == null || _isClosing)
        {
            return;
        }

        ShowTransientHint(AccentBrush, $"键位：X {layout.BaseX:0}  Y {layout.BaseY:0} · 键距 {layout.ColumnSpacing:0} · "
                                       + $"排距 {layout.RowSpacing:0} · 缩进 {layout.RowIndent:0} · 圈 {layout.RingRadius:0}（已自动保存，下次打开沿用）");
    }

    /// <summary>在提示行上显示一句话，几秒后自动恢复成默认提示</summary>
    private void ShowTransientHint(Brush brush, string text)
    {
        HintText.Foreground = brush;
        HintText.Text = text;

        _hintTimer ??= CreateHintTimer();
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private DispatcherTimer CreateHintTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_isClosing)
            {
                HintText.Foreground = HintBrush;
                HintText.Text = DefaultHint;
            }
        };
        return timer;
    }

    /// <summary>异步键状态：这个键现在按着吗</summary>
    private static bool IsKeyDown(int virtualKey)
        => (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>只在「刚按下」的那一帧返回 true（长按不会连续触发）</summary>
    private bool PressedOnce(int virtualKey)
    {
        if (!IsKeyDown(virtualKey))
        {
            _downKeys.Remove(virtualKey);
            return false;
        }

        return _downKeys.Add(virtualKey);
    }

    /// <summary>引导圈的四种状态</summary>
    private enum KeyState
    {
        /// <summary>不在本段里</summary>
        Idle,

        /// <summary>本段要按，还没按</summary>
        Pending,

        /// <summary>和弦里已经按住，但这次按键还没按全</summary>
        Held,

        /// <summary>这一次按键已经弹对</summary>
        Satisfied
    }

    /// <summary>
    /// 一个引导圈 = 套在琴键边缘上的「锚圈」+ 一圈向外扩散的涟漪。
    ///
    /// 锚圈不做收缩动画：它就停在琴键边缘上（半径 = 实测的琴键按钮半径），随时都能看出有没有套准，
    /// 不会出现「圈缩到一半、看不出到底该套哪」的情况；「该按了」由从锚圈往外扩散淡出的涟漪表达。
    /// 锚圈是深色描边 + 高饱和亮色细环两层，压在米白琴键上也够显眼。
    ///
    /// 圈在最下方留了一个缺口（<see cref="GapHalfAngle"/>）：原神琴键按钮的底部压着唱名
    /// （do / re / mi …）和键位字母块，整圆会从它们上面划过去，留出口之后圈只贴着按钮的边缘，
    /// 挡住了键位就看不清该按哪个了。
    /// </summary>
    private sealed class KeyVisual
    {
        /// <summary>底部缺口半角（度）：不画的部分 = 正下方 ± 这个角度</summary>
        private const double GapHalfAngle = 45d;

        /// <summary>锚圈：深色描边 + 亮色细环（始终停在琴键边缘）</summary>
        private readonly List<RingShape> _anchor = [];

        /// <summary>涟漪：从锚圈往外扩散淡出的亮色圈</summary>
        private readonly List<RingShape> _ripple = [];

        private readonly ScaleTransform _anchorScale = new(1, 1);
        private readonly ScaleTransform _rippleScale = new(1, 1);

        private KeyState _state = KeyState.Pending;
        private long _errorStart = -1;

        public KeyVisual(char key, GuidedKeyPosition position, double thickness)
        {
            Key = key;

            var radius = Math.Max(6, position.Size / 2);
            var box = radius * 2 * 1.9;

            Root = new Canvas
            {
                Width = box,
                Height = box,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(Root, position.X - box / 2);
            Canvas.SetTop(Root, position.Y - box / 2);

            var center = box / 2;

            var anchorHost = CreateLayer(box, _anchorScale);
            AddArc(anchorHost, _anchor, center, radius, thickness * 2.4, RingHaloBrush, 0.45, false);
            AddArc(anchorHost, _anchor, center, radius, thickness, PendingRingBrush, 1);
            Root.Children.Add(anchorHost);

            var rippleHost = CreateLayer(box, _rippleScale);
            AddArc(rippleHost, _ripple, center, radius, Math.Max(2, thickness * 1.6), PendingRingBrush, 0.6,
                true, new BlurEffect { Radius = Math.Max(3, thickness * 2.2), KernelType = KernelType.Gaussian });
            Root.Children.Add(rippleHost);
        }

        public char Key { get; }

        public Canvas Root { get; }

        public bool IsActive => _state != KeyState.Idle;

        public void SetIdle()
        {
            _state = KeyState.Idle;
            Repaint();
        }

        public void SetActive()
        {
            _state = KeyState.Pending;
            _errorStart = -1;
            Repaint();
        }

        public void SetHeld()
        {
            _state = KeyState.Held;
            _errorStart = -1;
            Repaint();
        }

        public void SetSatisfied()
        {
            _state = KeyState.Satisfied;
            _errorStart = -1;
            Repaint();
        }

        public void FlashError(long now)
        {
            _errorStart = now;
            ApplyBrush(ErrorRingBrush);
            SetLayerOpacity(_anchor, 1);
            SetLayerOpacity(_ripple, 0.8);
            _anchorScale.ScaleX = _anchorScale.ScaleY = 1.06;
            _rippleScale.ScaleX = _rippleScale.ScaleY = 1.06;
            Root.Opacity = 1;
        }

        /// <summary>按当前状态设置静态外观（不参与呼吸动画）</summary>
        private void Repaint()
        {
            if (_state == KeyState.Idle)
            {
                Root.Opacity = 0;
                return;
            }

            Root.Opacity = 1;
            ApplyBrush(_state switch
            {
                KeyState.Held => HeldRingBrush,
                KeyState.Satisfied => SatisfiedRingBrush,
                _ => PendingRingBrush
            });

            // 已弹完：整圈缩小变淡，留个「按过了」的痕迹（绿色的淡淡一小圈）
            var done = _state == KeyState.Satisfied;
            var scale = done ? 0.62 : 1d;
            _anchorScale.ScaleX = _anchorScale.ScaleY = scale;
            _rippleScale.ScaleX = _rippleScale.ScaleY = scale;
            SetLayerOpacity(_anchor, done ? 0.5 : 1);
            SetLayerOpacity(_ripple, done ? 0.2 : 0);
        }

        /// <summary>
        /// 每帧刷新。待按时锚圈原地轻轻呼吸、涟漪从锚圈往外扩散淡出；按住后锚圈呼吸、涟漪收起；
        /// 红闪期间做一次心跳式脉冲。
        /// </summary>
        public void Render(double phase, long now)
        {
            if (_state == KeyState.Idle)
            {
                return;
            }

            if (_errorStart >= 0)
            {
                if (now - _errorStart >= ErrorFlashDuration.TotalMilliseconds)
                {
                    _errorStart = -1;
                    Repaint();
                    return;
                }

                var pulse = 1.06 + 0.06 * Math.Sin((now - _errorStart) / 55d);
                _anchorScale.ScaleX = _anchorScale.ScaleY = pulse;
                _rippleScale.ScaleX = _rippleScale.ScaleY = pulse;
                return;
            }

            if (_state == KeyState.Satisfied)
            {
                return;
            }

            if (_state == KeyState.Held)
            {
                // 已经按住：锚圈原地呼吸，不再往外扩散（等这一组和弦按全）
                _anchorScale.ScaleX = _anchorScale.ScaleY = 1 + 0.03 * Math.Sin(phase * 4 * Math.PI);
                SetLayerOpacity(_anchor, 1);
                SetLayerOpacity(_ripple, 0);
                return;
            }

            // 待按：锚圈始终标在琴键上（轻微呼吸），涟漪从锚圈往外扩散并淡出，提示「现在按」
            var breath = 0.5 + 0.5 * Math.Sin(phase * 2 * Math.PI);
            _anchorScale.ScaleX = _anchorScale.ScaleY = 1 + 0.015 * breath;
            SetLayerOpacity(_anchor, 0.8 + 0.2 * breath);

            var cycle = phase - Math.Floor(phase);
            var grow = 1 + 0.32 * cycle;
            _rippleScale.ScaleX = _rippleScale.ScaleY = grow;
            var fade = cycle < 0.15 ? cycle / 0.15 : 1 - (cycle - 0.15) / 0.85;
            SetLayerOpacity(_ripple, fade);
        }

        /// <summary>换整套圈的颜色（深色描边不跟着变，它只负责压底色）</summary>
        private void ApplyBrush(Brush brush)
        {
            foreach (var shape in _anchor.Where(x => x.Tinted))
            {
                shape.Shape.Stroke = brush;
            }

            foreach (var shape in _ripple.Where(x => x.Tinted))
            {
                shape.Shape.Stroke = brush;
            }
        }

        private static void SetLayerOpacity(List<RingShape> layer, double factor)
        {
            foreach (var shape in layer)
            {
                shape.Shape.Opacity = shape.BaseOpacity * factor;
            }
        }

        /// <summary>一层圈：一个按圆心对齐、可整体缩放的画布</summary>
        private static Canvas CreateLayer(double size, ScaleTransform scale)
        {
            return new Canvas
            {
                Width = size,
                Height = size,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale
            };
        }

        /// <summary>
        /// 画一段圆弧（底部留缺口）。角度按 WPF 画布算：y 轴向下，90° 正好是正下方，
        /// 缺口 = 90°±<see cref="GapHalfAngle"/>，其余 270° 用一条大弧补上。
        /// </summary>
        private static void AddArc(Canvas host, List<RingShape> layer, double center, double radius,
            double thickness, Brush brush, double opacity, bool tinted = true, Effect? effect = null)
        {
            var gap = GapHalfAngle * Math.PI / 180d;
            var startAngle = Math.PI / 2 + gap;
            var endAngle = Math.PI / 2 - gap;

            var figure = new PathFigure
            {
                StartPoint = new Point(center + radius * Math.Cos(startAngle), center + radius * Math.Sin(startAngle)),
                IsClosed = false,
                IsFilled = false
            };
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(center + radius * Math.Cos(endAngle), center + radius * Math.Sin(endAngle)),
                Size = new Size(radius, radius),
                IsLargeArc = true,
                SweepDirection = SweepDirection.Clockwise
            });

            var path = new Path
            {
                Data = new PathGeometry([figure]),
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = opacity,
                IsHitTestVisible = false
            };
            if (effect != null)
            {
                path.Effect = effect;
            }

            host.Children.Add(path);
            layer.Add(new RingShape(path, opacity, tinted));
        }

        private sealed record RingShape(Shape Shape, double BaseOpacity, bool Tinted);
    }

    /// <summary>
    /// 状态面板上的键位方块。宽度自适应：和弦的文本是 "E + T" 这种，比单键长得多，
    /// 字号按文本长度降一档，保证整段的键位都能完整显示出来。
    /// </summary>
    private sealed class ChipVisual(string text)
    {
        public string Text { get; set; } = text;

        /// <summary>单键 19，两三个键的和弦 15，更长的和弦 13</summary>
        public double FontSize { get; } = text.Length switch
        {
            <= 2 => 19,
            <= 5 => 15,
            _ => 13
        };

        public Brush Background { get; set; } = ChipPendingBrush;

        public Brush BorderBrush { get; set; } = PendingRingBrush;

        public Brush Foreground { get; set; } = ChipTextPendingBrush;
    }
}
