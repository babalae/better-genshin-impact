using BetterGenshinImpact.Core.Input;
using System;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.QuickBuy;

public class QuickBuyTask
{
    private static readonly ILogger<QuickBuyTask> _logger = App.GetLogger<QuickBuyTask>();

    /// <summary>
    /// 快速购买热键入口。运行环境是否存在由热键调用方判断
    /// </summary>
    /// <param name="runtime">热键触发时的运行环境，截图、等待与点击都只使用它</param>
    public static void Done(GameRuntime runtime)
    {
        if (!runtime.IsOperable())
        {
            return;
        }

        try
        {
            using ImageRegion ra = runtime.CaptureToRectArea();
            if (ra.Find(RecognitionAssets.Get("QuickBuy", "SereniteaPotCoin", ra)).IsExist())
            {
                // _logger.LogInformation("触发尘歌壶快速购买逻辑");
                // 尘歌壶购买逻辑
                // GameCaptureRegion.GameRegionClick((size, scale) => (200 * scale, 200 * scale));
                // TaskControl.CheckAndSleep(100);
                // 选中左边点 
                runtime.GameRegion1080PPosMove(1450, 690);
                runtime.CheckAndSleep(100);
                InputHub.Foreground.Mouse.LeftButtonDown();
                runtime.CheckAndSleep(50);

                // 向右滑动
                InputHub.Foreground.Mouse.MoveMouseBy(1000, 0);
                runtime.CheckAndSleep(200);
                InputHub.Foreground.Mouse.LeftButtonUp();
                runtime.CheckAndSleep(200);

                runtime.GameRegion1080PPosClick(1600, 1020);
                runtime.CheckAndSleep(200); // 等待窗口消失
                runtime.GameRegion1080PPosClick(960, 850);

                return;
            }

            // 点击购买/兑换 右下225x60
            runtime.GameRegionClick((size, scale) => (size.Width - 225 * scale, size.Height - 60 * scale));
            runtime.CheckAndSleep(100); // 等待窗口弹出

            // 选中左边点 742x601
            runtime.GameRegion1080PPosMove(742, 601);
            runtime.CheckAndSleep(100);
            InputHub.Foreground.Mouse.LeftButtonDown();
            runtime.CheckAndSleep(50);

            // 向右滑动
            InputHub.Foreground.Mouse.MoveMouseBy(1000, 0);
            runtime.CheckAndSleep(200);
            InputHub.Foreground.Mouse.LeftButtonUp();
            runtime.CheckAndSleep(100);

            // 点击弹出页的购买/兑换 1100x780
            runtime.GameRegion1080PPosClick(1100, 780);
            runtime.CheckAndSleep(200); // 等待窗口消失
            runtime.GameRegionClick((size, scale) => (size.Width - 225 * scale, size.Height - 60 * scale));
            runtime.CheckAndSleep(200);
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
