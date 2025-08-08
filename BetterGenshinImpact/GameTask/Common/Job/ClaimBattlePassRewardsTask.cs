using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using Microsoft.Extensions.Localization;
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

    private readonly string[] claimAllLocalizedStrings;

    public ClaimBattlePassRewardsTask()
    {
        IStringLocalizer<ClaimBattlePassRewardsTask> stringLocalizer = App.GetService<IStringLocalizer<ClaimBattlePassRewardsTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this.claimAllLocalizedStrings = ((string[])["一键", "领取"]).Select(i => stringLocalizer.WithCultureGet(cultureInfo, i)).ToArray();
    }

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
        TaskContext.Instance().PostMessageSimulator.SimulateAction(GIActions.OpenBattlePassScreen); // F4 开纪行

        // 领取战令1
        await Delay(1000, ct);
        await ClaimAll(ct);


        // 领取点数
        await Delay(1000, ct);
        GameCaptureRegion.GameRegion1080PPosClick(960, 45); // 点中间
        await Delay(500, ct);
        await ClaimAll(ct);

        // 领取战令2
        await Delay(2500, ct); // 等待升级动画
        // 还可能存在领取到原石的情况
        if (CaptureToRectArea().Find(ElementAssets.Instance.PrimogemRo).IsExist())
        {
            TaskContext.Instance().PostMessageSimulator.KeyPress(User32.VK.VK_ESCAPE);
        }
        GameCaptureRegion.GameRegion1080PPosClick(858, 45);
        await Delay(1500, ct);
        await ClaimAll(ct);

        // 关闭
        await _returnMainUiTask.Start(ct);
    }

    /// <summary>
    /// 一键领取
    /// </summary>
    /// <returns></returns>
    private async Task<bool> ClaimAll(CancellationToken ct)
    {
        using var ra = CaptureToRectArea();
        var ocrList = ra.FindMulti(RecognitionObject.Ocr(ra.ToRect().CutRightBottom(0.3, 0.2)));
        var wt = ocrList.FirstOrDefault(txt => this.claimAllLocalizedStrings.Any(i => Regex.IsMatch(txt.Text, i)));
        Debug.WriteLine(this.claimAllLocalizedStrings);
        if (wt != null)
        {
            wt.Click();
            Logger.LogInformation("纪行：{Text}", "一键领取");
            await Delay(1000, ct);
            using var ra2 = CaptureToRectArea();
            if (ra2.Find(ElementAssets.Instance.PrimogemRo).IsExist())
            {
                TaskContext.Instance().PostMessageSimulator.KeyPress(User32.VK.VK_ESCAPE);
            }

            return true;
        }
        else
        {
            Logger.LogInformation("纪行：{Text}", "无需领取");
            return false;
        }
    }
}