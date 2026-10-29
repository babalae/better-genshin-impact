using System;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 计算显示规则需要的输入
/// </summary>
/// <param name="IsOpen">小窗是否打开</param>
/// <param name="HiddenByUser">被用户隐藏</param>
/// <param name="HiddenByTask">因任务运行被隐藏</param>
/// <param name="TaskForceShown">任务运行中用户主动显示</param>
/// <param name="HiddenByGame">因游戏最小化或切到后台被隐藏</param>
/// <param name="Interaction">交互模式</param>
/// <param name="ClickThrough">用户开启了鼠标穿透</param>
/// <param name="PointerOver">鼠标悬停在小窗上（未穿透时）</param>
/// <param name="CursorInside">鼠标穿透时，可见的鼠标指针位于小窗上</param>
/// <param name="SmartFade">开启了智能透明</param>
/// <param name="Opacity">设置的不透明度</param>
/// <param name="SmartFadeOpacity">智能透明淡化后的不透明度</param>
public readonly record struct GuidePresentationInput(
    bool IsOpen,
    bool HiddenByUser,
    bool HiddenByTask,
    bool TaskForceShown,
    bool HiddenByGame,
    bool Interaction,
    bool ClickThrough,
    bool PointerOver,
    bool CursorInside,
    bool SmartFade,
    double Opacity,
    double SmartFadeOpacity);

/// <summary>
/// 显示规则的计算结果
/// </summary>
/// <param name="Visible">是否显示</param>
/// <param name="ClickThrough">是否鼠标穿透</param>
/// <param name="Activatable">是否允许获得焦点</param>
/// <param name="Opacity">不透明度</param>
/// <param name="BarAllowed">是否允许显示窗口栏</param>
public readonly record struct GuidePresentation(bool Visible, bool ClickThrough, bool Activatable, double Opacity, bool BarAllowed);

/// <summary>
/// 小窗显示规则：从上到下判断，命中一条即用该条结果（见设计文档 6.2 节）。纯函数，不访问任何状态
/// </summary>
public static class GuideWindowPresentation
{
    /// <summary>
    /// 不透明度下限，避免窗口几乎看不见却仍挡住点击
    /// </summary>
    public const double MinOpacity = 0.2;

    /// <summary>
    /// 按优先级计算最终的显示状态
    /// </summary>
    public static GuidePresentation Compute(in GuidePresentationInput input)
    {
        var opacity = Math.Clamp(input.Opacity, MinOpacity, 1);

        // 1. 未打开，或被用户、任务、游戏状态隐藏
        if (!input.IsOpen || input.HiddenByUser || input.HiddenByTask && !input.TaskForceShown || input.HiddenByGame)
        {
            return new GuidePresentation(false, true, false, opacity, false);
        }

        // 2. 交互模式：可以用键盘鼠标完整操作网页
        if (input.Interaction)
        {
            return new GuidePresentation(true, false, true, 1, true);
        }

        // 3. 任务运行中被用户临时显示：强制穿透，避免挡住任务的点击
        if (input.HiddenByTask && input.TaskForceShown)
        {
            return new GuidePresentation(true, true, false, opacity, false);
        }

        // 4. 鼠标穿透：智能透明开启且可见的鼠标经过时淡化，露出下方的游戏画面
        if (input.ClickThrough)
        {
            var fade = Math.Clamp(input.SmartFadeOpacity, 0.1, 0.8);
            return new GuidePresentation(true, true, false,
                input.SmartFade && input.CursorInside ? Math.Min(opacity, fade) : opacity, false);
        }

        // 5. 悬停：显示窗口栏，不透明度 100%
        if (input.PointerOver)
        {
            return new GuidePresentation(true, false, false, 1, true);
        }

        // 6. 其他：使用设置的不透明度
        return new GuidePresentation(true, false, false, opacity, false);
    }
}
