namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>调度器只读取用户活动，不通过输入检测主动操作桌面。</summary>
public interface IPuloniaUserActivityMonitor
{
    /// <summary>当前是否有可操作且未锁定的交互桌面；检测失败视为不可用。</summary>
    bool DesktopAvailable { get; }
    /// <summary>排除本程序输入后的空闲秒数。</summary>
    double IdleSeconds { get; }
    /// <summary>随用户输入或桌面切换递增，用于发现执行过程中的活动。</summary>
    long ActivityVersion { get; }
}
