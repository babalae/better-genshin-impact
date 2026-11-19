using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPick;

/// <summary>
/// 配置组地图追踪如何挂拾取触发器。
/// </summary>
public static class PathingGroupPickup
{
    /// <summary>万叶/琴吸怪后，给模板拾取留出的等待时间。</summary>
    public const int ArtifactPickupWaitAfterKazuhaMs = 2000;

    public static PathingPickupMode Resolve(PathingPartyConfig? partyConfig)
    {
        if (partyConfig is not { Enabled: true })
        {
            return PathingPickupMode.AutoPick;
        }

        if (partyConfig.PickupMode == PathingPickupMode.ArtifactOnly)
        {
            return PathingPickupMode.ArtifactOnly;
        }

        return partyConfig.AutoPickEnabled ? PathingPickupMode.AutoPick : PathingPickupMode.Disabled;
    }

    /// <summary>当前调度任务所属配置组是否为「只拾取圣遗物」。</summary>
    public static bool IsCurrentArtifactOnly()
    {
        var partyConfig = RunnerContext.Instance.CurrentScriptProject?.GroupInfo?.Config.PathingConfig;
        return Resolve(partyConfig) == PathingPickupMode.ArtifactOnly;
    }

    /// <summary>
    /// 万叶/琴拾取动作结束后：若当前组是只拾取圣遗物，停留片刻让实时触发器捡完。
    /// 应在切回原队伍之前调用。
    /// </summary>
    public static async Task DelayAfterKazuhaIfArtifactOnlyAsync(CancellationToken ct)
    {
        if (!IsCurrentArtifactOnly())
        {
            return;
        }

        TaskControl.Logger.LogInformation("只拾取圣遗物：万叶拾取后停留 {Seconds} 秒等待拾取",
            ArtifactPickupWaitAfterKazuhaMs / 1000.0);
        await TaskControl.Delay(ArtifactPickupWaitAfterKazuhaMs, ct);
    }

    public static void AddTrigger(PathingPartyConfig? partyConfig)
    {
        switch (Resolve(partyConfig))
        {
            case PathingPickupMode.AutoPick:
                TaskTriggerDispatcher.Instance().AddTrigger("AutoPick");
                break;
            case PathingPickupMode.ArtifactOnly:
                TaskTriggerDispatcher.Instance().AddTrigger("AutoPick",
                    new AutoPickExternalConfig { RuntimeMode = AutoPickRuntimeMode.ArtifactTemplate });
                break;
        }
    }
}
