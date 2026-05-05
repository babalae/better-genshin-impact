using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Tasks;

namespace BetterGenshinImpact.Service.GearTask;

/// <summary>
/// 错误占位任务，执行时抛出创建阶段捕获到的异常信息。
/// </summary>
internal class ErrorGearTask : BaseGearTask
{
    private readonly string _errorMessage;

    public ErrorGearTask(string errorMessage)
    {
        _errorMessage = errorMessage;
    }

    public override Task Run(CancellationToken ct)
    {
        throw new InvalidOperationException($"任务转换失败: {_errorMessage}");
    }
}
