using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Infrastructure.WebView;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using Wpf.Ui.Violeta.Controls;
using FormsScreen = System.Windows.Forms.Screen;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 悬浮小窗的协调服务：唯一决定小窗显示、穿透、焦点和播放命令的地方（见设计文档 5.3、6.2 节）。
/// 汇总用户偏好、悬停与交互、游戏前台、任务模式、网页状态，按 <see cref="GuideWindowPresentation"/> 计算结果后交给窗口呈现。
/// 公开方法可以在任意线程调用，内部状态只在界面线程读写
/// </summary>
public sealed class GuideWindowService : IGuideWindowService, IHostedService, IDisposable
{
    /// <summary>默认宽度（DIP）</summary>
    private const double DefaultWidthDip = 480;

    /// <summary>默认高度（DIP）</summary>
    private const double DefaultHeightDip = 270;

    /// <summary>最小宽度（DIP）</summary>
    public const double MinWidthDip = 320;

    /// <summary>最小高度（DIP）</summary>
    public const double MinHeightDip = 180;

    /// <summary>默认与游戏画面或屏幕边缘的距离（DIP）</summary>
    private const double DefaultMarginDip = 16;

    /// <summary>倍速下限</summary>
    private const double MinRate = 0.25;

    /// <summary>倍速上限</summary>
    private const double MaxRate = 4;

    /// <summary>播放中保存进度的间隔</summary>
    private static readonly TimeSpan ProgressSaveInterval = TimeSpan.FromSeconds(10);

    /// <summary>自动打开后暂停网页自动播放的时限（毫秒）</summary>
    private const long AutoplaySuppressMs = 10_000;

    /// <summary>打开网页后等待视频加载并续播的时限（毫秒）</summary>
    private const long ResumeWindowMs = 30_000;

    /// <summary>网站已自己续播的判断阈值（秒）：加载后位置超过它时不再跳转</summary>
    private const double SiteResumedThreshold = 3;

    private readonly AllConfig _allConfig;
    private readonly GuideLibraryStore _library;
    private readonly GameRuntimeService _runtimeService;
    private readonly TaskTriggerDispatcher _dispatcher;
    private readonly IMaskWindowHost _maskWindowHost;
    private readonly Func<IGuideWindowView> _windowFactory;
    private readonly Func<IGuideSubtitleView> _subtitleFactory;
    private readonly ILogger<GuideWindowService> _logger;

    /// <summary>
    /// 游戏窗口事件监听
    /// </summary>
    private readonly GuideGameWatcher _watcher = new();

    /// <summary>
    /// 播放中定时保存进度
    /// </summary>
    private DispatcherTimer? _progressTimer;

    /// <summary>
    /// 交互模式下鼠标离开后自动结束
    /// </summary>
    private DispatcherTimer? _interactionExitTimer;

    /// <summary>
    /// 意外获得焦点后，等鼠标松开再把焦点还给游戏
    /// </summary>
    private DispatcherTimer? _focusRestoreTimer;

    /// <summary>当前小窗，未打开时为 null</summary>
    private IGuideWindowView? _view;

    /// <summary>当前小窗的网页播放器</summary>
    private IGuidePlayer? _player;

    /// <summary>字幕浮窗，第一次需要显示时创建</summary>
    private IGuideSubtitleView? _subtitleView;

    /// <summary>被用户隐藏</summary>
    private bool _hiddenByUser;

    /// <summary>因任务运行被隐藏</summary>
    private bool _hiddenByTask;

    /// <summary>任务运行中用户主动显示</summary>
    private bool _taskForceShown;

    /// <summary>因游戏最小化或切到后台被隐藏</summary>
    private bool _hiddenByGame;

    /// <summary>游戏在后台时用户主动显示，下一次游戏前台变化时清除</summary>
    private bool _gameForceShown;

    /// <summary>交互模式</summary>
    private bool _interaction;

    /// <summary>进入交互模式前游戏是否在前台，结束时据此把焦点还给游戏</summary>
    private bool _returnFocusAfterInteraction;

    /// <summary>鼠标悬停在小窗上</summary>
    private bool _pointerOver;

    /// <summary>鼠标穿透时，可见的鼠标指针位于小窗上</summary>
    private bool _cursorInside;

    /// <summary>临时加速是否生效</summary>
    private bool _boostActive;

    /// <summary>临时加速前的倍速</summary>
    private double _boostPreviousRate = 1;

    /// <summary>临时加速使用的倍速</summary>
    private double _boostAppliedRate = 2;

    /// <summary>临时加速开始的时间（TickCount64）</summary>
    private long _boostStartedAt;

    /// <summary>在这个时间（TickCount64）之前网页开始播放时暂停一次</summary>
    private long _suppressAutoplayUntil;

    /// <summary>等待续播的请求</summary>
    private PendingResume? _pendingResume;

    /// <summary>当前视频标识</summary>
    private string? _currentVideoKey;

    /// <summary>上一次状态是否暂停，用于在暂停时保存进度</summary>
    private bool _lastPaused = true;

    /// <summary>当前视频的进度快照，定时写入观看记录</summary>
    private ProgressSnapshot? _progress;

    /// <summary>
    /// 等待续播的请求
    /// </summary>
    /// <param name="Source">打开来源</param>
    /// <param name="Until">截止时间（TickCount64）</param>
    private sealed record PendingResume(GuideOpenSource Source, long Until);

    /// <summary>
    /// 进度快照
    /// </summary>
    private sealed record ProgressSnapshot(string VideoKey, string Title, string Url, double Position, double Duration, double Rate);

    public GuideWindowService(
        IConfigService configService,
        GuideLibraryStore library,
        GameRuntimeService runtimeService,
        TaskTriggerDispatcher dispatcher,
        IMaskWindowHost maskWindowHost,
        InstanceBootstrap instanceBootstrap,
        Func<IGuideWindowView> windowFactory,
        Func<IGuideSubtitleView> subtitleFactory,
        ILogger<GuideWindowService> logger)
    {
        _allConfig = configService.Get();
        _library = library;
        _runtimeService = runtimeService;
        _dispatcher = dispatcher;
        _maskWindowHost = maskWindowHost;
        _windowFactory = windowFactory;
        _subtitleFactory = subtitleFactory;
        _logger = logger;
        IsAvailable = instanceBootstrap.Context.IsRoot;
        State.IsTopmost = Config.Topmost;
        State.IsImmersive = Config.RememberImmersive && Config.Immersive;
    }

    /// <inheritdoc />
    public GuideWindowState State { get; } = new();

    /// <inheritdoc />
    public GuideWindowConfig Config => _allConfig.GuideWindowConfig;

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <summary>
    /// 小窗是否打开
    /// </summary>
    private bool IsOpen => _view is not null;

    #region 生命周期

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return Task.CompletedTask;
        }

        RunOnUi(() =>
        {
            var dispatcher = Application.Current.Dispatcher;
            _progressTimer = new DispatcherTimer(ProgressSaveInterval, DispatcherPriority.Background, (_, _) => SaveProgress(), dispatcher);
            _interactionExitTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
            _interactionExitTimer.Tick += (_, _) =>
            {
                _interactionExitTimer.Stop();
                EndInteractionCore(returnFocus: true);
            };
            _focusRestoreTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Normal, OnFocusRestoreTick, dispatcher);
            _focusRestoreTimer.Stop();

            Config.PropertyChanged += OnConfigChanged;
            _runtimeService.Started += OnRuntimeStarted;
            _runtimeService.Stopped += OnRuntimeStopped;
            _dispatcher.TaskModeChanged += OnTaskModeChanged;
            _maskWindowHost.StateChanged += OnMaskWindowStateChanged;
            // 这两个事件在系统事件钩子的回调里同步触发，异常一旦抛回系统会使整个进程崩溃，经 Invoke 只记录日志
            _watcher.StateChanged += (s, e) => Invoke(() => OnGameStateChanged(s, e));
            _watcher.GameMoved += (s, e) => Invoke(() => OnGameMoved(s, e));

            // 启动时截图器已经在运行（例如通过命令行启动）
            if (_runtimeService.Current is not null)
            {
                OnRuntimeStarted(this, EventArgs.Empty);
            }
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        SaveProgress();
        _library.Flush();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _watcher.Dispose();
    }

    #endregion

    #region 打开、显示与关闭

    /// <inheritdoc />
    public void Open(GuideOpenRequest? request = null) => RunOnUi(() => OpenCore(request));

    /// <inheritdoc />
    public void ToggleVisible() => RunOnUi(() =>
    {
        if (!IsOpen)
        {
            OpenCore(null);
            return;
        }

        if (State.IsShown)
        {
            HideCore();
            return;
        }

        ShowCore();
    });

    /// <inheritdoc />
    public void Hide() => RunOnUi(HideCore);

    /// <inheritdoc />
    public void Close() => RunOnUi(() =>
    {
        if (_view is not { } view)
        {
            return;
        }

        SaveProgress();
        _library.FlushInBackground();
        Config.WasOpen = false;
        // 先解除关联再关闭窗口：窗口销毁期间会处理积压的系统事件（例如前台窗口变化），重入的回调
        // 此时已把小窗视为关闭，不会再去定位或显示正在销毁的窗口
        ReleaseView();
        view.CloseWindow();
    });

    /// <inheritdoc />
    public void RestoreEmergency() => RunOnUi(() =>
    {
        if (!IsOpen)
        {
            OpenCore(null);
        }

        _hiddenByUser = false;
        _taskForceShown = _hiddenByTask;
        _gameForceShown = _hiddenByGame;
        EndBoost();
        if (_interaction)
        {
            EndInteractionCore(returnFocus: false);
        }

        if (State.IsImmersive)
        {
            SetImmersiveCore(false, showOsd: false);
        }

        if (State.IsSubtitleAdjusting)
        {
            State.IsSubtitleAdjusting = false;
        }

        Config.IsClickThrough = false;
        Config.Opacity = 1;
        Recompute();
        ApplyPosition();
        _view?.BringToTopmost();
        State.ShowOsd("已恢复小窗：鼠标穿透已关闭，不透明度 100%", 2500);
    });

    /// <summary>
    /// 打开小窗或显示已打开的小窗；请求中有网址时打开该网址
    /// </summary>
    private void OpenCore(GuideOpenRequest? request)
    {
        if (!IsAvailable)
        {
            Toast.Warning("悬浮小窗只能在主实例中使用");
            return;
        }

        if (!WebViewRuntime.IsAvailable)
        {
            Toast.Warning("悬浮小窗需要 Microsoft Edge WebView2 运行时，请安装后重试");
            return;
        }

        var created = false;
        if (_view is null)
        {
            try
            {
                CreateView();
                created = true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "打开悬浮小窗失败");
                Toast.Error($"打开悬浮小窗失败：{e.Message}");
                return;
            }
        }

        // 自动打开不打断游戏状态；用户主动打开时，即使当前被自动隐藏也显示出来
        if (request?.Source != GuideOpenSource.AutoOpen)
        {
            _hiddenByUser = false;
            _taskForceShown = _hiddenByTask;
            _gameForceShown = _hiddenByGame;
        }

        var url = request?.Url;
        if (string.IsNullOrWhiteSpace(url) && created)
        {
            url = _library.LastUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = string.IsNullOrWhiteSpace(Config.HomeUrl) ? GuideWindowConfig.DefaultHomeUrl : Config.HomeUrl;
            }
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            NavigateCore(url, request?.Source ?? GuideOpenSource.User);
        }

        Recompute();
        if (created && !Config.AltHintShown)
        {
            Config.AltHintShown = true;
            State.ShowOsd("提示：在游戏中按住 Alt 把鼠标移到小窗上即可操作", 4500);
        }
    }

    /// <summary>
    /// 创建小窗并挂接网页事件，位置在显示前设置好
    /// </summary>
    private void CreateView()
    {
        var view = _windowFactory();
        _view = view;
        _player = view.Player;
        _player.StateChanged += OnPlayerStateChanged;
        _player.SubtitleChanged += OnPlayerSubtitleChanged;
        _player.EventRaised += OnPlayerEventRaised;
        _player.SetZoom(Config.ZoomFactor);
        view.Closed += OnViewClosed;
        Config.WasOpen = true;
        State.IsOpen = true;
        State.NoticeText = string.Empty;
        ApplyPosition();
        _progressTimer?.Start();
    }

    /// <summary>
    /// 用户显示小窗：清除用户隐藏，被自动隐藏时也临时显示
    /// </summary>
    private void ShowCore()
    {
        _hiddenByUser = false;
        _taskForceShown = _hiddenByTask;
        _gameForceShown = _hiddenByGame;
        Recompute();
    }

    /// <summary>
    /// 用户隐藏小窗，保留网页
    /// </summary>
    private void HideCore()
    {
        if (!IsOpen)
        {
            return;
        }

        _hiddenByUser = true;
        _taskForceShown = false;
        _gameForceShown = false;
        Recompute();
    }

    /// <summary>
    /// 小窗不经服务关闭（程序退出）时清理状态；服务主动关闭时已在关闭前清理并取消了订阅
    /// </summary>
    private void OnViewClosed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _view))
        {
            return;
        }

        ReleaseView();
        _logger.LogDebug("悬浮小窗随程序关闭");
    }

    /// <summary>
    /// 解除服务与当前小窗的关联并清理状态，之后服务把小窗视为关闭，不再操作该窗口。
    /// 窗口引用先于窗口关闭清空，关闭过程中重入的系统事件回调不会再操作这两个窗口
    /// </summary>
    private void ReleaseView()
    {
        if (_view is { } view)
        {
            view.Closed -= OnViewClosed;
        }

        if (_player is { } player)
        {
            player.StateChanged -= OnPlayerStateChanged;
            player.SubtitleChanged -= OnPlayerSubtitleChanged;
            player.EventRaised -= OnPlayerEventRaised;
        }

        _view = null;
        _player = null;
        _interaction = false;
        _boostActive = false;
        _pointerOver = false;
        _cursorInside = false;
        _pendingResume = null;
        _currentVideoKey = null;
        _progress = null;
        _progressTimer?.Stop();
        _interactionExitTimer?.Stop();
        _focusRestoreTimer?.Stop();
        var subtitleView = _subtitleView;
        _subtitleView = null;
        subtitleView?.CloseWindow();

        State.IsOpen = false;
        State.IsShown = false;
        State.IsInteraction = false;
        State.IsSubtitleAdjusting = false;
        State.HasVideo = false;
        State.IsPlaying = false;
        State.SubtitleText = string.Empty;
        State.NoticeText = string.Empty;
        State.Title = string.Empty;
        State.CurrentTime = 0;
        State.Duration = 0;
    }

    #endregion

    #region 播放控制

    /// <inheritdoc />
    public void TogglePlay() => RunOnUiAsync(async () =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        var wasPlaying = State.IsPlaying;
        var result = await ExecuteAsync(GuidePlayerCommand.Toggle);
        if (!result.Ok)
        {
            ShowFailure(result);
            return;
        }

        State.IsPlaying = !wasPlaying;
        State.ShowOsd(wasPlaying ? "已暂停" : "播放");
    });

    /// <inheritdoc />
    public void SeekBy(int direction) => RunOnUiAsync(async () =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        var seconds = Math.Clamp(Config.SeekSeconds, 1, 60);
        var result = await ExecuteAsync(GuidePlayerCommand.SeekBy, seconds * Math.Sign(direction));
        if (!result.Ok)
        {
            ShowFailure(result);
            return;
        }

        var target = State.CurrentTime + seconds * Math.Sign(direction);
        State.CurrentTime = State.Duration > 0 ? Math.Clamp(target, 0, State.Duration) : Math.Max(0, target);
        State.ShowOsd($"{(direction > 0 ? "快进" : "快退")} {seconds} 秒 · {State.TimeText}");
    });

    /// <inheritdoc />
    public void ChangeRate(int direction) => RunOnUiAsync(async () =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        // 临时加速期间手动调整倍速：以加速前的倍速为基准，并结束临时加速
        var current = _boostActive ? _boostPreviousRate : State.PlaybackRate;
        _boostActive = false;
        var step = Math.Clamp(Config.RateStep, 0.05, 1);
        var rate = Math.Round((current + step * Math.Sign(direction)) / step) * step;
        await SetRateCoreAsync(rate);
    });

    /// <inheritdoc />
    public void SetRate(double rate) => RunOnUiAsync(async () =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        _boostActive = false;
        await SetRateCoreAsync(rate);
    });

    /// <summary>
    /// 设置倍速并显示反馈
    /// </summary>
    private async Task SetRateCoreAsync(double rate)
    {
        rate = Math.Clamp(Math.Round(rate, 2), MinRate, MaxRate);
        var result = await ExecuteAsync(GuidePlayerCommand.SetRate, rate);
        if (!result.Ok)
        {
            ShowFailure(result);
            return;
        }

        State.PlaybackRate = rate;
        State.ShowOsd($"倍速 {rate:0.##}x");
    }

    /// <inheritdoc />
    public void BeginBoost() => RunOnUiAsync(async () =>
    {
        if (_boostActive || _player is null || !State.HasVideo || !State.IsShown && !State.IsPlaying)
        {
            return;
        }

        var previous = State.PlaybackRate;
        var boost = Math.Clamp(Config.BoostRate, 1.25, MaxRate);
        _boostActive = true;
        _boostPreviousRate = previous;
        _boostAppliedRate = boost;
        _boostStartedAt = Environment.TickCount64;
        var result = await ExecuteAsync(GuidePlayerCommand.SetRate, boost);
        if (!result.Ok)
        {
            _boostActive = false;
            ShowFailure(result);
            return;
        }

        State.PlaybackRate = boost;
        State.ShowOsd($"临时加速 {boost:0.##}x", 800);
    });

    /// <inheritdoc />
    public void EndBoost() => RunOnUiAsync(async () =>
    {
        if (!_boostActive)
        {
            return;
        }

        _boostActive = false;
        var previous = _boostPreviousRate;
        var result = await ExecuteAsync(GuidePlayerCommand.SetRate, previous);
        if (result.Ok)
        {
            State.PlaybackRate = previous;
            State.ShowOsd($"倍速 {previous:0.##}x", 800);
        }
    });

    /// <inheritdoc />
    public void ChangeVolume(int direction) => RunOnUiAsync(async () =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        var step = Math.Clamp(Config.VolumeStep, 0.01, 0.5);
        var volume = Math.Clamp(Math.Round((State.Volume + step * Math.Sign(direction)) * 100) / 100, 0, 1);
        var result = await ExecuteAsync(GuidePlayerCommand.SetVolume, volume);
        if (!result.Ok)
        {
            ShowFailure(result);
            return;
        }

        if (direction > 0 && _player!.IsMuted)
        {
            _player.IsMuted = false;
            State.IsMuted = false;
        }

        State.Volume = volume;
        State.ShowOsd($"音量 {Math.Round(volume * 100)}%");
    });

    /// <inheritdoc />
    public void ToggleMute() => RunOnUi(() =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        _player!.IsMuted = !_player.IsMuted;
        State.IsMuted = _player.IsMuted;
        State.ShowOsd(State.IsMuted ? "已静音" : "取消静音");
    });

    /// <inheritdoc />
    public void SwitchEpisode(int offset) => RunOnUiAsync(async () =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        var result = await ExecuteAsync(offset > 0 ? GuidePlayerCommand.NextEpisode : GuidePlayerCommand.PrevEpisode);
        if (!result.Ok)
        {
            ShowFailure(result);
            return;
        }

        EndBoost();
        State.ShowOsd(offset > 0 ? "下一集" : "上一集");
    });

    /// <inheritdoc />
    public void ToggleImmersive() => RunOnUi(() =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        SetImmersiveCore(!State.IsImmersive, showOsd: true);
    });

    /// <summary>
    /// 进入或退出沉浸模式；进入时按视频比例调整小窗高度
    /// </summary>
    private void SetImmersiveCore(bool immersive, bool showOsd)
    {
        State.IsImmersive = immersive;
        if (Config.RememberImmersive)
        {
            Config.Immersive = immersive;
        }

        _ = ExecuteAsync(GuidePlayerCommand.SetImmersive, immersive ? 1 : 0);
        if (immersive && Config.LockAspectInImmersive)
        {
            FitHeightToVideo();
        }

        if (showOsd)
        {
            State.ShowOsd(immersive ? "沉浸模式：开" : "沉浸模式：关");
        }
    }

    /// <summary>
    /// 按视频宽高比调整小窗高度，保持停靠角不动
    /// </summary>
    private void FitHeightToVideo()
    {
        if (_view is not { } view || !view.TryGetBounds(out var bounds) || State.AspectRatio <= 0)
        {
            return;
        }

        var height = (int)Math.Round(bounds.Width / State.AspectRatio);
        var minHeight = (int)Math.Round(MinHeightDip * SystemScale);
        height = Math.Max(height, minHeight);
        if (Math.Abs(height - bounds.Height) <= 1)
        {
            return;
        }

        var keepBottom = Config.PositionMode == GuidePositionMode.FollowGame && _watcher.IsAttached
                         && Config.FollowAnchor is GuideAnchor.BottomLeft or GuideAnchor.BottomRight;
        var top = keepBottom ? bounds.Bottom - height : bounds.Top;
        view.SetBounds(new RECT(bounds.Left, top, bounds.Right, top + height));
        CommitBoundsCore();
    }

    /// <inheritdoc />
    public void ToggleDanmaku() => RunOnUiAsync(async () =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        if (State.Danmaku is not { } danmaku)
        {
            State.ShowOsd("当前页面不支持弹幕开关");
            return;
        }

        var result = await ExecuteAsync(GuidePlayerCommand.SetDanmaku, danmaku ? 0 : 1);
        if (!result.Ok)
        {
            ShowFailure(result);
            return;
        }

        State.Danmaku = !danmaku;
        State.ShowOsd(danmaku ? "弹幕：关" : "弹幕：开");
    });

    #endregion

    #region 窗口与输入模式

    /// <inheritdoc />
    public void ToggleClickThrough() => RunOnUi(() =>
    {
        var enable = !Config.IsClickThrough;
        Config.IsClickThrough = enable;
        if (enable && _interaction)
        {
            EndInteractionCore(returnFocus: true);
        }

        var hotkey = HotkeyText(_allConfig.HotKeyConfig.GuideClickThroughHotkey);
        State.ShowOsd(enable
            ? $"鼠标穿透：开{(hotkey.Length > 0 ? $"（{hotkey} 关闭）" : "（可在托盘菜单恢复）")}"
            : "鼠标穿透：关", enable ? 2500 : 1200);
    });

    /// <inheritdoc />
    public void ToggleInteraction() => RunOnUi(() =>
    {
        if (_interaction)
        {
            EndInteractionCore(returnFocus: true);
            return;
        }

        if (BeginInteractionCore())
        {
            State.ShowOsd("交互模式：可以输入文字，鼠标移开后自动回到游戏", 2000);
        }
    });

    /// <inheritdoc />
    public void BeginInteraction() => RunOnUi(() => BeginInteractionCore());

    /// <inheritdoc />
    public void EndInteraction() => RunOnUi(() => EndInteractionCore(returnFocus: true));

    /// <summary>
    /// 进入交互模式：显示小窗、取消穿透并激活窗口
    /// </summary>
    /// <returns>已处于或成功进入交互模式时返回 true</returns>
    private bool BeginInteractionCore()
    {
        if (_view is not { } view)
        {
            return false;
        }

        if (_interaction)
        {
            return true;
        }

        if (!State.IsShown)
        {
            ShowCore();
        }

        _returnFocusAfterInteraction = _watcher.IsAttached && (_watcher.IsGameForeground || _watcher.LastExternalForegroundWasGame);
        _interaction = true;
        Recompute();
        view.ActivateForInput();
        // 用快捷键进入时鼠标可能还不在小窗上，多给一些时间移过来
        RestartInteractionExitTimer(_pointerOver ? 0 : Math.Max(5, Config.InteractionAutoExitSeconds));
        return true;
    }

    /// <summary>
    /// 结束交互模式，恢复原来的输入状态
    /// </summary>
    /// <param name="returnFocus">进入前游戏在前台时是否把焦点还给游戏</param>
    private void EndInteractionCore(bool returnFocus)
    {
        if (!_interaction)
        {
            return;
        }

        _interaction = false;
        _interactionExitTimer?.Stop();
        Recompute();
        if (returnFocus && _returnFocusAfterInteraction)
        {
            _watcher.FocusGame();
        }
    }

    /// <summary>
    /// 重新开始交互模式的自动结束计时；seconds 为 0 时停止计时
    /// </summary>
    private void RestartInteractionExitTimer(int seconds)
    {
        if (_interactionExitTimer is null)
        {
            return;
        }

        _interactionExitTimer.Stop();
        if (!_interaction || seconds <= 0 || Config.InteractionAutoExitSeconds <= 0)
        {
            return;
        }

        _interactionExitTimer.Interval = TimeSpan.FromSeconds(seconds);
        _interactionExitTimer.Start();
    }

    /// <inheritdoc />
    public void ToggleSubtitle() => RunOnUi(() =>
    {
        Config.SubtitleWindowEnabled = !Config.SubtitleWindowEnabled;
        State.ShowOsd(Config.SubtitleWindowEnabled
            ? State.HasSubtitle ? "字幕浮窗：开" : "字幕浮窗：开（当前视频没有字幕）"
            : "字幕浮窗：关");
    });

    /// <inheritdoc />
    public void ChangeOpacity(int direction) => RunOnUi(() =>
    {
        var opacity = Math.Clamp(Math.Round((Config.Opacity + 0.1 * Math.Sign(direction)) * 10) / 10,
            GuideWindowPresentation.MinOpacity, 1);
        Config.Opacity = opacity;
        State.ShowOsd(_pointerOver && !_interaction
            ? $"不透明度 {Math.Round(opacity * 100)}%（鼠标移开后生效）"
            : $"不透明度 {Math.Round(opacity * 100)}%");
    });

    /// <inheritdoc />
    public void ToggleTopmost() => RunOnUi(() =>
    {
        Config.Topmost = !Config.Topmost;
        State.ShowOsd(Config.Topmost ? "始终置顶：开" : "始终置顶：关");
    });

    /// <inheritdoc />
    public void SetPointerOver(bool over) => RunOnUi(() =>
    {
        if (_pointerOver == over)
        {
            return;
        }

        _pointerOver = over;
        if (_interaction)
        {
            RestartInteractionExitTimer(over ? 0 : Config.InteractionAutoExitSeconds);
        }

        Recompute();
    });

    /// <inheritdoc />
    public void SetCursorInside(bool inside) => RunOnUi(() =>
    {
        if (_cursorInside == inside)
        {
            return;
        }

        _cursorInside = inside;
        Recompute();
    });

    /// <inheritdoc />
    public void OnUnexpectedActivation() => RunOnUi(() =>
    {
        if (_interaction || !_watcher.IsAttached || !_watcher.LastExternalForegroundWasGame)
        {
            return;
        }

        _focusRestoreTimer?.Start();
    });

    /// <summary>
    /// 等鼠标按键全部松开后把焦点还给游戏，避免打断正在进行的点击或拖动
    /// </summary>
    private void OnFocusRestoreTick(object? sender, EventArgs e)
    {
        if (_interaction || _view is null)
        {
            _focusRestoreTimer?.Stop();
            return;
        }

        const int vkLButton = 0x01;
        const int vkRButton = 0x02;
        if ((User32.GetAsyncKeyState(vkLButton) & 0x8000) != 0 || (User32.GetAsyncKeyState(vkRButton) & 0x8000) != 0)
        {
            return;
        }

        _focusRestoreTimer?.Stop();
        if ((nint)User32.GetForegroundWindow() == _view.Handle)
        {
            _watcher.FocusGame();
        }
    }

    #endregion

    #region 位置

    /// <inheritdoc />
    public void CommitBounds() => RunOnUi(CommitBoundsCore);

    /// <inheritdoc />
    public void SetPositionMode(GuidePositionMode mode) => RunOnUi(() =>
    {
        if (Config.PositionMode == mode)
        {
            return;
        }

        Config.PositionMode = mode;
        CommitBoundsCore();
        State.ShowOsd(mode == GuidePositionMode.FollowGame ? "位置：跟随游戏窗口" : "位置：自由放置");
    });

    /// <inheritdoc />
    public void Dock(GuideAnchor anchor) => RunOnUi(() =>
    {
        if (_view is not { } view || !view.TryGetBounds(out var bounds))
        {
            return;
        }

        var margin = (int)Math.Round(DefaultMarginDip * SystemScale);
        if (Config.PositionMode == GuidePositionMode.FollowGame && _watcher.IsAttached)
        {
            Config.FollowAnchor = anchor;
            Config.FollowOffsetX = margin;
            Config.FollowOffsetY = margin;
        }
        else
        {
            var area = FormsScreen.FromHandle(view.Handle).WorkingArea;
            var (left, top) = PlaceAtCorner(anchor, area.Left, area.Top, area.Right, area.Bottom, bounds.Width, bounds.Height, margin, margin);
            Config.FreeLeft = left;
            Config.FreeTop = top;
        }

        ApplyPosition();
        CommitBoundsCore();
        State.ShowOsd($"已停靠到{AnchorName(anchor)}");
    });

    /// <inheritdoc />
    public void MoveToMonitor(int monitorIndex) => RunOnUi(() =>
    {
        var screens = FormsScreen.AllScreens;
        if (_view is not { } view || monitorIndex < 0 || monitorIndex >= screens.Length || !view.TryGetBounds(out var bounds))
        {
            return;
        }

        var area = screens[monitorIndex].WorkingArea;
        var margin = (int)Math.Round(DefaultMarginDip * SystemScale);
        var width = Math.Min(bounds.Width, area.Width);
        var height = Math.Min(bounds.Height, area.Height);
        Config.PositionMode = GuidePositionMode.Free;
        Config.FreeLeft = area.Right - margin - width;
        Config.FreeTop = area.Top + margin;
        Config.WindowWidth = width;
        Config.WindowHeight = height;
        ApplyPosition();
        State.ShowOsd($"已移到显示器 {monitorIndex + 1}");
    });

    /// <summary>
    /// 保存小窗当前的位置和尺寸：跟随模式记录停靠角与偏移，同时记录屏幕位置供没有游戏窗口时使用
    /// </summary>
    private void CommitBoundsCore()
    {
        if (_view is not { } view || !view.TryGetBounds(out var bounds))
        {
            return;
        }

        Config.WindowWidth = bounds.Width;
        Config.WindowHeight = bounds.Height;
        Config.FreeLeft = bounds.Left;
        Config.FreeTop = bounds.Top;
        if (Config.PositionMode != GuidePositionMode.FollowGame || !_watcher.TryGetGameRect(out var game))
        {
            return;
        }

        var centerX = (bounds.Left + bounds.Right) / 2;
        var centerY = (bounds.Top + bounds.Bottom) / 2;
        var left = centerX < (game.Left + game.Right) / 2;
        var top = centerY < (game.Top + game.Bottom) / 2;
        var anchor = (left, top) switch
        {
            (true, true) => GuideAnchor.TopLeft,
            (false, true) => GuideAnchor.TopRight,
            (true, false) => GuideAnchor.BottomLeft,
            _ => GuideAnchor.BottomRight,
        };
        Config.FollowAnchor = anchor;
        Config.FollowOffsetX = left ? bounds.Left - game.Left : game.Right - bounds.Right;
        Config.FollowOffsetY = top ? bounds.Top - game.Top : game.Bottom - bounds.Bottom;
    }

    /// <summary>
    /// 按配置把小窗放到应在的位置
    /// </summary>
    private void ApplyPosition()
    {
        _view?.SetBounds(ComputeBounds());
    }

    /// <summary>
    /// 按位置模式计算小窗的位置和尺寸（物理像素），并夹回可见的工作区
    /// </summary>
    private RECT ComputeBounds()
    {
        var scale = SystemScale;
        var minWidth = (int)Math.Round(MinWidthDip * scale);
        var minHeight = (int)Math.Round(MinHeightDip * scale);
        var width = Math.Max(Config.WindowWidth ?? (int)Math.Round(DefaultWidthDip * scale), minWidth);
        var height = Math.Max(Config.WindowHeight ?? (int)Math.Round(DefaultHeightDip * scale), minHeight);
        var margin = (int)Math.Round(DefaultMarginDip * scale);

        int left, top;
        if (Config.PositionMode == GuidePositionMode.FollowGame && _watcher.TryGetGameRect(out var game))
        {
            (left, top) = PlaceAtCorner(Config.FollowAnchor, game.Left, game.Top, game.Right, game.Bottom, width, height,
                Config.FollowOffsetX ?? margin, Config.FollowOffsetY ?? margin);
        }
        else if (Config.FreeLeft is { } freeLeft && Config.FreeTop is { } freeTop)
        {
            left = freeLeft;
            top = freeTop;
        }
        else
        {
            var area = FormsScreen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            left = area.Right - margin - width;
            top = area.Top + margin;
        }

        (left, top) = EnsureVisible(left, top, width, height);
        return new RECT(left, top, left + width, top + height);
    }

    /// <summary>
    /// 计算放在区域某个角时的左上角坐标
    /// </summary>
    private static (int Left, int Top) PlaceAtCorner(GuideAnchor anchor, int areaLeft, int areaTop, int areaRight, int areaBottom,
        int width, int height, int offsetX, int offsetY) => anchor switch
    {
        GuideAnchor.TopLeft => (areaLeft + offsetX, areaTop + offsetY),
        GuideAnchor.TopRight => (areaRight - offsetX - width, areaTop + offsetY),
        GuideAnchor.BottomLeft => (areaLeft + offsetX, areaBottom - offsetY - height),
        _ => (areaRight - offsetX - width, areaBottom - offsetY - height),
    };

    /// <summary>
    /// 小窗顶部至少有一块窗口栏大小的区域落在某个工作区内，否则夹到最近的工作区（规则同 WindowResizeBehavior）
    /// </summary>
    private static (int Left, int Top) EnsureVisible(int left, int top, int width, int height)
    {
        var screens = FormsScreen.AllScreens;
        if (screens.Length == 0)
        {
            return (left, top);
        }

        var visibleWidth = Math.Min(width, 120);
        var visibleHeight = Math.Min(height, 24);
        var titleBar = new System.Drawing.Rectangle(left, top, width, visibleHeight);
        if (screens.Any(screen => System.Drawing.Rectangle.Intersect(titleBar, screen.WorkingArea) is var hit
                                  && hit.Width >= visibleWidth && hit.Height >= visibleHeight))
        {
            return (left, top);
        }

        var area = screens
            .Select(screen => screen.WorkingArea)
            .OrderBy(work => DistanceSquared(left, top, work))
            .First();
        var correctedLeft = width <= area.Width ? Math.Clamp(left, area.Left, area.Right - width) : area.Left;
        var correctedTop = height <= area.Height ? Math.Clamp(top, area.Top, area.Bottom - height) : area.Top;
        return (correctedLeft, correctedTop);
    }

    /// <summary>
    /// 点到矩形的距离平方
    /// </summary>
    private static double DistanceSquared(int x, int y, System.Drawing.Rectangle rect)
    {
        var dx = x < rect.Left ? rect.Left - x : x > rect.Right ? x - rect.Right : 0d;
        var dy = y < rect.Top ? rect.Top - y : y > rect.Bottom ? y - rect.Bottom : 0d;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// 系统 DPI 缩放比例：界面单位与物理像素的换算
    /// </summary>
    private static double SystemScale
    {
        get
        {
            var scale = DpiHelper.GetScale().X;
            return scale > 0 ? scale : 1;
        }
    }

    /// <summary>
    /// 停靠角的中文名
    /// </summary>
    private static string AnchorName(GuideAnchor anchor) => anchor switch
    {
        GuideAnchor.TopLeft => "左上角",
        GuideAnchor.TopRight => "右上角",
        GuideAnchor.BottomLeft => "左下角",
        _ => "右下角",
    };

    #endregion

    #region 网页、收藏与剪贴板

    /// <inheritdoc />
    public void ToggleFavorite() => RunOnUi(() =>
    {
        if (!RequirePlayer())
        {
            return;
        }

        if (string.IsNullOrEmpty(_currentVideoKey))
        {
            State.ShowOsd("当前页面不是视频，无法收藏");
            return;
        }

        var url = GuideLinkResolver.Normalize(State.Url);
        var favorite = _library.ToggleFavorite(_currentVideoKey, State.Title, url);
        State.IsFavorite = favorite;
        State.ShowOsd(favorite ? "已收藏" : "已取消收藏");
    });

    /// <inheritdoc />
    public void OpenFromClipboard() => RunOnUi(() =>
    {
        string? text = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                text = Clipboard.GetText();
            }
        }
        catch (Exception e)
        {
            // 剪贴板被其他程序占用时读取会失败
            _logger.LogDebug(e, "读取剪贴板失败");
        }

        if (!GuideLinkResolver.TryResolve(text, allowAnyUrl: true, out var link))
        {
            if (State.IsShown)
            {
                State.ShowOsd("剪贴板中没有可打开的链接");
            }
            else
            {
                Toast.Information("剪贴板中没有可打开的链接");
            }

            return;
        }

        OpenCore(new GuideOpenRequest(link.Url, GuideOpenSource.Clipboard));
    });

    /// <inheritdoc />
    public void NavigateFromInput(string input) => RunOnUi(() =>
    {
        var url = GuideLinkResolver.FromAddressInput(input);
        if (url is null)
        {
            return;
        }

        OpenCore(new GuideOpenRequest(url, GuideOpenSource.Address));
    });

    /// <inheritdoc />
    public void GoBack() => RunOnUi(() =>
    {
        if (_player is { CanGoBack: true } player)
        {
            player.GoBack();
        }
    });

    /// <inheritdoc />
    public void Reload() => RunOnUi(() => _player?.Reload());

    /// <inheritdoc />
    public void ReloadAfterCrash() => RunOnUi(() =>
    {
        State.NoticeText = string.Empty;
        _player?.Reload();
    });

    /// <inheritdoc />
    public void SetZoom(double factor) => RunOnUi(() =>
    {
        Config.ZoomFactor = Math.Clamp(factor, 0.5, 2);
        State.ShowOsd($"网页缩放 {Math.Round(Config.ZoomFactor * 100)}%");
    });

    /// <summary>
    /// 打开网址：先保存当前视频的进度，记录续播与自动播放的处理方式
    /// </summary>
    private void NavigateCore(string url, GuideOpenSource source)
    {
        if (_player is null)
        {
            return;
        }

        SaveProgress();
        var now = Environment.TickCount64;
        _pendingResume = new PendingResume(source, now + ResumeWindowMs);
        _suppressAutoplayUntil = source == GuideOpenSource.AutoOpen ? now + AutoplaySuppressMs : 0;
        State.Url = url;
        State.NoticeText = string.Empty;
        _player.Navigate(url);
    }

    #endregion

    #region 字幕浮窗

    /// <inheritdoc />
    public void BeginSubtitleAdjust() => RunOnUi(() =>
    {
        if (!IsOpen)
        {
            Toast.Information("请先打开悬浮小窗");
            return;
        }

        State.IsSubtitleAdjusting = true;
        UpdateSubtitleWindow();
    });

    /// <inheritdoc />
    public void EndSubtitleAdjust() => RunOnUi(() =>
    {
        State.IsSubtitleAdjusting = false;
        UpdateSubtitleWindow();
    });

    /// <summary>
    /// 按开关、字幕文本与小窗状态显示或隐藏字幕浮窗
    /// </summary>
    private void UpdateSubtitleWindow()
    {
        var shouldShow = IsOpen && (State.IsSubtitleAdjusting
                                    || Config.SubtitleWindowEnabled && State.HasSubtitle && !_hiddenByTask && !_hiddenByGame
                                    && (State.IsShown || _hiddenByUser && Config.ShowSubtitleWhenHidden));
        if (shouldShow)
        {
            _subtitleView ??= _subtitleFactory();
            UpdateSubtitleAnchor();
            if (!_subtitleView.IsShown)
            {
                _subtitleView.ShowWithoutActivation();
                _subtitleView.BringToTopmost();
            }

            return;
        }

        if (_subtitleView is { IsShown: true } subtitleView)
        {
            subtitleView.HideWindow();
        }
    }

    /// <summary>
    /// 字幕浮窗的定位参照：游戏画面，没有游戏窗口时为主显示器工作区
    /// </summary>
    private void UpdateSubtitleAnchor()
    {
        if (_subtitleView is null)
        {
            return;
        }

        if (_watcher.TryGetGameRect(out var game))
        {
            _subtitleView.UpdateAnchor(game);
            return;
        }

        var area = FormsScreen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        _subtitleView.UpdateAnchor(new RECT(area.Left, area.Top, area.Right, area.Bottom));
    }

    #endregion

    #region 状态计算

    /// <summary>
    /// 汇总全部输入，计算并应用小窗的显示状态
    /// </summary>
    private void Recompute()
    {
        var presentation = GuideWindowPresentation.Compute(new GuidePresentationInput(
            IsOpen,
            _hiddenByUser,
            _hiddenByTask,
            _taskForceShown,
            _hiddenByGame && !_gameForceShown,
            _interaction,
            Config.IsClickThrough,
            _pointerOver,
            _cursorInside,
            Config.SmartFadeEnabled,
            Config.Opacity,
            Config.SmartFadeOpacity));

        var wasShown = State.IsShown;
        State.IsOpen = IsOpen;
        State.IsShown = presentation.Visible;
        State.IsClickThrough = presentation.ClickThrough;
        State.IsActivatable = presentation.Activatable;
        State.Opacity = presentation.Opacity;
        State.IsBarAllowed = presentation.BarAllowed;
        State.IsInteraction = _interaction;
        State.IsTopmost = Config.Topmost;
        State.IsHiddenByUser = _hiddenByUser;
        State.IsHiddenByTask = _hiddenByTask && !_taskForceShown;
        State.IsHiddenByGame = _hiddenByGame && !_gameForceShown;

        if (_view is { } view)
        {
            if (presentation.Visible && !view.IsShown)
            {
                ApplyPosition();
                view.ShowWithoutActivation();
                view.BringToTopmost();
            }
            else if (!presentation.Visible && view.IsShown)
            {
                view.HideWindow();
            }
        }

        if (wasShown && !presentation.Visible)
        {
            OnBecameHidden();
        }

        UpdateSubtitleWindow();
    }

    /// <summary>
    /// 小窗从显示变为隐藏：结束临时状态，保存进度，按设置暂停
    /// </summary>
    private void OnBecameHidden()
    {
        _pointerOver = false;
        _cursorInside = false;
        EndBoost();
        if (_interaction)
        {
            EndInteractionCore(returnFocus: false);
        }

        SaveProgress();
        _library.FlushInBackground();
        if (Config.PauseWhenHidden && !State.IsHiddenByTask && State.IsPlaying)
        {
            _ = ExecuteAsync(GuidePlayerCommand.Pause);
        }
    }

    /// <summary>
    /// 配置变化：重新计算显示状态，或把设置同步给网页
    /// </summary>
    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        RunOnUi(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(GuideWindowConfig.Opacity):
                case nameof(GuideWindowConfig.IsClickThrough):
                case nameof(GuideWindowConfig.Topmost):
                case nameof(GuideWindowConfig.SmartFadeEnabled):
                case nameof(GuideWindowConfig.SmartFadeOpacity):
                    Recompute();
                    if (e.PropertyName == nameof(GuideWindowConfig.Topmost) && Config.Topmost)
                    {
                        _view?.BringToTopmost();
                    }

                    break;
                case nameof(GuideWindowConfig.HideWhenGameInactive):
                case nameof(GuideWindowConfig.KeepVisibleOnOtherMonitor):
                    EvaluateGameState();
                    break;
                case nameof(GuideWindowConfig.ZoomFactor):
                    _player?.SetZoom(Config.ZoomFactor);
                    break;
                case nameof(GuideWindowConfig.SubtitleWindowEnabled):
                case nameof(GuideWindowConfig.HidePageSubtitle):
                    _ = ExecuteAsync(GuidePlayerCommand.SetPageSubtitleHidden,
                        Config.SubtitleWindowEnabled && Config.HidePageSubtitle ? 1 : 0);
                    UpdateSubtitleWindow();
                    break;
                case nameof(GuideWindowConfig.ShowSubtitleWhenHidden):
                    UpdateSubtitleWindow();
                    break;
                case nameof(GuideWindowConfig.AutoEnableChineseSubtitle):
                    _ = ExecuteAsync(GuidePlayerCommand.SetAutoSubtitle, Config.AutoEnableChineseSubtitle ? 1 : 0);
                    break;
                case nameof(GuideWindowConfig.PositionMode):
                    ApplyPosition();
                    break;
                case nameof(GuideWindowConfig.RememberImmersive):
                    Config.Immersive = Config.RememberImmersive && State.IsImmersive;
                    break;
            }
        });
    }

    #endregion

    #region 游戏与任务联动

    /// <summary>
    /// 截图器绑定游戏窗口：挂接监听，按设置自动打开
    /// </summary>
    private void OnRuntimeStarted(object? sender, EventArgs e)
    {
        var runtime = _runtimeService.Current;
        if (runtime is null || runtime.Kind != GameRuntimeKind.Win32Window)
        {
            return;
        }

        _watcher.Attach(runtime.Window);
        EvaluateGameState();
        if (IsOpen)
        {
            ApplyPosition();
            return;
        }

        var autoOpen = Config.AutoOpenOnGameStart switch
        {
            GuideAutoOpenMode.Always => true,
            GuideAutoOpenMode.RestoreLast => Config.WasOpen,
            _ => false,
        };
        if (autoOpen)
        {
            OpenCore(new GuideOpenRequest(null, GuideOpenSource.AutoOpen));
            _suppressAutoplayUntil = Environment.TickCount64 + AutoplaySuppressMs;
        }
    }

    /// <summary>
    /// 截图器解绑：解除监听，不再按游戏状态隐藏
    /// </summary>
    private void OnRuntimeStopped(object? sender, EventArgs e)
    {
        _watcher.Detach();
        _hiddenByGame = false;
        _gameForceShown = false;
        Recompute();
    }

    /// <summary>
    /// 游戏前台或最小化状态变化
    /// </summary>
    private void OnGameStateChanged(object? sender, EventArgs e)
    {
        var gameForeground = _watcher.IsGameForeground;
        if (gameForeground && _interaction)
        {
            // 用户点回了游戏：结束交互模式，焦点已经在游戏上
            EndInteractionCore(returnFocus: false);
        }

        if (!gameForeground)
        {
            // 键鼠监听只在游戏前台时报告松开，切走后结束临时加速，避免一直加速
            EndBoost();
        }

        _gameForceShown = false;
        EvaluateGameState();
        if (gameForeground && State.IsShown)
        {
            _view?.BringToTopmost();
            _subtitleView?.BringToTopmost();
        }
    }

    /// <summary>
    /// 按游戏窗口状态决定是否自动隐藏（见设计文档 9.1 节）
    /// </summary>
    private void EvaluateGameState()
    {
        bool hidden;
        if (!_watcher.IsAttached || !Config.HideWhenGameInactive)
        {
            hidden = false;
        }
        else if (_watcher.IsGameMinimized)
        {
            hidden = true;
        }
        else if (_watcher.IsGameForeground)
        {
            hidden = false;
        }
        else if (GuideGameWatcher.IsForegroundOwnedByBetterGi())
        {
            // 前台是 BetterGI 自身（主界面、交互模式下的小窗）时保持不变
            hidden = _hiddenByGame;
        }
        else if (Config.KeepVisibleOnOtherMonitor && _view is { } view
                                                   && !GuideGameWatcher.IsOnSameMonitor(view.Handle, _watcher.GameHandle))
        {
            hidden = false;
        }
        else
        {
            hidden = true;
        }

        _hiddenByGame = hidden;
        Recompute();
    }

    /// <summary>
    /// 游戏窗口移动或缩放：跟随模式下同步位置，字幕浮窗同步参照区域
    /// </summary>
    private void OnGameMoved(object? sender, EventArgs e)
    {
        if (IsOpen && Config.PositionMode == GuidePositionMode.FollowGame && State.IsShown)
        {
            ApplyPosition();
        }

        UpdateSubtitleAnchor();
    }

    /// <summary>
    /// 遮罩显示或移动后会被放到最上层，之后重新置顶小窗和字幕浮窗
    /// </summary>
    private void OnMaskWindowStateChanged(object? sender, MaskWindowState e)
    {
        if (!State.IsShown || !Config.Topmost)
        {
            return;
        }

        _view?.BringToTopmost();
        _subtitleView?.BringToTopmost();
    }

    /// <summary>
    /// 任务模式变化（在任务线程上触发）：开始时暂停并隐藏，结束后按设置恢复
    /// </summary>
    private void OnTaskModeChanged(object? sender, bool inTask)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        // 以最高优先级投递，在任务的第一次点击之前隐藏
        _ = dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => ApplyTaskMode(inTask)));
    }

    /// <summary>
    /// 在界面线程应用任务模式变化
    /// </summary>
    private void ApplyTaskMode(bool inTask)
    {
        if (inTask)
        {
            EndBoost();
            if (_interaction)
            {
                EndInteractionCore(returnFocus: false);
            }

            if (Config.PauseDuringTask && State.IsPlaying)
            {
                _ = ExecuteAsync(GuidePlayerCommand.Pause);
            }

            if (Config.HideDuringTask)
            {
                _hiddenByTask = true;
                _taskForceShown = false;
            }
        }
        else
        {
            if (_hiddenByTask && !Config.RestoreAfterTask && !_taskForceShown)
            {
                _hiddenByUser = true;
            }

            _hiddenByTask = false;
            _taskForceShown = false;
        }

        Recompute();
    }

    #endregion

    #region 网页事件

    /// <summary>
    /// 网页上报播放状态
    /// </summary>
    private void OnPlayerStateChanged(object? sender, GuidePlayerState state)
    {
        State.Title = state.Title;
        if (!string.IsNullOrEmpty(state.Href))
        {
            State.Url = state.Href;
            if (state.Href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                _library.SetLastUrl(state.Href);
            }
        }

        State.HasVideo = state.HasVideo;
        State.IsPlaying = state.HasVideo && !state.Paused;
        State.Duration = state.Duration;
        State.CurrentTime = state.CurrentTime;
        State.PlaybackRate = state.PlaybackRate;
        State.Volume = state.Volume;
        State.IsMuted = _player?.IsMuted ?? false;
        State.CanGoBack = _player?.CanGoBack ?? false;
        State.HasPrevEpisode = state.HasPrevEpisode;
        State.HasNextEpisode = state.HasNextEpisode;
        State.EpisodeText = state.EpisodeCount > 1 && state.EpisodeIndex > 0 ? $"P{state.EpisodeIndex}/{state.EpisodeCount}" : string.Empty;
        State.Danmaku = state.Danmaku;
        State.AspectRatio = state.VideoWidth > 0 && state.VideoHeight > 0 ? state.VideoWidth / state.VideoHeight : 16d / 9d;

        if (!string.Equals(state.VideoKey, _currentVideoKey, StringComparison.Ordinal))
        {
            OnVideoKeyChanged(state.VideoKey);
        }

        State.IsFavorite = _library.IsFavorite(_currentVideoKey);

        // 临时加速期间用户在网页里改了倍速：不再恢复，尊重用户的新倍速
        if (_boostActive && Environment.TickCount64 - _boostStartedAt > 800
                         && Math.Abs(state.PlaybackRate - _boostAppliedRate) > 0.01)
        {
            _boostActive = false;
        }

        // 自动打开时网页第一次自动播放暂停一次，避免进入游戏时突然出声
        if (state.HasVideo && !state.Paused && _suppressAutoplayUntil > 0)
        {
            if (Environment.TickCount64 < _suppressAutoplayUntil)
            {
                _ = ExecuteAsync(GuidePlayerCommand.Pause);
            }

            _suppressAutoplayUntil = 0;
        }

        TryResume(state);

        if (state.HasVideo && _currentVideoKey is { } key && state.Duration > 0)
        {
            _progress = new ProgressSnapshot(key, state.Title, GuideLinkResolver.Normalize(state.Href), state.CurrentTime,
                state.Duration, _boostActive ? _boostPreviousRate : state.PlaybackRate);
        }

        if (state.Paused && !_lastPaused)
        {
            SaveProgress();
            _library.FlushInBackground();
        }

        _lastPaused = state.Paused;
    }

    /// <summary>
    /// 视频切换：保存上一个视频的进度，结束临时加速，“所有视频”续播模式下准备续播
    /// </summary>
    private void OnVideoKeyChanged(string? videoKey)
    {
        SaveProgress();
        _progress = null;
        _currentVideoKey = videoKey;
        _boostActive = false;
        State.SubtitleText = string.Empty;
        if (_pendingResume is null && videoKey is not null && Config.ResumeMode == GuideResumeMode.Always)
        {
            _pendingResume = new PendingResume(GuideOpenSource.User, Environment.TickCount64 + ResumeWindowMs);
        }

        UpdateSubtitleWindow();
    }

    /// <summary>
    /// 视频加载后按续播设置跳到上次观看的位置（见设计文档 10.3 节）
    /// </summary>
    private void TryResume(GuidePlayerState state)
    {
        if (_pendingResume is not { } pending || state.VideoKey is null || !state.HasVideo || state.Duration <= 0)
        {
            return;
        }

        _pendingResume = null;
        if (Environment.TickCount64 > pending.Until)
        {
            return;
        }

        var allowed = Config.ResumeMode switch
        {
            GuideResumeMode.Always => true,
            GuideResumeMode.FromLibrary => pending.Source is GuideOpenSource.Favorite or GuideOpenSource.History,
            _ => false,
        };
        var record = allowed ? _library.FindRecord(state.VideoKey) : null;
        if (record is null || record.Finished || record.Position < 10 || state.CurrentTime > SiteResumedThreshold
            || record.Position >= state.Duration - 5)
        {
            return;
        }

        _ = ExecuteAsync(GuidePlayerCommand.SeekTo, record.Position);
        if (Config.RestorePlaybackRate && Math.Abs(record.PlaybackRate - state.PlaybackRate) > 0.01)
        {
            _ = ExecuteAsync(GuidePlayerCommand.SetRate, Math.Clamp(record.PlaybackRate, MinRate, MaxRate));
        }

        State.ShowOsd($"已从 {GuideWindowState.FormatTime(record.Position)} 继续", 2500);
    }

    /// <summary>
    /// 网页字幕变化
    /// </summary>
    private void OnPlayerSubtitleChanged(object? sender, string text)
    {
        State.SubtitleText = text;
        UpdateSubtitleWindow();
    }

    /// <summary>
    /// 网页事件
    /// </summary>
    private void OnPlayerEventRaised(object? sender, GuidePlayerEvent e)
    {
        switch (e.Kind)
        {
            case GuidePlayerEventKind.Ready:
                // 每个新文档都要重新告诉页面当前的设置
                State.NoticeText = string.Empty;
                _ = ExecuteAsync(GuidePlayerCommand.SetAutoSubtitle, Config.AutoEnableChineseSubtitle ? 1 : 0);
                _ = ExecuteAsync(GuidePlayerCommand.SetPageSubtitleHidden, Config.SubtitleWindowEnabled && Config.HidePageSubtitle ? 1 : 0);
                if (State.IsImmersive)
                {
                    _ = ExecuteAsync(GuidePlayerCommand.SetImmersive, 1);
                }

                break;
            case GuidePlayerEventKind.Ended:
                if (_progress is { } progress)
                {
                    _library.UpdateProgress(progress.VideoKey, progress.Title, progress.Url, progress.Duration,
                        progress.Duration, progress.Rate, ended: true);
                    _library.FlushInBackground();
                }

                break;
            case GuidePlayerEventKind.PlayRejected:
                State.ShowOsd("网页拒绝播放，请点击一次视频", 2000);
                break;
            case GuidePlayerEventKind.EditFocus:
                BeginInteractionCore();
                break;
            case GuidePlayerEventKind.ImmersiveExited:
                State.IsImmersive = false;
                if (Config.RememberImmersive)
                {
                    Config.Immersive = false;
                }

                break;
            case GuidePlayerEventKind.Crashed:
                State.CanReloadNotice = true;
                State.NoticeText = "网页进程异常退出";
                break;
            case GuidePlayerEventKind.BrowserExited:
                State.CanReloadNotice = false;
                State.NoticeText = "网页进程已退出，请关闭小窗后重新打开";
                break;
            case GuidePlayerEventKind.InitializeFailed:
                State.CanReloadNotice = false;
                State.NoticeText = $"网页初始化失败：{e.Message}";
                break;
        }
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 把当前视频的进度写入观看记录（内存），由定时器和各个时机调用
    /// </summary>
    private void SaveProgress()
    {
        if (_progress is not { } progress)
        {
            return;
        }

        _library.UpdateProgress(progress.VideoKey, progress.Title, progress.Url, progress.Position, progress.Duration, progress.Rate);
        if (State.IsPlaying)
        {
            _library.FlushInBackground();
        }
    }

    /// <summary>
    /// 播放命令需要小窗已打开
    /// </summary>
    private bool RequirePlayer() => _player is not null;

    /// <summary>
    /// 执行播放命令，异常转为失败结果
    /// </summary>
    private async Task<GuideCommandResult> ExecuteAsync(GuidePlayerCommand command, double value = 0)
    {
        if (_player is not { } player)
        {
            return GuideCommandResult.NotReady;
        }

        try
        {
            return await player.ExecuteAsync(command, value);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "执行小窗播放命令 {Command} 失败", command);
            return new GuideCommandResult(false, e.Message);
        }
    }

    /// <summary>
    /// 命令失败时在小窗中提示原因
    /// </summary>
    private void ShowFailure(GuideCommandResult result)
    {
        State.ShowOsd(result.Reason switch
        {
            "noVideo" => "当前页面没有视频",
            "noNext" => "没有下一集",
            "noPrev" => "没有上一集",
            "unsupported" => "当前页面不支持该操作",
            "notReady" => "网页还在加载",
            _ => "操作失败",
        });
    }

    /// <summary>
    /// 快捷键配置值转为显示文本，例如“Ctrl+Alt+P”；未绑定时为空
    /// </summary>
    private static string HotkeyText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace(" ", string.Empty);

    /// <summary>
    /// 在界面线程执行
    /// </summary>
    private void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            Invoke(action);
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(() => Invoke(action)));
    }

    /// <summary>
    /// 在界面线程执行异步操作，异常只记录日志
    /// </summary>
    private void RunOnUiAsync(Func<Task> action)
    {
        RunOnUi(() => _ = InvokeAsync(action));
    }

    /// <summary>
    /// 执行操作，异常只记录日志，不影响调用方（快捷键回调、系统事件）
    /// </summary>
    private void Invoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "悬浮小窗操作失败");
        }
    }

    /// <summary>
    /// 执行异步操作，异常只记录日志
    /// </summary>
    private async Task InvokeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "悬浮小窗操作失败");
        }
    }

    #endregion
}
