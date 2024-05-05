using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickSereniteaPot.Assets;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

public class QuickSereniteaPotTask
{
    private static void WaitForBagToOpen()
    {
        NewRetry.Do(() =>
        {
            TaskControl.Sleep(1);
            using var ra2 = TaskControl.CaptureToRectArea().Find(QuickSereniteaPotAssets.Instance.BagCloseButtonRo);
            if (ra2.IsEmpty())
            {
                throw new RetryException("背包未打开");
            }
        }, TimeSpan.FromMilliseconds(500), 3);
    }

    private static void FindPotIcon()
    {
        NewRetry.Do(() =>
        {
            TaskControl.Sleep(1);
            using var ra2 = TaskControl.CaptureToRectArea().Find(QuickSereniteaPotAssets.Instance.SereniteaPotIconRo);
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
            System.Windows.MessageBox.Show("请先启动");
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
            Simulation.SendInputEx.Keyboard.KeyPress(VK.VK_B);
            TaskControl.CheckAndSleep(500);
            WaitForBagToOpen();

            // 点击道具页
            GameCaptureRegion.GameRegion1080PPosClick(1050, 50);
            TaskControl.CheckAndSleep(200);

            // 尝试放置壶
            FindPotIcon();
            TaskControl.CheckAndSleep(200);

            // 点击放置 右下225,60
            GameCaptureRegion.GameRegionClick((size, assetScale) => (size.Width - 225 * assetScale, size.Height - 60 * assetScale));
            // 也可以使用下面的方法点击放置按钮
            // Bv.ClickWhiteConfirmButton(TaskControl.CaptureToRectArea());
            TaskControl.CheckAndSleep(800);

            // 按F进入
            Simulation.SendInputEx.Keyboard.KeyPress(VK.VK_F);
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
