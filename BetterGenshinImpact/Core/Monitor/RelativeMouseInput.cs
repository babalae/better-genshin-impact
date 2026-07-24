using System;

namespace BetterGenshinImpact.Core.Monitor;

public enum RelativeMouseInputType
{
    DirectInput,
    RawInput
}

public sealed class RelativeMouseMoveEventArgs(int deltaX, int deltaY, uint timestamp) : EventArgs
{
    public int DeltaX { get; } = deltaX;

    public int DeltaY { get; } = deltaY;

    public uint Timestamp { get; } = timestamp;
}

public interface IRelativeMouseInputMonitor
{
    /// <summary>
    /// 订阅相对鼠标移动事件。首个订阅会启动采集，最后一个订阅释放后停止采集。
    /// 回调在采集线程执行，需要操作 UI 时由订阅方自行切换到 UI 线程。
    /// </summary>
    IDisposable Subscribe(EventHandler<RelativeMouseMoveEventArgs> handler);
}

public interface IRelativeMouseInputMonitorFactory
{
    IRelativeMouseInputMonitor Get(RelativeMouseInputType type);
}
