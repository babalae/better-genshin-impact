using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 由任务编排层显式执行的角色复苏及神像恢复流程。
/// </summary>
public sealed class DefeatRecoveryTask
{
    public async Task<bool> Start(CancellationToken ct)
    {
        using (var region = CaptureToRectArea())
        {
            if (!Bv.ClickIfInReviveModal(region))
            {
                Logger.LogWarning("未检测到复苏界面，无法执行死亡恢复");
                return false;
            }
        }

        await Bv.WaitForMainUi(ct);
        await Delay(4000, ct);
        await RunnerContext.Instance.StopAutoPickRunTask(
            async () => await new TpTask(ct).TpToStatueOfTheSeven(),
            5);
        Logger.LogInformation("角色复苏完成，已前往七天神像恢复");
        return true;
    }
}
