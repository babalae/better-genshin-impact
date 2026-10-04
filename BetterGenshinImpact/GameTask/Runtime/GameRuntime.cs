using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Mask;
using Fischless.GameCapture;
using System;

namespace BetterGenshinImpact.GameTask.Runtime;

/// <summary>
/// 游戏运行环境：一次绑定的产物，由 <see cref="IGameRuntimeProvider"/> 创建。
/// 两种环境的差异都在 Provider 和 <see cref="IGameWindow"/> 的实现里，这里只做组合
/// </summary>
public sealed class GameRuntime(
    GameRuntimeKind kind,
    IGameWindow window,
    IGameCapture capture,
    IInputBackend input,
    IMaskWindowDrawingBoard maskWindowDrawingBoard,
    IMaskWindowMapState maskWindowMapState) : IDisposable
{
    public GameRuntimeKind Kind { get; } = kind;

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
    /// 释放截图器和窗口监听。不关闭游戏，也不释放 <see cref="Input"/>、<see cref="MaskWindowDrawingBoard"/>、<see cref="MaskWindowMapState"/>
    /// </summary>
    public void Dispose()
    {
        Capture.Dispose();
        Window.Dispose();
    }
}
