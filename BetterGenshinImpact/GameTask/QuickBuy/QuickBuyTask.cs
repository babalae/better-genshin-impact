using System;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickBuy.Assets;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.QuickBuy;

public class QuickBuyTask
{
    private static readonly ILogger<QuickBuyTask> _logger = App.GetLogger<QuickBuyTask>();

    
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

        try
        {
            ImageRegion ra = TaskControl.CaptureToRectArea();
            if (ra.Find(QuickBuyAssets.Instance.SereniteaPotCoin).IsExist())
            {
                // _logger.LogInformation("触发尘歌壶快速购买逻辑");
                // 尘歌壶购买逻辑
                // GameCaptureRegion.GameRegionClick((size, scale) => (200 * scale, 200 * scale));
                // TaskControl.CheckAndSleep(100);
                // 选中左边点 
                GameCaptureRegion.GameRegion1080PPosMove(1450, 690);
                TaskControl.CheckAndSleep(100);
                Simulation.SendInput.Mouse.LeftButtonDown();
                TaskControl.CheckAndSleep(50);

                // 向右滑动
                Simulation.SendInput.Mouse.MoveMouseBy(1000, 0);
                TaskControl.CheckAndSleep(200);
                Simulation.SendInput.Mouse.LeftButtonUp();
                TaskControl.CheckAndSleep(200);

                GameCaptureRegion.GameRegion1080PPosClick(1600, 1020);
                TaskControl.CheckAndSleep(200); // 等待窗口消失
                GameCaptureRegion.GameRegion1080PPosClick(960, 850);

                return;
            }
            // 点击购买/兑换 右下225x60
            GameCaptureRegion.GameRegionClick((size, scale) => (size.Width - 225 * scale, size.Height - 60 * scale));
            TaskControl.CheckAndSleep(100); // 等待窗口弹出

            // 选中左边点 742x601
            GameCaptureRegion.GameRegion1080PPosMove(742, 601);
            TaskControl.CheckAndSleep(100);
            Simulation.SendInput.Mouse.LeftButtonDown();
            TaskControl.CheckAndSleep(50);

            // 向右滑动
            Simulation.SendInput.Mouse.MoveMouseBy(1000, 0);
            TaskControl.CheckAndSleep(200);
            Simulation.SendInput.Mouse.LeftButtonUp();
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
