using System.Threading;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 和触发器的区别：任务不需要持续捕获游戏图像
/// </summary>
public class BaseTaskThread
{

    protected CancellationTokenSource Cancellation;

    public BaseTaskThread(CancellationTokenSource cts)
    {
        Cancellation = cts;
    }
}