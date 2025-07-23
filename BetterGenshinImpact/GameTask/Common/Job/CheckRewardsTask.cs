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
/// ��齱����֪ͨ������
/// </summary>
public class CheckRewardsTask
{
    private readonly ILogger<CheckRewardsTask> _logger = App.GetLogger<CheckRewardsTask>();

    private readonly string _dailyRewardsClaimedLocalizedString;

    public CheckRewardsTask()
    {
        IStringLocalizer<CheckRewardsTask> stringLocalizer = App.GetService<IStringLocalizer<CheckRewardsTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this._dailyRewardsClaimedLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "���ս�������ȡ");
    }

    public string Name => "��齱����֪ͨ������";
    
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
                GetConfirmRa(true,"ÿ��ί�н���"),
                ()=>
                {
                    Simulation.SendInput.SimulateAction(GIActions.OpenAdventurerHandbook); 
                    var screen = CaptureToRectArea();
                    var ra = screen.FindMulti(GetConfirmRa())
                        .FirstOrDefault(btn => btn.Text == "ί��");
                        ra?.Click();
                },ct,4,1000);
            
            // OCRʶ��ÿ���Ƿ����
            var done = await NewRetry.WaitForElementAppear(
                GetConfirmRa(true,_dailyRewardsClaimedLocalizedString),null,
                ct,4,500);
            if (done)
            {
                Logger.LogInformation("���ÿ�ս��������{Msg}", "���ս�������ȡ");
                Notify.Event(NotificationEvent.DailyReward).Success("notification.message.dailyRewardClaimed");
            }
            else
            {
                Logger.LogWarning("���ÿ�ս��������{Msg}�����ֶ���飡", "δ��ȡ");
                Notify.Event(NotificationEvent.DailyReward).Error("notification.message.dailyRewardUnclaimed");
            }
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "��齱����֪ͨ�������쳣");
            Logger.LogError("��齱����֪ͨ�������쳣: {Msg}", e.Message);
        }
    }
}
