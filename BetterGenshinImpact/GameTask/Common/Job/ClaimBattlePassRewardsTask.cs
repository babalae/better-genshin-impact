using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 一键领取纪行
/// </summary>
public class ClaimBattlePassRewardsTask
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
            Logger.LogDebug(e, "领取纪行奖励异常");
            Logger.LogError("领取纪行奖励异常: {Msg}", e.Message);
        }
    }

    public async Task DoOnce(CancellationToken ct)
    {
        await _returnMainUiTask.Start(ct);

        await Delay(200, ct);
        TaskContext.Instance().PostMessageSimulator.KeyPress(User32.VK.VK_F4); // F4 开纪行


        await Delay(1000, ct);
        GameCaptureRegion.GameRegion1080PPosClick(960, 45); // 点中间
        await Delay(500, ct);
        ClaimAll();
        
        await Delay(500, ct);
        GameCaptureRegion.GameRegion1080PPosClick(858, 45);
        await Delay(500, ct);
        ClaimAll();

        // 关闭
        await _returnMainUiTask.Start(ct);
    }

    /// <summary>
    /// 一键领取
    /// </summary>
    /// <returns></returns>
    private static bool ClaimAll()
    {
        using var ra = CaptureToRectArea();
        var ocrList = ra.FindMulti(RecognitionObject.Ocr(ra.ToRect().CutRightBottom(0.3, 0.18)));
        var wt = ocrList.FirstOrDefault(txt => txt.Text.Contains("一键"));
        if (wt != null)
        {
            wt.Click();
            Logger.LogInformation("纪行：{Text}", "一键领取");
            return true;
        }
        else
        {
            Logger.LogInformation("纪行：{Text}", "无需领取");
            return false;
        }
    }
}