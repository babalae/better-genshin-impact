using BetterGenshinImpact.Core.Input;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取邮件奖励
/// </summary>
public class ClaimMailRewardsTask
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 返回主界面子任务
    /// </summary>
    private readonly ReturnMainUiTask _returnMainUiTask;

    /// <summary>
    /// 创建领取邮件奖励任务
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    public ClaimMailRewardsTask(GameRuntime runtime)
    {
        _runtime = runtime;
        _returnMainUiTask = new ReturnMainUiTask(runtime);
    }

    public async Task Start(CancellationToken ct)
    {
        try
        {
            await DoOnce(ct);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "领取邮件奖励异常");
            Logger.LogError("领取邮件奖励异常: {Msg}", e.Message);
        }
    }

    public async Task DoOnce(CancellationToken ct)
    {
        await _returnMainUiTask.Start(ct);

        await _runtime.Delay(200, ct);

        InputHub.Background.SimulateAction(GIActions.OpenPaimonMenu); // ESC 

        await _runtime.Delay(1300, ct);

        using (var ra = _runtime.CaptureToRectArea())
        {
            var mailIcon = ra.Find(ElementRecognition.Get("EscMailReward", ra));
            if (mailIcon.IsExist())
            {
                mailIcon.Click();
                await _runtime.Delay(1000, ct);

                using var claimArea = _runtime.CaptureToRectArea();
                var claimAll = claimArea.Find(ElementRecognition.Get("Collect", claimArea));
                if (claimAll.IsExist())
                {
                    claimAll.Click();
                    Logger.LogInformation("邮件：{Text}", "全部领取");
                    await _runtime.Delay(200, ct);
                    // TODO 截图

                    InputHub.Background.Keyboard.KeyPress(User32.VK.VK_ESCAPE); // ESC 
                }
            }
            else
            {
                Logger.LogInformation("邮件：{Text}", "没有邮件奖励");
            }
        }

        // 关闭
        await _returnMainUiTask.Start(ct);
    }
}
