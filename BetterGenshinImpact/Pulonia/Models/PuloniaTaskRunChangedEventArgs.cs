using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 通知页面重新读取指定运行状态的事件参数。
/// </summary>
public sealed class PuloniaTaskRunChangedEventArgs : EventArgs
{
    /// <summary>
    /// 状态发生变化的运行 ID。
    /// </summary>
    public string RunId { get; }

    /// <summary>
    /// 建立运行状态变化通知。
    /// </summary>
    public PuloniaTaskRunChangedEventArgs(string runId)
    {
        RunId = runId;
    }
}
