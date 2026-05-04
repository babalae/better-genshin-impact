using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Tasks;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 容器任务，用于目录类型或禁用任务的结构承载，不执行实际逻辑。
/// </summary>
internal class ContainerGearTask : BaseGearTask
{
    public override Task Run(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
