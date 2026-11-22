using BetterGenshinImpact.GameTask.Common.BgiVision;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
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

    internal static bool IsClaimTime()
    {
        var time = ServerTimeHelper.GetServerTimeNow().AddMinutes(5);
        return time.Hour == 4 && time.Minute < 10;
    }

    internal static bool IsClaimScreen(ImageRegion screen)
    {
        if (Bv.IsInBlessingOfTheWelkinMoon(screen)) return true;
        using var primogem = screen.Find(ElementRecognition.Get("Primogem", screen));
        return primogem.IsExist();
    }

    public async Task Start(CancellationToken ct)
    {
        try
        {
            if (IsClaimTime())
            {
                using var ra = CaptureToRectArea();
                if (IsClaimScreen(ra))
                {
                    Logger.LogInformation("检测到空月祝福界面，自动点击");
                    GameCaptureRegion.GameRegion1080PPosMove(100, 100);
                    for (int i = 0, j = 0; i < 20 && j < 3; ++i)
                    {
                        if (j == 0)
                        {
                            // 双击快速跳过
                            GameCaptureRegion.GameRegion1080PPosClick(100, 100);
                            await Task.Delay(100, ct);
                            GameCaptureRegion.GameRegion1080PPosClick(100, 100);
                        }
                        await Delay(500, ct);
                        using var ra2 = CaptureToRectArea();
                        if (IsClaimScreen(ra2))
                        {
                            j = 0;
                        }
                        else
                        {
                            // 连续3次没检测到才认为处理完毕，避免淡出/淡入特效影响
                            ++j;
                        }
                    }
                    Logger.LogInformation("空月祝福处理完毕");
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogError("月卡判断异常：" + e.Message);
        }
    }
}
