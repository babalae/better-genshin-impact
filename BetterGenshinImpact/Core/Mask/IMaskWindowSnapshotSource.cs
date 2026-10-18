using System;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩窗口渲染侧的读取接口，仅 MaskWindowViewModel 使用
/// </summary>
public interface IMaskWindowSnapshotSource<out T>
{
    /// <summary>
    /// 不可变快照，UI 线程直接读取，无需加锁
    /// </summary>
    T Current { get; }

    /// <summary>
    /// 可在任意线程触发，只表示"有新版本"，由订阅方自行合并。回调里只能做 O(1) 的操作
    /// </summary>
    event Action? Changed;
}
