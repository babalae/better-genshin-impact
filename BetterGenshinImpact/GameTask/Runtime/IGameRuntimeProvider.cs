using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 某种运行环境的获取与关闭。每种运行环境一个实现，注册为 DI 单例
/// </summary>
public interface IGameRuntimeProvider
{
    GameRuntimeKind Kind { get; }

    /// <summary>
    /// 获取运行环境：先附着到正在运行的游戏，没有就按本环境的策略启动后再附着。
    /// 不能获取时返回 null，提示由实现给出。在 UI 线程上调用
    /// </summary>
    Task<GameRuntime?> AcquireAsync(CancellationToken ct);

    /// <summary>
    /// 关闭游戏。Win32：结束游戏进程；网页版：关闭宿主窗口
    /// </summary>
    void CloseGame();
}
