using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class WalkToFTask
{
    /// <summary>
    /// 行走直到F出现
    /// </summary>
    /// <param name="ct"></param>
    /// <param name="timeoutMilliseconds">超时时间</param>
    /// <param name="runToF">是否冲刺</param>
    /// <returns></returns>
    public async Task<bool> Start(CancellationToken ct, bool needPress = true, bool runToF = false, int timeoutMilliseconds = 30000)
    {
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        await Delay(30, ct);
        // 组合键好像不能直接用 postmessage
        if (runToF)
        {
            Simulation.SendInput.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyDown);
        }

        try
        {
            bool res = await NewRetry.WaitForElementAppear(AutoPickAssets.Instance.PickRo, null, ct, timeoutMilliseconds / 100 + 1, 100);
            if (res)
            {
                if (needPress)
                {
                    Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
                }

                Logger.LogInformation(Lang.S["GameTask_10423_d4417d"]);
            }
            else
            {
                Logger.LogWarning(Lang.S["GameTask_11652_ea2c51"]);
            }

            return res;
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            Sleep(50);
            if (runToF)
            {
                Simulation.SendInput.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyUp);
            }
        }
    }
}