using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 小窗的当前状态，由 <see cref="GuideWindowService"/> 在界面线程写入，小窗、字幕浮窗和设置页绑定它
/// </summary>
public partial class GuideWindowState : ObservableObject
{
    /// <summary>
    /// 小窗是否打开（网页已创建）
    /// </summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>
    /// 小窗当前是否显示在屏幕上
    /// </summary>
    [ObservableProperty]
    private bool _isShown;

    /// <summary>
    /// 当前是否鼠标穿透（综合用户设置、场景与交互模式后的结果）
    /// </summary>
    [ObservableProperty]
    private bool _isClickThrough;

    /// <summary>
    /// 当前是否允许获得焦点，只有交互模式下为 true
    /// </summary>
    [ObservableProperty]
    private bool _isActivatable;

    /// <summary>
    /// 当前不透明度
    /// </summary>
    [ObservableProperty]
    private double _opacity = 1;

    /// <summary>
    /// 当前是否允许显示窗口栏（悬停或交互模式）
    /// </summary>
    [ObservableProperty]
    private bool _isBarAllowed;

    /// <summary>
    /// 是否处于交互模式
    /// </summary>
    [ObservableProperty]
    private bool _isInteraction;

    /// <summary>
    /// 是否处于沉浸模式
    /// </summary>
    [ObservableProperty]
    private bool _isImmersive;

    /// <summary>
    /// 是否始终置顶
    /// </summary>
    [ObservableProperty]
    private bool _isTopmost = true;

    /// <summary>
    /// 被用户隐藏
    /// </summary>
    [ObservableProperty]
    private bool _isHiddenByUser;

    /// <summary>
    /// 因任务运行被自动隐藏
    /// </summary>
    [ObservableProperty]
    private bool _isHiddenByTask;

    /// <summary>
    /// 因游戏最小化或切到后台被自动隐藏
    /// </summary>
    [ObservableProperty]
    private bool _isHiddenByGame;

    /// <summary>
    /// 网页标题
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// 网页地址
    /// </summary>
    [ObservableProperty]
    private string _url = string.Empty;

    /// <summary>
    /// 当前页面是否有视频
    /// </summary>
    [ObservableProperty]
    private bool _hasVideo;

    /// <summary>
    /// 当前视频是否已收藏
    /// </summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// 是否正在播放
    /// </summary>
    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>
    /// 当前位置（秒）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Progress))]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    private double _currentTime;

    /// <summary>
    /// 时长（秒）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Progress))]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    [NotifyPropertyChangedFor(nameof(TimeText))]
    private double _duration;

    /// <summary>
    /// 倍速
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RateText))]
    private double _playbackRate = 1;

    /// <summary>
    /// 音量，0–1
    /// </summary>
    [ObservableProperty]
    private double _volume = 1;

    /// <summary>
    /// 网页是否静音
    /// </summary>
    [ObservableProperty]
    private bool _isMuted;

    /// <summary>
    /// 是否可以后退
    /// </summary>
    [ObservableProperty]
    private bool _canGoBack;

    /// <summary>
    /// 是否有上一集
    /// </summary>
    [ObservableProperty]
    private bool _hasPrevEpisode;

    /// <summary>
    /// 是否有下一集
    /// </summary>
    [ObservableProperty]
    private bool _hasNextEpisode;

    /// <summary>
    /// 分集文字，例如“P3/12”，没有分集时为空
    /// </summary>
    [ObservableProperty]
    private string _episodeText = string.Empty;

    /// <summary>
    /// 弹幕是否开启，站点不支持时为 null
    /// </summary>
    [ObservableProperty]
    private bool? _danmaku;

    /// <summary>
    /// 视频宽高比，没有视频时为 16:9
    /// </summary>
    [ObservableProperty]
    private double _aspectRatio = 16d / 9d;

    /// <summary>
    /// 当前字幕文本
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtitle))]
    private string _subtitleText = string.Empty;

    /// <summary>
    /// 字幕浮窗是否处于调整位置模式
    /// </summary>
    [ObservableProperty]
    private bool _isSubtitleAdjusting;

    /// <summary>
    /// 热键反馈文本
    /// </summary>
    [ObservableProperty]
    private string _osdText = string.Empty;

    /// <summary>
    /// 热键反馈序号，每次显示反馈时加一，界面据此重新开始计时
    /// </summary>
    [ObservableProperty]
    private int _osdSerial;

    /// <summary>
    /// 热键反馈显示时长（毫秒）
    /// </summary>
    [ObservableProperty]
    private int _osdDurationMs = 1200;

    /// <summary>
    /// 网页异常说明，为空时不显示
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _noticeText = string.Empty;

    /// <summary>
    /// 网页异常说明下是否提供“重新加载”
    /// </summary>
    [ObservableProperty]
    private bool _canReloadNotice;

    /// <summary>
    /// 播放进度，0–1
    /// </summary>
    public double Progress => Duration > 0 ? Math.Clamp(CurrentTime / Duration, 0, 1) : 0;

    /// <summary>
    /// 是否有可显示的进度（时长已知）
    /// </summary>
    public bool HasProgress => Duration > 0;

    /// <summary>
    /// 倍速文字，例如“1.25x”
    /// </summary>
    public string RateText => $"{PlaybackRate:0.##}x";

    /// <summary>
    /// 时间文字，例如“12:35 / 20:10”
    /// </summary>
    public string TimeText => Duration > 0 ? $"{FormatTime(CurrentTime)} / {FormatTime(Duration)}" : FormatTime(CurrentTime);

    /// <summary>
    /// 是否有字幕文本
    /// </summary>
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(SubtitleText);

    /// <summary>
    /// 是否显示网页异常说明
    /// </summary>
    public bool HasNotice => !string.IsNullOrEmpty(NoticeText);

    /// <summary>
    /// 显示一条热键反馈
    /// </summary>
    /// <param name="text">反馈文本</param>
    /// <param name="durationMs">显示时长（毫秒）</param>
    public void ShowOsd(string text, int durationMs = 1200)
    {
        OsdDurationMs = durationMs;
        OsdText = text;
        OsdSerial++;
    }

    /// <summary>
    /// 秒数格式化为 m:ss 或 h:mm:ss
    /// </summary>
    public static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(Math.Floor(seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}
