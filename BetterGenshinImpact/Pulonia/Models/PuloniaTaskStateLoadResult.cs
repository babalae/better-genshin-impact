namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 状态文件读取结果，显式标记是否发生备份回退。
/// </summary>
public sealed class PuloniaTaskStateLoadResult
{
    /// <summary>
    /// 成功读取或首次建立的状态。
    /// </summary>
    public PuloniaTaskState State { get; }

    /// <summary>
    /// 是否因正式文件损坏而从备份恢复。
    /// </summary>
    public bool RecoveredFromBackup { get; }

    /// <summary>
    /// 建立状态读取结果。
    /// </summary>
    public PuloniaTaskStateLoadResult(PuloniaTaskState state, bool recoveredFromBackup)
    {
        State = state;
        RecoveredFromBackup = recoveredFromBackup;
    }
}
