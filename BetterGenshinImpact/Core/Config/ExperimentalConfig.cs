using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 实验性优化设置
/// </summary>
[Serializable]
public partial class ExperimentalConfig : ObservableObject
{
    /// <summary>
    /// 窗口检测增强兜底（即时生效，每次点击启动按钮/查找窗口时读取）：
    /// 旧逻辑（按进程名取 MainWindowHandle + 会话过滤）失败时，依次兜底：
    /// ① 按 UnityWndClass 窗口类名 EnumWindows 枚举并反查进程名校验；
    /// ② 对原神进程 EnumWindows 取最大的可见顶层窗口（不依赖 MainWindowHandle、不限会话）。
    /// 默认关闭，关闭时行为与旧版完全一致；开启后如绑定到错误窗口请关闭此开关回退。
    /// </summary>
    [ObservableProperty]
    private bool _windowDetectFallbackEnabled = false;
}
