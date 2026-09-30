using BetterGenshinImpact.Core.Input;
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
    IInputBackend input) : IDisposable
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
    /// 释放截图器和窗口监听。不关闭游戏，也不释放 <see cref="Input"/>
    /// </summary>
    public void Dispose()
    {
        Capture.Dispose();
        Window.Dispose();
    }
}
