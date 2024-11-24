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
    
    public async Task DoOnce( CancellationToken ct)
    {
        await _returnMainUiTask.Start(ct);

        await Delay(200, ct);

        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_F1); // F1 开书

        // 找委托按钮
        using var ra = CaptureToRectArea();
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        var ocrList = ra.FindMulti(RecognitionObject.Ocr(0, 0, 360 * assetScale, ra.Height));

        var wt = ocrList.FirstOrDefault(txt => txt.Text.Contains("委托"));

        if (wt != null)
        {
            wt.Click();
            await Delay(1000, ct);
        }
        else
        {
            Logger.LogError("未找到委托按钮");
            return;
        }
        
        // 领取
        using var ra2 = CaptureToRectArea();
        var claimBtn = ra2.Find(ElementAssets.Instance.BtnClaimEncounterPointsRewards);
        if (claimBtn.IsExist())
        {
            claimBtn.Click();
            Logger.LogInformation("领取长效历练点奖励");
            await Delay(1000, ct);
            // TODO 截图并通知
        }
        else
        {
            Logger.LogInformation("未找到领取按钮");
        }
        
        // 关闭
        await _returnMainUiTask.Start(ct);
    }
}