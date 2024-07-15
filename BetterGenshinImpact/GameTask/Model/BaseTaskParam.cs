using System.Threading;
using BetterGenshinImpact.GameTask.Model.Enum;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 独立任务参数基类
/// </summary>
public class BaseTaskParam
{
    public string Name { get; set; } = string.Empty;

    public CancellationTokenSource Cts { get; set; }

    /// <summary>
    /// 针对实时触发器的操作
    /// </summary>
    public DispatcherTimerOperationEnum TriggerOperation { get; set; } = DispatcherTimerOperationEnum.None;

    protected BaseTaskParam(CancellationTokenSource cts)
    {
        this.Cts = cts;
    }
}
