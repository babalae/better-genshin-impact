using System;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 小窗窗口的抽象，由 View 层的 GuideWindow 实现。服务层只通过它控制窗口，不引用 WPF 窗口类型。
/// 所有成员只在界面线程调用；坐标与尺寸都是物理像素
/// </summary>
public interface IGuideWindowView
{
    /// <summary>
    /// 窗口内的网页播放器
    /// </summary>
    IGuidePlayer Player { get; }

    /// <summary>
    /// 窗口句柄，调用时会确保句柄已创建
    /// </summary>
    nint Handle { get; }

    /// <summary>
    /// 窗口当前是否显示
    /// </summary>
    bool IsShown { get; }

    /// <summary>
    /// 显示窗口，不激活、不抢焦点
    /// </summary>
    void ShowWithoutActivation();

    /// <summary>
    /// 隐藏窗口，保留网页
    /// </summary>
    void HideWindow();

    /// <summary>
    /// 关闭窗口并释放网页
    /// </summary>
    void CloseWindow();

    /// <summary>
    /// 设置窗口位置和尺寸（物理像素），不激活窗口
    /// </summary>
    void SetBounds(RECT bounds);

    /// <summary>
    /// 读取窗口位置和尺寸（物理像素）
    /// </summary>
    bool TryGetBounds(out RECT bounds);

    /// <summary>
    /// 重新放到置顶层的最上方，不激活窗口
    /// </summary>
    void BringToTopmost();

    /// <summary>
    /// 激活窗口以接收键盘输入，只在交互模式下调用
    /// </summary>
    void ActivateForInput();

    /// <summary>
    /// 窗口已关闭（用户关闭或程序关闭）
    /// </summary>
    event EventHandler? Closed;
}

/// <summary>
/// 字幕浮窗的抽象，由 View 层的 GuideSubtitleWindow 实现。所有成员只在界面线程调用
/// </summary>
public interface IGuideSubtitleView
{
    /// <summary>
    /// 窗口当前是否显示
    /// </summary>
    bool IsShown { get; }

    /// <summary>
    /// 显示窗口，不激活
    /// </summary>
    void ShowWithoutActivation();

    /// <summary>
    /// 隐藏窗口
    /// </summary>
    void HideWindow();

    /// <summary>
    /// 关闭窗口
    /// </summary>
    void CloseWindow();

    /// <summary>
    /// 更新定位参照区域（物理像素）：游戏画面，没有游戏窗口时为主显示器工作区。浮窗按配置中的比例放在其中
    /// </summary>
    void UpdateAnchor(RECT anchor);

    /// <summary>
    /// 重新放到置顶层的最上方，不激活窗口
    /// </summary>
    void BringToTopmost();
}
