using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickSereniteaPot.Assets;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using Wpf.Ui.Violeta.Controls;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

public class QuickSereniteaPotTask
{
    private static void WaitForBagToOpen()
    {
        NewRetry.Do(() =>
        {
            TaskControl.Sleep(1);
            using var ra2 = TaskControl.CaptureToRectArea(forceNew: true).Find(QuickSereniteaPotAssets.Instance.BagCloseButtonRo);
            if (ra2.IsEmpty())
            {
                throw new RetryException("背包未打开");
            }
        }, TimeSpan.FromMilliseconds(500), 5);
    }

    private static void FindPotIcon()
    {
        NewRetry.Do(() =>
        {
            TaskControl.Sleep(1);
            using var ra2 = TaskControl.CaptureToRectArea(forceNew: true).Find(QuickSereniteaPotAssets.Instance.SereniteaPotIconRo);
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

    public static void Done()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            Toast.Warning("请先启动");
            return;
        }

        if (!SystemControl.IsGenshinImpactActiveByProcess())
        {
            return;
        }

        QuickSereniteaPotAssets.DestroyInstance();

        try
        {
            // 打开背包
            Simulation.SendInput.SimulateAction(GIActions.OpenInventory);
            TaskControl.CheckAndSleep(500);
            WaitForBagToOpen();

            // 点击道具页
            GameCaptureRegion.GameRegion1080PPosClick(1050, 50);
            TaskControl.CheckAndSleep(200);

            // 尝试放置壶
            FindPotIcon();
            TaskControl.CheckAndSleep(200);

            // 点击放置 右下225,60
            // GameCaptureRegion.GameRegionClick((size, assetScale) => (size.Width - 225 * assetScale, size.Height - 60 * assetScale));
            // 也可以使用下面的方法点击放置按钮
            Bv.ClickWhiteConfirmButton(TaskControl.CaptureToRectArea());
            TaskControl.CheckAndSleep(800);
            // 校验是否部署成功
            var seccess = false;
            for (int i = 0; i < 5; i++)
            {
                if (Bv.IsInMainUi(TaskControl.CaptureToRectArea()))
                {
                    seccess = true;
                    break;
                }
            }
            if (!seccess) {
                for (int i = 0; i < 5; ++i)
                {
                    if (!Bv.IsInBigMapUi(TaskControl.CaptureToRectArea()))
                    {
                        Simulation.SendInput.SimulateAction(GIActions.OpenInventory);
                    }
                    else
                    {
                        return;
                    }
                }
            }
            // 校验F交互是否是 进入[尘歌壶]
            bool canIn = Bv.FindF(TaskControl.CaptureToRectArea(), "进入","尘歌壶");

            if (canIn) {
                TaskControl.Logger.LogInformation("快速进入尘歌壶:识别到 进入尘歌壶");
                // 按F进入
                Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
                TaskControl.Logger.LogInformation("快速进入尘歌壶:F进入尘歌壶");
                TaskControl.CheckAndSleep(200);
                // 点击进入尘歌壶
                // 如果不是联机状态，此时玩家应已进入传送界面，本次点击不会影响实际功能
                GameCaptureRegion.GameRegion1080PPosClick(1010, 760);
            }
            else
            {
                TaskControl.Logger.LogInformation("快速进入尘歌壶:未识别到 进入尘歌壶");
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
        }
    }
}
