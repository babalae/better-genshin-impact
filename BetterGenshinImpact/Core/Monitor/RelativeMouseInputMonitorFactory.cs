using System;

namespace BetterGenshinImpact.Core.Monitor;

public sealed class RelativeMouseInputMonitorFactory(
    DirectInputMonitor directInputMonitor,
    RawInputMonitor rawInputMonitor) : IRelativeMouseInputMonitorFactory
{
    public IRelativeMouseInputMonitor Get(RelativeMouseInputType type)
    {
        return type switch
        {
            RelativeMouseInputType.DirectInput => directInputMonitor,
            RelativeMouseInputType.RawInput => rawInputMonitor,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的相对鼠标输入类型")
        };
    }
}
