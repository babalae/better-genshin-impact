using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 悬浮小窗配置：偏好与窗口几何。播放进度、收藏、历史保存在 User/GuideWindow 下的独立文件中，不放在这里
/// （见 Docs/design/guide-window.md 12.2 节）
/// </summary>
[Serializable]
public partial class GuideWindowConfig : ObservableObject
{
    /// <summary>
    /// 默认主页，B 站首页
    /// </summary>
    public const string DefaultHomeUrl = "https://www.bilibili.com/";

    /// <summary>
    /// 主页：没有上次页面时打开
    /// </summary>
    [ObservableProperty]
    private string _homeUrl = DefaultHomeUrl;

    /// <summary>
    /// 小窗当前是否打开，供“进入游戏时恢复上次状态”使用。在打开、关闭小窗时写入
    /// </summary>
    [ObservableProperty]
    private bool _wasOpen;

    /// <summary>
    /// 进入游戏（截图器绑定游戏窗口）时是否自动打开小窗
    /// </summary>
    [ObservableProperty]
    private GuideAutoOpenMode _autoOpenOnGameStart = GuideAutoOpenMode.RestoreLast;

    /// <summary>
    /// 游戏最小化或切到其他程序时自动隐藏
    /// </summary>
    [ObservableProperty]
    private bool _hideWhenGameInactive = true;

    /// <summary>
    /// 小窗与游戏不在同一显示器时，游戏切到后台不隐藏（游戏最小化时仍隐藏）
    /// </summary>
    [ObservableProperty]
    private bool _keepVisibleOnOtherMonitor = true;

    /// <summary>
    /// 小窗隐藏（用户隐藏或自动隐藏）时暂停视频；任务导致的隐藏由 <see cref="PauseDuringTask"/> 决定
    /// </summary>
    [ObservableProperty]
    private bool _pauseWhenHidden;

    /// <summary>
    /// 执行独立任务、调度器、一条龙等任务时隐藏小窗
    /// </summary>
    [ObservableProperty]
    private bool _hideDuringTask = true;

    /// <summary>
    /// 任务开始时暂停视频
    /// </summary>
    [ObservableProperty]
    private bool _pauseDuringTask = true;

    /// <summary>
    /// 任务结束后恢复显示小窗（不会自动继续播放）
    /// </summary>
    [ObservableProperty]
    private bool _restoreAfterTask = true;

    /// <summary>
    /// 鼠标不在小窗上时的不透明度，0.2–1.0
    /// </summary>
    [ObservableProperty]
    private double _opacity = 0.85;

    /// <summary>
    /// 鼠标穿透：小窗不接收鼠标，点击落到下方的游戏
    /// </summary>
    [ObservableProperty]
    private bool _isClickThrough;

    /// <summary>
    /// 交互模式下鼠标离开小窗多少秒后自动结束；0 表示不自动结束
    /// </summary>
    [ObservableProperty]
    private int _interactionAutoExitSeconds = 2;

    /// <summary>
    /// 始终置顶
    /// </summary>
    [ObservableProperty]
    private bool _topmost = true;

    /// <summary>
    /// 位置模式：跟随游戏窗口或自由放置
    /// </summary>
    [ObservableProperty]
    private GuidePositionMode _positionMode = GuidePositionMode.FollowGame;

    /// <summary>
    /// 跟随游戏时停靠的游戏客户区角
    /// </summary>
    [ObservableProperty]
    private GuideAnchor _followAnchor = GuideAnchor.TopRight;

    /// <summary>
    /// 跟随游戏时小窗与停靠角的水平距离（物理像素），为空时使用默认边距
    /// </summary>
    [ObservableProperty]
    private int? _followOffsetX;

    /// <summary>
    /// 跟随游戏时小窗与停靠角的垂直距离（物理像素），为空时使用默认边距
    /// </summary>
    [ObservableProperty]
    private int? _followOffsetY;

    /// <summary>
    /// 自由模式下小窗左上角的屏幕横坐标（物理像素）
    /// </summary>
    [ObservableProperty]
    private int? _freeLeft;

    /// <summary>
    /// 自由模式下小窗左上角的屏幕纵坐标（物理像素）
    /// </summary>
    [ObservableProperty]
    private int? _freeTop;

    /// <summary>
    /// 小窗宽度（物理像素），为空时按 480 DIP 换算
    /// </summary>
    [ObservableProperty]
    private int? _windowWidth;

    /// <summary>
    /// 小窗高度（物理像素），为空时按 270 DIP 换算
    /// </summary>
    [ObservableProperty]
    private int? _windowHeight;

    /// <summary>
    /// 当前是否处于沉浸模式（B 站网页全屏）
    /// </summary>
    [ObservableProperty]
    private bool _immersive;

    /// <summary>
    /// 记住沉浸模式，下次打开视频时沿用
    /// </summary>
    [ObservableProperty]
    private bool _rememberImmersive = true;

    /// <summary>
    /// 沉浸模式下按视频比例缩放小窗
    /// </summary>
    [ObservableProperty]
    private bool _lockAspectInImmersive = true;

    /// <summary>
    /// 窗口栏隐藏时在小窗底边显示播放进度线
    /// </summary>
    [ObservableProperty]
    private bool _showProgressLine = true;

    /// <summary>
    /// 网页缩放比例
    /// </summary>
    [ObservableProperty]
    private double _zoomFactor = 1.0;

    /// <summary>
    /// 快进、快退的秒数，1–60
    /// </summary>
    [ObservableProperty]
    private int _seekSeconds = 5;

    /// <summary>
    /// 倍速每次调整的步长
    /// </summary>
    [ObservableProperty]
    private double _rateStep = 0.25;

    /// <summary>
    /// 按住临时加速的倍速，1.25–4.0
    /// </summary>
    [ObservableProperty]
    private double _boostRate = 2.0;

    /// <summary>
    /// 音量每次调整的步长（0–1）
    /// </summary>
    [ObservableProperty]
    private double _volumeStep = 0.1;

    /// <summary>
    /// 何时跳到上次观看的位置
    /// </summary>
    [ObservableProperty]
    private GuideResumeMode _resumeMode = GuideResumeMode.FromLibrary;

    /// <summary>
    /// 续播时同时恢复记录的倍速
    /// </summary>
    [ObservableProperty]
    private bool _restorePlaybackRate = true;

    /// <summary>
    /// 打开 B 站视频时自动开启中文字幕
    /// </summary>
    [ObservableProperty]
    private bool _autoEnableChineseSubtitle = true;

    /// <summary>
    /// 字幕浮窗开关
    /// </summary>
    [ObservableProperty]
    private bool _subtitleWindowEnabled;

    /// <summary>
    /// 字幕浮窗字号，12–48
    /// </summary>
    [ObservableProperty]
    private double _subtitleFontSize = 22;

    /// <summary>
    /// 字幕浮窗背景不透明度，0–0.8
    /// </summary>
    [ObservableProperty]
    private double _subtitleBackgroundOpacity = 0.4;

    /// <summary>
    /// 开启字幕浮窗时隐藏网页内的字幕
    /// </summary>
    [ObservableProperty]
    private bool _hidePageSubtitle = true;

    /// <summary>
    /// 小窗被用户隐藏时继续显示字幕（只听声音、看字幕）
    /// </summary>
    [ObservableProperty]
    private bool _showSubtitleWhenHidden = true;

    /// <summary>
    /// 字幕浮窗中心相对游戏画面宽度的位置，0–1
    /// </summary>
    [ObservableProperty]
    private double _subtitleCenterXRatio = 0.5;

    /// <summary>
    /// 字幕浮窗底边距游戏画面底部的距离，占游戏画面高度的比例，0–1
    /// </summary>
    [ObservableProperty]
    private double _subtitleBottomRatio = 0.22;

    /// <summary>
    /// 字幕浮窗最大宽度，占游戏画面宽度的比例，0.2–1
    /// </summary>
    [ObservableProperty]
    private double _subtitleWidthRatio = 0.6;

    /// <summary>
    /// 智能透明：鼠标穿透时，鼠标经过小窗自动淡化，露出下方的游戏画面
    /// </summary>
    [ObservableProperty]
    private bool _smartFadeEnabled = true;

    /// <summary>
    /// 智能透明淡化后的不透明度，0.1–0.8
    /// </summary>
    [ObservableProperty]
    private double _smartFadeOpacity = 0.25;

    /// <summary>
    /// 地图点位中的视频链接默认用小窗打开
    /// </summary>
    [ObservableProperty]
    private bool _openMapVideoInWindow = true;

    /// <summary>
    /// 主界面激活时识别剪贴板中的 B 站链接并询问是否用小窗打开
    /// </summary>
    [ObservableProperty]
    private bool _clipboardPromptEnabled = true;

    /// <summary>
    /// 是否已经显示过“按住 Alt 操作小窗”的提示
    /// </summary>
    [ObservableProperty]
    private bool _altHintShown;
}

/// <summary>
/// 进入游戏时自动打开小窗的方式
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuideAutoOpenMode>))]
public enum GuideAutoOpenMode
{
    /// <summary>不自动打开</summary>
    Never,

    /// <summary>上次退出时小窗是打开的才打开</summary>
    RestoreLast,

    /// <summary>总是打开</summary>
    Always,
}

/// <summary>
/// 小窗的位置模式
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuidePositionMode>))]
public enum GuidePositionMode
{
    /// <summary>跟随游戏窗口移动</summary>
    FollowGame,

    /// <summary>自由放置在屏幕上</summary>
    Free,
}

/// <summary>
/// 跟随游戏时停靠的游戏客户区角
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuideAnchor>))]
public enum GuideAnchor
{
    /// <summary>左上</summary>
    TopLeft,

    /// <summary>右上</summary>
    TopRight,

    /// <summary>左下</summary>
    BottomLeft,

    /// <summary>右下</summary>
    BottomRight,
}

/// <summary>
/// 何时跳到上次观看的位置
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GuideResumeMode>))]
public enum GuideResumeMode
{
    /// <summary>只在从收藏或观看记录打开时</summary>
    FromLibrary,

    /// <summary>打开任何有记录的视频时</summary>
    Always,

    /// <summary>从不</summary>
    Never,
}
