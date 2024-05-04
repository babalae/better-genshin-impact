using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Windows;

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
            // 点击购买/兑换 右下225x60
            GameCaptureRegion.GameRegionClick((size, scale) => (size.Width - 225 * scale, size.Height - 60 * scale));
            TaskControl.CheckAndSleep(100); // 等待窗口弹出

            // 选中左边点 742x601
            GameCaptureRegion.GameRegion1080PPosMove(742, 601);
            TaskControl.CheckAndSleep(100);
            Simulation.SendInputEx.Mouse.LeftButtonDown();
            TaskControl.CheckAndSleep(50);

            // 向右滑动
            Simulation.SendInputEx.Mouse.MoveMouseBy(1000, 0);
            TaskControl.CheckAndSleep(200);
            Simulation.SendInputEx.Mouse.LeftButtonUp();
            TaskControl.CheckAndSleep(100);

            // 点击弹出页的购买/兑换 1100x780
            GameCaptureRegion.GameRegion1080PPosClick(1100, 780);
            TaskControl.CheckAndSleep(200); // 等待窗口消失
            GameCaptureRegion.GameRegionClick((size, scale) => (size.Width - 225 * scale, size.Height - 60 * scale));
            TaskControl.CheckAndSleep(200);
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e.Message);
        }
    }
}
