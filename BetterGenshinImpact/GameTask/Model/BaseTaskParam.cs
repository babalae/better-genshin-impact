using System.Threading;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 独立任务参数基类
/// </summary>
public class BaseTaskParam
{

    public CancellationTokenSource Cts { get; set; }

    public BaseTaskParam(CancellationTokenSource cts)  {
        Cts = cts;
    }
}