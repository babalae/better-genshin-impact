using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取邮件奖励
/// </summary>
public class ClaimMailRewardsTask
{
    private readonly ReturnMainUiTask _returnMainUiTask = new();

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

        await Delay(200, ct);

        TaskContext.Instance().PostMessageSimulator.KeyPress(User32.VK.VK_ESCAPE); // ESC 

        await Delay(1300, ct);

        var ra = CaptureToRectArea();
        var mailIcon = ra.Find(ElementAssets.Instance.EscMailReward);
        if (mailIcon.IsExist())
        {
            mailIcon.Click();
            await Delay(1000, ct);
            ra = CaptureToRectArea();
            var claimAll = ra.Find(ElementAssets.Instance.CollectRo);
            if (claimAll.IsExist())
            {
                claimAll.Click();
                Logger.LogInformation("邮件：{Text}", "全部领取");
                await Delay(200, ct);
                // TODO 截图
                
                TaskContext.Instance().PostMessageSimulator.KeyPress(User32.VK.VK_ESCAPE); // ESC 
            }
        }
        else
        {
            Logger.LogInformation("邮件：{Text}", "没有邮件奖励");
        }

        ra.Dispose();

        // 关闭
        await _returnMainUiTask.Start(ct);
    }
}