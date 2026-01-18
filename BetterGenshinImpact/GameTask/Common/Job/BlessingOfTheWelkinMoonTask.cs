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

    public async Task Start(CancellationToken ct)
    {
        try
        {
            var t = ServerTimeHelper.GetServerTimeNow().AddMinutes(5);
            if (t.Hour == 4 && t.Minute < 10)
            {
                using var ra = CaptureToRectArea();
                if (Bv.IsInBlessingOfTheWelkinMoon(ra) || ra.Find(ElementAssets.Instance.PrimogemRo).IsExist())
                {
                    Logger.LogInformation("检测到空月祝福界面，自动点击");
                    GameCaptureRegion.GameRegion1080PPosMove(100, 100);
                    for (int i = 0, j = 0; i < 20 && j < 3; ++i)
                    {
                        if (j == 0)
                        {
                            // 双击快速跳过
                            TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
                            TaskContext.Instance().PostMessageSimulator.LeftButtonClickBackground();
                        }
                        await Delay(500, ct);
                        using var ra2 = CaptureToRectArea();
                        if (Bv.IsInBlessingOfTheWelkinMoon(ra2))
                        {
                            // 仍在空月祝福界面
                            j = 0;
                        }
                        else if (ra2.Find(ElementAssets.Instance.PrimogemRo).IsExist())
                        {
                            // 仍在原石界面
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
