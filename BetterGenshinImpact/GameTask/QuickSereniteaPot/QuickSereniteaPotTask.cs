using System;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.QuickSereniteaPot.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

public class QuickSereniteaPotTask
{
    private static readonly QuickSereniteaPotAssets _assets = QuickSereniteaPotAssets.Instance;

    private static void WaitForBagToOpen()
    {
        var content = TaskControl.CaptureToContent();

        NewRetry.Do(() =>
        {
            TaskControl.Sleep(1);
            var content = TaskControl.CaptureToContent();
            var ra2 = content.CaptureRectArea.Find(_assets.BagCloseButtonRo);
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
            var content = TaskControl.CaptureToContent();
            var ra2 = content.CaptureRectArea.Find(_assets.SereniteaPotIconRo);
            if (ra2.IsEmpty())
            {
                throw new RetryException("未检测到壶");
            }
            else
            {
                ra2.ClickCenter();
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

        try
        {
            var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
            var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            var info = TaskContext.Instance().SystemInfo;

            var clickOffset = new ClickOffset(captureArea.X, captureArea.Y, assetScale);

            // 打开背包
            Simulation.SendInputEx.Keyboard.KeyPress(VK.VK_B);
            TaskControl.CheckAndSleep(500);
            WaitForBagToOpen();

            // 点击道具页
            clickOffset.ClickWithoutScale((int)(1050 * assetScale), (int)(50 * assetScale));
            TaskControl.CheckAndSleep(200);

            // 尝试放置壶
            FindPotIcon();
            TaskControl.CheckAndSleep(200);

            // 点击放置 右下225,60
            clickOffset.ClickWithoutScale(captureArea.Width - (int)(225 * assetScale), captureArea.Height - (int)(60 * assetScale));
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
