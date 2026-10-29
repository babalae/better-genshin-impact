using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 打开小窗的来源，决定是否续播、是否自动播放
/// </summary>
public enum GuideOpenSource
{
    /// <summary>用户通过快捷键、按钮、托盘打开</summary>
    User,

    /// <summary>进入游戏时自动打开</summary>
    AutoOpen,

    /// <summary>从收藏打开</summary>
    Favorite,

    /// <summary>从观看记录打开</summary>
    History,

    /// <summary>从地图点位详情打开</summary>
    MapPoint,

    /// <summary>从剪贴板打开</summary>
    Clipboard,

    /// <summary>从地址框打开</summary>
    Address,
}

/// <summary>
/// 打开小窗的请求
/// </summary>
/// <param name="Url">要打开的网址；为空时恢复上次页面或打开主页</param>
/// <param name="Source">来源</param>
public sealed record GuideOpenRequest(string? Url, GuideOpenSource Source = GuideOpenSource.User);

/// <summary>
/// 悬浮小窗的唯一入口：快捷键、窗口栏、托盘、地图点位、剪贴板、设置页都调用这里（见设计文档 5.3 节）。
/// 所有方法都可以在任意线程调用，内部切换到界面线程执行
/// </summary>
public interface IGuideWindowService
{
    /// <summary>
    /// 小窗当前状态，界面绑定用
    /// </summary>
    GuideWindowState State { get; }

    /// <summary>
    /// 小窗配置
    /// </summary>
    GuideWindowConfig Config { get; }

    /// <summary>
    /// 当前实例能否使用小窗（首版只在主实例提供）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 打开小窗；已打开时显示出来，请求中有网址时打开该网址
    /// </summary>
    void Open(GuideOpenRequest? request = null);

    /// <summary>
    /// 显示或隐藏小窗；未打开时打开
    /// </summary>
    void ToggleVisible();

    /// <summary>
    /// 隐藏小窗，保留网页
    /// </summary>
    void Hide();

    /// <summary>
    /// 关闭小窗并释放网页
    /// </summary>
    void Close();

    /// <summary>
    /// 紧急恢复：显示小窗，退出交互与沉浸模式，关闭鼠标穿透，不透明度恢复 100%，移回可见区域
    /// </summary>
    void RestoreEmergency();

    /// <summary>
    /// 播放或暂停
    /// </summary>
    void TogglePlay();

    /// <summary>
    /// 快进（1）或快退（-1），步长取设置值
    /// </summary>
    void SeekBy(int direction);

    /// <summary>
    /// 倍速增加（1）或降低（-1）
    /// </summary>
    void ChangeRate(int direction);

    /// <summary>
    /// 设置倍速
    /// </summary>
    void SetRate(double rate);

    /// <summary>
    /// 开始按住临时加速
    /// </summary>
    void BeginBoost();

    /// <summary>
    /// 结束按住临时加速，恢复原倍速
    /// </summary>
    void EndBoost();

    /// <summary>
    /// 音量增加（1）或降低（-1）
    /// </summary>
    void ChangeVolume(int direction);

    /// <summary>
    /// 静音或取消静音
    /// </summary>
    void ToggleMute();

    /// <summary>
    /// 上一集（-1）或下一集（1）
    /// </summary>
    void SwitchEpisode(int offset);

    /// <summary>
    /// 鼠标穿透开关
    /// </summary>
    void ToggleClickThrough();

    /// <summary>
    /// 交互模式开关
    /// </summary>
    void ToggleInteraction();

    /// <summary>
    /// 进入交互模式（编辑地址、网页输入框获得焦点时自动调用）
    /// </summary>
    void BeginInteraction();

    /// <summary>
    /// 结束交互模式，进入前游戏在前台时把焦点还给游戏
    /// </summary>
    void EndInteraction();

    /// <summary>
    /// 沉浸模式开关
    /// </summary>
    void ToggleImmersive();

    /// <summary>
    /// 字幕浮窗开关
    /// </summary>
    void ToggleSubtitle();

    /// <summary>
    /// 不透明度增加（1）或降低（-1），步长 10%
    /// </summary>
    void ChangeOpacity(int direction);

    /// <summary>
    /// 始终置顶开关
    /// </summary>
    void ToggleTopmost();

    /// <summary>
    /// 弹幕开关（仅 B 站）
    /// </summary>
    void ToggleDanmaku();

    /// <summary>
    /// 收藏或取消收藏当前视频
    /// </summary>
    void ToggleFavorite();

    /// <summary>
    /// 打开剪贴板中的链接
    /// </summary>
    void OpenFromClipboard();

    /// <summary>
    /// 打开地址框输入的内容：网址、BV 号或搜索词
    /// </summary>
    void NavigateFromInput(string input);

    /// <summary>
    /// 网页后退
    /// </summary>
    void GoBack();

    /// <summary>
    /// 网页刷新
    /// </summary>
    void Reload();

    /// <summary>
    /// 设置网页缩放比例
    /// </summary>
    void SetZoom(double factor);

    /// <summary>
    /// 切换位置模式，保留小窗当前位置
    /// </summary>
    void SetPositionMode(GuidePositionMode mode);

    /// <summary>
    /// 停靠到游戏画面（跟随模式）或所在显示器（自由模式）的一个角
    /// </summary>
    void Dock(GuideAnchor anchor);

    /// <summary>
    /// 移到指定显示器的右上角，切换为自由模式
    /// </summary>
    /// <param name="monitorIndex">显示器序号，与 System.Windows.Forms.Screen.AllScreens 的顺序一致</param>
    void MoveToMonitor(int monitorIndex);

    /// <summary>
    /// 小窗报告鼠标是否悬停在窗口上（未穿透时）
    /// </summary>
    void SetPointerOver(bool over);

    /// <summary>
    /// 小窗报告可见的鼠标指针是否位于窗口上（穿透时，用于智能透明）
    /// </summary>
    void SetCursorInside(bool inside);

    /// <summary>
    /// 小窗报告用户拖动或缩放结束，保存位置和尺寸
    /// </summary>
    void CommitBounds();

    /// <summary>
    /// 小窗报告窗口意外获得了焦点（不在交互模式时），鼠标松开后把焦点还给游戏
    /// </summary>
    void OnUnexpectedActivation();

    /// <summary>
    /// 开始调整字幕浮窗位置
    /// </summary>
    void BeginSubtitleAdjust();

    /// <summary>
    /// 结束调整字幕浮窗位置
    /// </summary>
    void EndSubtitleAdjust();

    /// <summary>
    /// 网页异常后重新加载
    /// </summary>
    void ReloadAfterCrash();
}
