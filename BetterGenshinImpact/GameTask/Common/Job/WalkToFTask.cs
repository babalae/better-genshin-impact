using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class WalkToFTask
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 创建行走直到交互键出现的任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public WalkToFTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
    }

    /// <summary>
    /// 行走直到F出现
    /// </summary>
    /// <param name="ct"></param>
    /// <param name="timeoutMilliseconds">超时时间</param>
    /// <param name="runToF">是否冲刺</param>
    /// <returns></returns>
    public async Task<bool> Start(CancellationToken ct, bool needPress = true, bool runToF = false, int timeoutMilliseconds = 30000)
    {
        InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        await _runtime.Delay(30, ct);
        // 组合键好像不能直接用 postmessage
        if (runToF)
        {
            InputHub.Foreground.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyDown);
        }

        try
        {
            AutoPickAssets pickAssets;
            using (var gameCaptureRegion = _runtime.CaptureToRectArea())
            {
                pickAssets = AutoPickAssets.Get(gameCaptureRegion, _config.AutoPickConfig.PickKey);
            }
            bool res = await NewRetry.WaitForElementAppear(_runtime, pickAssets.PickRo, null, ct, timeoutMilliseconds / 100 + 1, 100);
            if (res)
            {
                if (needPress)
                {
                    InputHub.Foreground.Keyboard.KeyPress(pickAssets.PickVk);
                }

                Logger.LogInformation("检测到交互键");
            }
            else
            {
                Logger.LogWarning("前往目标[F]超时！");
            }

            return res;
        }
        finally
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            _runtime.Sleep(50);
            if (runToF)
            {
                InputHub.Foreground.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyUp);
            }
        }
    }
}
