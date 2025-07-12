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
        this._dailyRewardsClaimedLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "今日奖励已领取");
    }

    public string Name => "检查奖励并通知的任务";
    
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
                GetConfirmRa(true,"每日委托奖励"),
                ()=>
                {
                    Simulation.SendInput.SimulateAction(GIActions.OpenAdventurerHandbook); 
                    var screen = CaptureToRectArea();
                    var ra = screen.FindMulti(GetConfirmRa())
                        .FirstOrDefault(btn => btn.Text == "委托");
                        ra?.Click();
                },ct,4,1000);
            
            // OCR识别每日是否完成
            var done = await NewRetry.WaitForElementAppear(
                GetConfirmRa(true,_dailyRewardsClaimedLocalizedString),null,
                ct,4,500);
            if (done)
            {
                Logger.LogInformation("检查每日奖励结果：{Msg}", "今日奖励已领取");
                Notify.Event(NotificationEvent.DailyReward).Success("检查每日奖励：已领取");
            }
            else
            {
                Logger.LogWarning("检查每日奖励结果：{Msg}，请手动检查！", "未领取");
                Notify.Event(NotificationEvent.DailyReward).Error("检查到每日奖励未领取，请手动查看！");
            }
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "检查奖励并通知的任务异常");
            Logger.LogError("检查奖励并通知的任务异常: {Msg}", e.Message);
        }
    }
}