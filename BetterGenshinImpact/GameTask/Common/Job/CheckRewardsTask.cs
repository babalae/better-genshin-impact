using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;


namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 检查奖励并通知的任务
/// </summary>
public class CheckRewardsTask
{
    private readonly ILogger<CheckRewardsTask> _logger = App.GetLogger<CheckRewardsTask>();

    public string Name => "检查奖励并通知的任务";

    public async Task Start(CancellationToken ct)
    {
        try
        {
            await new ReturnMainUiTask().Start(ct);
            Simulation.SendInput.SimulateAction(GIActions.OpenAdventurerHandbook); // F1 开书
            await Delay(2000, ct);
            // OCR识别每日是否完成
            var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            using var ra = CaptureToRectArea();
            var ocrList = ra.FindMulti(RecognitionObject.Ocr(0, ra.Height - ra.Height / 3.0, 730 * assetScale, ra.Height / 3.0));
            var done = ocrList.FirstOrDefault(txt => txt.Text.Contains("今日奖励已领取"));
            if (done != null)
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