using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 独立任务接口
/// </summary>
public interface ISoloTask
{
    /// <summary>
    /// 独立任务名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 启动独立任务
    /// </summary>
    /// <param name="ct">取消Token</param>
    /// <returns></returns>
    Task Start(CancellationToken ct);
}

public interface ISoloTask<T> : ISoloTask
{
    /// <summary>
    /// 启动独立任务
    /// </summary>
    /// <param name="ct">取消Token</param>
    /// <returns></returns>
    new Task<T> Start(CancellationToken ct);
}
