using System;
using System.Windows;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.QucikBuy;

public class QuickBuyTask
{
    public static void Done()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            MessageBox.Show("请先启动");
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

            var clickOffset = new ClickOffset(captureArea.X, captureArea.Y, assetScale);

            // 点击购买/兑换 右下225x60
            clickOffset.ClickWithoutScale(captureArea.Width - (int)(225 * assetScale), captureArea.Height - (int)(60 * assetScale));
            TaskControl.CheckAndSleep(100); // 等待窗口弹出

            // 选中左边点 742x601
            clickOffset.Move(742, 601);
            TaskControl.CheckAndSleep(100);
            Simulation.SendInputEx.Mouse.LeftButtonDown();
            TaskControl.CheckAndSleep(50);

            // 向右滑动
            Simulation.SendInputEx.Mouse.MoveMouseBy(1000, 0);
            TaskControl.CheckAndSleep(200);
            Simulation.SendInputEx.Mouse.LeftButtonUp();
            TaskControl.CheckAndSleep(100);

            // 点击弹出页的购买/兑换 1100x780
            clickOffset.Click(1100, 780);
            TaskControl.CheckAndSleep(200); // 等待窗口消失
            clickOffset.ClickWithoutScale(captureArea.Width - (int)(225 * assetScale), captureArea.Height - (int)(60 * assetScale));
            TaskControl.CheckAndSleep(200);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e.Message);
        }
    }
}
