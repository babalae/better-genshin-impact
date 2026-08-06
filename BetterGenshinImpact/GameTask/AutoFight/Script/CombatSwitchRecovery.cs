using System;
using System.Threading;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common.Party;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

/// <summary>
/// Combat-specific recovery for a failed avatar switch.
/// </summary>
public static class CombatSwitchRecovery
{
    private static readonly GIActions[] Directions =
    [
        GIActions.MoveForward,
        GIActions.MoveBackward,
        GIActions.MoveLeft,
        GIActions.MoveRight
    ];

    public static void Switch(Avatar avatar, CancellationToken ct)
    {
        if (TrySwitch(avatar, ct))
        {
            return;
        }

        throw new RetryException($"战斗中切换角色失败：{avatar.Name}");
    }

    public static bool TrySwitch(Avatar avatar, CancellationToken ct, int tryTimes = 10)
    {
        if (avatar.TrySwitch(tryTimes))
        {
            return true;
        }

        var direction = Directions[System.Random.Shared.Next(Directions.Length)];
        Logger.LogWarning("战斗中切换角色卡住，由战斗执行器执行脱困（方向：{Direction}）", direction);
        Simulation.SendInput.SimulateAction(GIActions.Jump);
        Sleep(200, ct);
        Simulation.SendInput.SimulateAction(direction, KeyType.KeyDown);
        avatar.TrySwitch(1);
        Sleep(1000, ct);
        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
        Simulation.ReleaseAllKey();

        return avatar.TrySwitch(tryTimes);
    }
}
