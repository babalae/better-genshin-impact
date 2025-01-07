using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 领取长效历练点奖励
/// </summary>
public class ClaimEncounterPointsRewardsTask
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
            Logger.LogDebug(e, "领取长效历练点奖励异常");
            Logger.LogError("领取长效历练点奖励异常: {Msg}", e.Message);
        }
    }

    public async Task DoOnce(CancellationToken ct)
    {
        await _returnMainUiTask.Start(ct);

        await Delay(200, ct);

        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenAdventurerHandbook); // F1 开书

        await Delay(1000, ct);

        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        // 找委托按钮
        var f1Success = await NewRetry.WaitForAction(() =>
        {
            using var ra = CaptureToRectArea();

            var ocrList = ra.FindMulti(RecognitionObject.Ocr(0, 0, 360 * assetScale, ra.Height));

            var wt = ocrList.FirstOrDefault(txt => txt.Text.Contains("委托"));

            if (wt != null)
            {
                wt.Click();
                return true;
            }

            return false;
        }, ct, 5);

        if (!f1Success)
        {
            Logger.LogError("{F}未找到委托按钮,F1打开冒险之证失败", "历练点：");
            return;
        }

        await Delay(1000, ct);

        // 领取
        using var ra2 = CaptureToRectArea();
        var claimBtn = ra2.Find(ElementAssets.Instance.BtnClaimEncounterPointsRewards);
        if (claimBtn.IsExist())
        {
            claimBtn.Click();
            Logger.LogInformation("{F}领取长效历练点奖励", "历练点：");
            await Delay(1000, ct);
            // TODO 截图并通知
        }
        else
        {
            Logger.LogInformation("{F}未找到领取历练点奖励按钮", "历练点：");
        }

        // 关闭
        await _returnMainUiTask.Start(ct);
    }
}