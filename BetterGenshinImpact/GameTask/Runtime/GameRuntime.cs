using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.Model;
using Fischless.GameCapture;
using System;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 游戏运行环境：一次绑定的产物，由 <see cref="IGameRuntimeProvider"/> 创建，<see cref="GameRuntimeService"/> 负责结束。
/// 两种环境的差异都在 Provider 和 <see cref="IGameWindow"/> 的实现里，这里只做组合；
/// 绑定到它的操作（截图、等待、按 1080P 坐标点击）见 GameRuntimeExtensions
/// </summary>
public sealed class GameRuntime(
    GameRuntimeKind kind,
    IGameWindow window,
    IGameCapture capture,
    IInputBackend input,
    ISystemInfo systemInfo,
    IMaskWindowDrawingBoard maskWindowDrawingBoard,
    IMaskWindowMapState maskWindowMapState) : IDisposable
{
    /// <summary>
    /// 解绑开始时取消。不释放：持有者在 Dispose 之后仍会读取 <see cref="Stopping"/>；
    /// 这个 CTS 没有使用计时器，不释放也不会泄漏
    /// </summary>
    private readonly CancellationTokenSource _stopping = new();

    /// <summary>
    /// 运行环境的种类
    /// </summary>
    public GameRuntimeKind Kind { get; } = kind;

    /// <summary>
    /// 游戏画面所在的顶层窗口
    /// </summary>
    public IGameWindow Window { get; } = window ?? throw new ArgumentNullException(nameof(window));

    /// <summary>
    /// 已经 Start 的截图器
    /// </summary>
    public IGameCapture Capture { get; } = capture ?? throw new ArgumentNullException(nameof(capture));

    /// <summary>
    /// 绑定时交给 InputHub.Attach，之后由 InputHub 负责释放
    /// </summary>
    public IInputBackend Input { get; } = input ?? throw new ArgumentNullException(nameof(input));

    /// <summary>
    /// 当前画面区域及其分辨率派生数据，由 Provider 在启动截图器之前构建。
    /// 窗口移动或缩放时由调度器同步 CaptureAreaRect；DpiScale 仍为绑定时快照
    /// </summary>
    public ISystemInfo SystemInfo { get; } = systemInfo ?? throw new ArgumentNullException(nameof(systemInfo));

    /// <summary>
    /// 在这次运行环境的画面上叠加绘制，坐标为捕获像素。
    /// 借用进程级的 DI 单例，不属于运行环境：Dispose 不释放；解绑时由 GameRuntimeService 清空内容
    /// </summary>
    public IMaskWindowDrawingBoard MaskWindowDrawingBoard { get; } =
        maskWindowDrawingBoard ?? throw new ArgumentNullException(nameof(maskWindowDrawingBoard));

    /// <summary>
    /// 这次运行环境的地图点位状态（是否在大地图、视口）。
    /// 与 <see cref="MaskWindowDrawingBoard"/> 相同：借用进程级的 DI 单例，Dispose 不释放；解绑时由 GameRuntimeService 重置
    /// </summary>
    public IMaskWindowMapState MaskWindowMapState { get; } =
        maskWindowMapState ?? throw new ArgumentNullException(nameof(maskWindowMapState));

    /// <summary>
    /// 本运行环境开始解绑时取消。运行期对象据此判断"我的运行环境已结束"，不需要查询当前绑定的是哪一个
    /// </summary>
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>
    /// 本运行环境是否已经开始解绑
    /// </summary>
    public bool IsStopping => _stopping.IsCancellationRequested;

    /// <summary>
    /// 由 GameRuntimeService 在解绑第一步调用，可重复调用。
    /// 已注册的回调在调用线程（UI 线程）上同步执行，注册者只能做轻量操作
    /// </summary>
    internal void SignalStopping()
    {
        if (!_stopping.IsCancellationRequested)
        {
            _stopping.Cancel();
        }
    }

    /// <summary>
    /// 释放截图器和窗口监听。不关闭游戏，也不释放 <see cref="Input"/>、<see cref="MaskWindowDrawingBoard"/>、<see cref="MaskWindowMapState"/>
    /// </summary>
    public void Dispose()
    {
        SignalStopping();
        Capture.Dispose();
        Window.Dispose();
    }
}
