using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;


namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 检查奖励并通知的任务
/// </summary>
public class CheckRewardsTask
{
    private readonly ILogger<CheckRewardsTask> _logger = App.GetLogger<CheckRewardsTask>();

    private readonly string _dailyRewardsClaimedLocalizedString;

    public CheckRewardsTask()
    {
        IStringLocalizer<CheckRewardsTask> stringLocalizer = App.GetService<IStringLocalizer<CheckRewardsTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this._dailyRewardsClaimedLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, Lang.S["GameTask_11502_dabda7"]);
    }

    public string Name => Lang.S["GameTask_11504_6f82da"];
    
    private static RecognitionObject GetConfirmRa(bool isOcrMatch = false,params string[] targetText)
    {
        var screenArea = CaptureToRectArea();
        var x = (int)(screenArea.Width * 0.1);
        var y = (int)(screenArea.Height * 0.1);
        var width = (int)(screenArea.Width * 0.3);
        var height = (int)(screenArea.Height * 0.7);
        
        return isOcrMatch ? RecognitionObject.OcrMatch(x, y, width, height, targetText) : 
            RecognitionObject.Ocr(x, y, width, height);
    }

    public async Task Start(CancellationToken ct)
    {
        try
        {
            await new ReturnMainUiTask().Start(ct);
            
            _ = await NewRetry.WaitForElementAppear(
                GetConfirmRa(true,Lang.S["GameTask_11503_514e8c"]),
                ()=>
                {
                    Simulation.SendInput.SimulateAction(GIActions.OpenAdventurerHandbook); 
                    var screen = CaptureToRectArea();
                    var ra = screen.FindMulti(GetConfirmRa())
                        .FirstOrDefault(btn => btn.Text == Lang.S["GameTask_11242_f6b0bb"]);
                        ra?.Click();
                },ct,4,1000);
            
            // OCR识别每日是否完成
            var done = await NewRetry.WaitForElementAppear(
                GetConfirmRa(true,_dailyRewardsClaimedLocalizedString),null,
                ct,4,500);
            if (done)
            {
                Logger.LogInformation(Lang.S["GameTask_11501_04ba62"], "今日奖励已领取");
                Notify.Event(NotificationEvent.DailyReward).Success(Lang.S["GameTask_11500_a4c3c1"]);
            }
            else
            {
                Logger.LogWarning(Lang.S["GameTask_11498_5e929c"], "未领取");
                Notify.Event(NotificationEvent.DailyReward).Error(Lang.S["GameTask_11497_b0eaaf"]);
            }
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, Lang.S["GameTask_11496_86ad36"]);
            Logger.LogError(Lang.S["GameTask_11495_a35e77"], e.Message);
        }
    }
}