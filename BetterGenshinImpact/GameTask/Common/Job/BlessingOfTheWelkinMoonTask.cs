using BetterGenshinImpact.GameTask.Common.BgiVision;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 跳过月卡
/// </summary>
public class BlessingOfTheWelkinMoonTask
{
    public string Name => "自动点击空月祝福";

    public async Task Start(CancellationToken ct)
    {
        try
        {
            // 4点全程触发
            if (ServerTimeHelper.GetServerTimeNow().Hour == 4)
            {
                using var ra = CaptureToRectArea();
                if (Bv.IsInBlessingOfTheWelkinMoon(ra))
                {
                    Logger.LogInformation("检测到空月祝福界面，自动点击");
                    GameCaptureRegion.GameRegion1080PPosMove(100,100);
                    TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
                    await Delay(5000, ct);

                    // 重新判断一次，因为界面刚出来的点击可能无效
                    if (Bv.IsInBlessingOfTheWelkinMoon(ra))
                    {
                        TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
                        await Delay(5000, ct);
                    }

                    await Delay(2000, ct);

                    TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();

                    await Delay(2000, ct);

                    TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogError("月卡判断异常：" + e.Message);
        }
    }
}
