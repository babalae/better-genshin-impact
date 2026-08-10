using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.Common.Party;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// 不启动完整路线会话的局部移动能力。
/// </summary>
public interface ILocalMovementService
{
    Task MoveToAsync(WaypointForTrack waypoint);
}

public sealed class LocalMovementService : ILocalMovementService
{
    private readonly PathingMovementController _movementController;

    public LocalMovementService(CancellationToken ct)
    {
        var rotateTask = new CameraRotateTask(ct);
        var navigator = new PathingNavigator(ct, _ => Task.CompletedTask);
        var partyConfig = new PathingPartyConfig
        {
            AutoRunEnabled = false,
            AutoFightEnabled = false,
            AutoSkipEnabled = false
        };

        _movementController = new PathingMovementController(
            ct,
            navigator,
            rotateTask,
            new TrapEscaper(ct),
            NoOpPathingSuspendState.Instance,
            new PathingMovementActions
            {
                CaptureAction = () => CaptureToRectArea(),
                EndJudgmentAction = _ => false,
                ResolveAnomaliesAction = _ => Task.CompletedTask,
                WaitUntilRotatedToAction = async (orientation, maxDiff) =>
                {
                    await rotateTask.WaitUntilRotatedTo(orientation, maxDiff);
                },
                SwitchAvatarAction = _ => Task.FromResult<Avatar?>(null),
                UseElementalSkillAction = () => Task.CompletedTask,
                PartyConfigGetter = () => partyConfig
            });
    }

    public async Task MoveToAsync(WaypointForTrack waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        await _movementController.MoveTo(waypoint);
    }
}
