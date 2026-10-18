using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using System;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

public class QuickSereniteaPotTask
{
    /// <summary>
    /// 等待背包界面打开
    /// </summary>
    /// <param name="runtime">本次操作使用的运行环境</param>
    private static void WaitForBagToOpen(GameRuntime runtime)
    {
        NewRetry.Do(() =>
        {
            runtime.Sleep(1);
            using var ra1 = runtime.CaptureToRectArea();
            using var ra2 = ra1.Find(RecognitionAssets.Get("QuickTeleport", "MapCloseButton", ra1));
            if (ra2.IsEmpty())
            {
                throw new RetryException("背包未打开");
            }
        }, TimeSpan.FromMilliseconds(500), 5);
    }

    /// <summary>
    /// 在背包中找到尘歌壶图标并点击
    /// </summary>
    /// <param name="runtime">本次操作使用的运行环境</param>
    private static void FindPotIcon(GameRuntime runtime)
    {
        NewRetry.Do(() =>
        {
            runtime.Sleep(1);
            using var ra1 = runtime.CaptureToRectArea();
            using var ra2 = ra1.Find(RecognitionAssets.Get("QuickSereniteaPot", "SereniteaPotIcon", ra1));
            if (ra2.IsEmpty())
            {
                throw new RetryException("未检测到壶");
            }
            else
            {
                ra2.Click();
            }
        }, TimeSpan.FromMilliseconds(200), 3);
    }

    /// <summary>
    /// 快速进出尘歌壶。热键与任务都可以调用；运行环境是否存在由调用方判断
    /// </summary>
    /// <param name="runtime">本次操作使用的运行环境，截图、等待与点击都只使用它</param>
    public static void Done(GameRuntime runtime)
    {
        if (!runtime.IsOperable())
        {
            return;
        }

        try
        {
            // 打开背包
            InputHub.Foreground.SimulateAction(GIActions.OpenInventory);
            runtime.CheckAndSleep(500);
            WaitForBagToOpen(runtime);

            // 点击道具页
            runtime.GameRegion1080PPosClick(1050, 50);
            runtime.CheckAndSleep(200);

            // 尝试放置壶
            FindPotIcon(runtime);
            runtime.CheckAndSleep(200);

            // 点击放置 右下225,60
            // GameCaptureRegion.GameRegionClick((size, assetScale) => (size.Width - 225 * assetScale, size.Height - 60 * assetScale));
            // 也可以使用下面的方法点击放置按钮
            using (var confirmCapture = runtime.CaptureToRectArea())
            {
                Bv.ClickWhiteConfirmButton(confirmCapture);
            }
            runtime.CheckAndSleep(800);
            // 校验是否部署成功
            var seccess = false;
            for (int i = 0; i < 5; i++)
            {
                using var mainUiCapture = runtime.CaptureToRectArea();
                if (Bv.IsInMainUi(mainUiCapture))
                {
                    seccess = true;
                    break;
                }
            }
            if (!seccess) {
                for (int i = 0; i < 5; ++i)
                {
                    using var bigMapCapture = runtime.CaptureToRectArea();
                    if (!Bv.IsInBigMapUi(bigMapCapture))
                    {
                        InputHub.Foreground.SimulateAction(GIActions.OpenInventory);
                    }
                    else
                    {
                        return;
                    }
                }
            }
            // 校验F交互是否是 进入/离开[尘歌壶] 
            using var capture = runtime.CaptureToRectArea();
            bool isEnter = Bv.FindF(capture, "进入", "尘歌壶");
            bool isLeave = Bv.FindF(capture, "离开", "尘歌壶");

            if (isEnter || isLeave) {
                string action = isEnter ? "进入" : "离开";
                TaskControl.Logger.LogInformation($"快速进出尘歌壶:识别到 {action}尘歌壶");
                
                // 按F触发交互
                InputHub.Foreground.SimulateAction(GIActions.PickUpOrInteract);
                TaskControl.Logger.LogInformation($"快速进出尘歌壶:F{action}尘歌壶");
                runtime.CheckAndSleep(200);
                // 点击进入/离开尘歌壶
                // 如果不是联机状态，此时玩家应已进入传送界面，本次点击不会影响实际功能
                runtime.GameRegion1080PPosClick(1010, 760);
            }
            else
            {
                TaskControl.Logger.LogInformation("快速进出尘歌壶:未识别到 进入或离开尘歌壶");
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e.Message);
        }
        finally
        {
            runtime.MaskWindowDrawingBoard.ClearAll();
        }
    }
}
