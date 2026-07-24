using System;

namespace BetterGenshinImpact.Core.Monitor;

public enum RelativeMouseInputType
{
    DirectInput,
    RawInput
}

public sealed class RelativeMouseMoveEventArgs(int deltaX, int deltaY, DateTime timestamp) : EventArgs
{
    public int DeltaX { get; } = deltaX;

    public int DeltaY { get; } = deltaY;

    public DateTime Timestamp { get; } = timestamp;
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

public sealed class RawKeyboardInputEventArgs(
    ushort virtualKey,
    ushort scanCode,
    ushort flags,
    bool isKeyDown,
    DateTime timestamp) : EventArgs
{
    public ushort VirtualKey { get; } = virtualKey;

    public ushort ScanCode { get; } = scanCode;

    public ushort Flags { get; } = flags;

    public bool IsKeyDown { get; } = isKeyDown;

    public DateTime Timestamp { get; } = timestamp;
}

public interface IRawKeyboardInputMonitor
{
    /// <summary>
    /// 订阅 Raw Input 键盘事件。首个鼠标或键盘订阅会启动采集，
    /// 最后一个订阅释放后停止采集。回调在采集线程执行。
    /// </summary>
    IDisposable SubscribeKeyboard(EventHandler<RawKeyboardInputEventArgs> handler);
}
