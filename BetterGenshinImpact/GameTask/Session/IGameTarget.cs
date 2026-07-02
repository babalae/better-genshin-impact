using System;
using System.Drawing;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Session;

/// <summary>
/// 可被 BetterGI 任务系统操作的游戏运行目标。
/// 该抽象只描述目标生命周期和显示能力，不包含具体截图及输入实现。
/// </summary>
public interface IGameTarget : IAsyncDisposable
{
    /// <summary>
    /// 获取目标进程或宿主窗口是否仍然存活。
    /// </summary>
    bool IsAlive { get; }

    /// <summary>
    /// 获取目标是否允许在非前台状态继续执行任务。
    /// </summary>
    bool CanRunInBackground { get; }

    /// <summary>
    /// 获取运行目标的父窗口句柄。
    /// </summary>
    IntPtr WindowHandle { get; }

    /// <summary>
    /// 获取任务识图使用的固定画面尺寸。
    /// </summary>
    Size CaptureSize { get; }

    /// <summary>
    /// 显示目标并允许用户交互。
    /// </summary>
    Task ShowInteractiveAsync();

    /// <summary>
    /// 显示目标但禁止用户交互。
    /// </summary>
    Task ShowReadOnlyAsync();

    /// <summary>
    /// 隐藏目标窗口，同时保留后台运行资源。
    /// </summary>
    Task HideAsync();
}
