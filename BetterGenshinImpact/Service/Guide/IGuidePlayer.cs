using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 小窗网页的播放器抽象。由 View 层的播放桥实现，服务层不引用 WebView2 控件（见设计文档第 13 节）。
/// 成员只在界面线程调用，事件也在界面线程触发
/// </summary>
public interface IGuidePlayer
{
    /// <summary>
    /// 最近一次上报的播放状态，网页尚未上报时为 null
    /// </summary>
    GuidePlayerState? State { get; }

    /// <summary>
    /// 网页当前地址，网页尚未初始化时为 null
    /// </summary>
    string? CurrentUrl { get; }

    /// <summary>
    /// 是否可以后退
    /// </summary>
    bool CanGoBack { get; }

    /// <summary>
    /// 整个网页是否静音
    /// </summary>
    bool IsMuted { get; set; }

    /// <summary>
    /// 播放状态更新
    /// </summary>
    event EventHandler<GuidePlayerState>? StateChanged;

    /// <summary>
    /// 当前字幕文本变化，没有字幕时为空字符串
    /// </summary>
    event EventHandler<string>? SubtitleChanged;

    /// <summary>
    /// 网页事件：视频切换、播放结束、输入框获得焦点等
    /// </summary>
    event EventHandler<GuidePlayerEvent>? EventRaised;

    /// <summary>
    /// 执行播放命令。网页尚未就绪或命令失败时返回 <see cref="GuideCommandResult.Ok"/> 为 false 的结果，不抛出异常
    /// </summary>
    /// <param name="command">命令</param>
    /// <param name="value">命令参数，例如快进秒数、倍速、音量</param>
    Task<GuideCommandResult> ExecuteAsync(GuidePlayerCommand command, double value = 0);

    /// <summary>
    /// 打开网址。网页尚未初始化时在初始化完成后打开
    /// </summary>
    void Navigate(string url);

    /// <summary>
    /// 后退
    /// </summary>
    void GoBack();

    /// <summary>
    /// 刷新
    /// </summary>
    void Reload();

    /// <summary>
    /// 设置网页缩放比例
    /// </summary>
    void SetZoom(double factor);
}

/// <summary>
/// 播放命令，与页面脚本 window.__bgiGuide.exec 的 cmd 一一对应
/// </summary>
public enum GuidePlayerCommand
{
    /// <summary>播放</summary>
    Play,

    /// <summary>暂停</summary>
    Pause,

    /// <summary>播放或暂停</summary>
    Toggle,

    /// <summary>相对跳转，参数为秒数，负数后退</summary>
    SeekBy,

    /// <summary>跳到指定位置，参数为秒数</summary>
    SeekTo,

    /// <summary>设置倍速</summary>
    SetRate,

    /// <summary>设置音量，0–1</summary>
    SetVolume,

    /// <summary>上一集</summary>
    PrevEpisode,

    /// <summary>下一集</summary>
    NextEpisode,

    /// <summary>进入或退出沉浸模式，参数大于 0 表示进入</summary>
    SetImmersive,

    /// <summary>新视频加载后是否自动开启中文字幕，参数大于 0 表示开启</summary>
    SetAutoSubtitle,

    /// <summary>是否隐藏网页内的字幕，参数大于 0 表示隐藏</summary>
    SetPageSubtitleHidden,

    /// <summary>开关弹幕（仅 B 站），参数大于 0 表示开启</summary>
    SetDanmaku,

    /// <summary>立即上报一次播放状态</summary>
    RequestState,
}

/// <summary>
/// 网页事件类型
/// </summary>
public enum GuidePlayerEventKind
{
    /// <summary>页面脚本已就绪（每个文档一次）</summary>
    Ready,

    /// <summary>切换到了另一个视频（含页内跳转和切集）</summary>
    VideoChanged,

    /// <summary>视频播放结束</summary>
    Ended,

    /// <summary>网页拒绝播放（自动播放策略或网站限制）</summary>
    PlayRejected,

    /// <summary>网页中的输入框获得焦点</summary>
    EditFocus,

    /// <summary>网页中的输入框失去焦点</summary>
    EditBlur,

    /// <summary>用户在网页内退出了网页全屏</summary>
    ImmersiveExited,

    /// <summary>网页进程反复崩溃，需要用户重新加载</summary>
    Crashed,

    /// <summary>浏览器进程退出，只能关闭小窗后重新打开</summary>
    BrowserExited,

    /// <summary>网页初始化失败</summary>
    InitializeFailed,
}

/// <summary>
/// 网页事件
/// </summary>
/// <param name="Kind">事件类型</param>
/// <param name="Message">附加说明，例如初始化失败原因</param>
public sealed record GuidePlayerEvent(GuidePlayerEventKind Kind, string? Message = null);

/// <summary>
/// 播放命令的执行结果
/// </summary>
/// <param name="Ok">是否成功</param>
/// <param name="Reason">失败原因：noVideo、noNext、noPrev、unsupported、notReady 等</param>
public sealed record GuideCommandResult(bool Ok, string? Reason = null)
{
    /// <summary>
    /// 网页尚未就绪
    /// </summary>
    public static GuideCommandResult NotReady { get; } = new(false, "notReady");

    /// <summary>
    /// 成功
    /// </summary>
    public static GuideCommandResult Success { get; } = new(true);
}

/// <summary>
/// 页面上报的播放状态（见设计文档 7.1 节）
/// </summary>
public sealed record GuidePlayerState
{
    /// <summary>页面地址</summary>
    public string Href { get; init; } = string.Empty;

    /// <summary>页面或视频标题</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>站点：bilibili 或 generic</summary>
    public string Site { get; init; } = "generic";

    /// <summary>视频标识，非视频页为 null</summary>
    public string? VideoKey { get; init; }

    /// <summary>页面中是否有视频</summary>
    public bool HasVideo { get; init; }

    /// <summary>是否暂停</summary>
    public bool Paused { get; init; } = true;

    /// <summary>当前位置（秒）</summary>
    public double CurrentTime { get; init; }

    /// <summary>时长（秒），未知或直播为 0</summary>
    public double Duration { get; init; }

    /// <summary>倍速</summary>
    public double PlaybackRate { get; init; } = 1;

    /// <summary>音量，0–1</summary>
    public double Volume { get; init; } = 1;

    /// <summary>视频宽度（像素）</summary>
    public double VideoWidth { get; init; }

    /// <summary>视频高度（像素）</summary>
    public double VideoHeight { get; init; }

    /// <summary>是否处于沉浸模式（B 站网页全屏或通用聚焦）</summary>
    public bool Immersive { get; init; }

    /// <summary>当前集序号（从 1 开始），未知为 0</summary>
    public int EpisodeIndex { get; init; }

    /// <summary>总集数，未知为 0</summary>
    public int EpisodeCount { get; init; }

    /// <summary>是否有上一集</summary>
    public bool HasPrevEpisode { get; init; }

    /// <summary>是否有下一集</summary>
    public bool HasNextEpisode { get; init; }

    /// <summary>弹幕是否开启，站点不支持时为 null</summary>
    public bool? Danmaku { get; init; }

    /// <summary>收到这条状态的时间（<see cref="Environment.TickCount64"/>）</summary>
    public long ReceivedAt { get; init; }
}
