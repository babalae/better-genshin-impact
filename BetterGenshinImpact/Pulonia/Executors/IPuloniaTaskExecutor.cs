using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 执行一种或多种已注册 Pulonia 叶子任务的能力合同。
/// </summary>
public interface IPuloniaTaskExecutor
{
    /// <summary>
    /// 执行器支持的任务类型及参数格式说明。
    /// </summary>
    IReadOnlyList<PuloniaTaskDefinition> Definitions { get; }

    /// <summary>
    /// 使用快照中的有效参数执行一个节点，并响应取消令牌。
    /// </summary>
    Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct);
}
