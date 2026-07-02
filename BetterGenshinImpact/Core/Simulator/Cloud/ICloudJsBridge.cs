using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Simulator.Cloud;

/// <summary>
/// 云输入队列与页面 JavaScript 包装层之间的最小通信协议。
/// </summary>
public interface ICloudJsBridge
{
    /// <summary>
    /// 按原顺序向页面提交一个命令批次。
    /// </summary>
    Task DispatchAsync(IReadOnlyList<CloudInputCommand> commands, CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放页面输入脚本记录的全部按键和鼠标按钮。
    /// </summary>
    Task ReleaseAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置页面输入脚本的内部状态。
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
