using BetterGenshinImpact.GameTask.Common.BgiVision;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 跳过月卡
/// </summary>
public class BlessingOfTheWelkinMoonTask
{
    public string Name => "自动点击空月祝福";

    private DateTime _lastRunTime = DateTime.MinValue;

    private bool _retry = false;

    public async Task Start(CancellationToken ct)
    {
        try
        {
            // 4点分界线触发一次
            if ((DateTime.Now.Hour == 4 && _lastRunTime.Date.Hour == 3) || _retry)
            {
                _retry = true;
                using var ra = CaptureToRectArea();
                if (Bv.IsInBlessingOfTheWelkinMoon(ra))
                {
                    Logger.LogInformation("检测到空月祝福界面，自动点击");
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

            _lastRunTime = DateTime.Now;
        }
        catch (Exception e)
        {
            Logger.LogError("月卡判断异常：" + e.Message);
        }
    }
}
