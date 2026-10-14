using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using System;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoSkip;

public class OneKeyExpeditionTask
{
    public void Run()
    {
        try
        {
            SystemControl.ActivateWindow();

            // 进入派遣页面后可能先弹出"是否使用历练点领取每日委托"的提示框,
            // 且每日委托需先领,故最多等约 2 秒点掉"确认"。
            for (var i = 0; i < 4; i++)
            {
                using var confirmRa = CaptureToRectArea(true);
                if (Bv.ClickBlackConfirmButton(confirmRa))
                {
                    Logger.LogInformation("探索派遣：{Text}", "确认使用历练点领取每日委托");
                    Sleep(1000);
                    break;
                }

                Sleep(300);
            }

            var claimed = false;
            for (int i = 0; i < 3; i++)
            {
                // 1.全部领取
                using var region = CaptureToRectArea(true);
                using var ra = region.Find(RecognitionAssets.Get("AutoSkip", "Collect", region.Width, region.Height));
                if (ra.IsEmpty())
                {
                    Logger.LogInformation("探索派遣：{Text}", "未找到领取按钮");
                    Sleep(1000);
                    continue;
                }

                ra.Click();
                Logger.LogInformation("探索派遣：{Text}", "全部领取");
                Sleep(1100);

                // 领取后可能再弹"是否使用历练点"提示框(会挡住"再次派遣"),先点掉"确认"
                using (var confirmRa = CaptureToRectArea(true))
                {
                    if (Bv.ClickBlackConfirmButton(confirmRa))
                    {
                        Logger.LogInformation("探索派遣：{Text}", "确认使用历练点领取每日委托");
                        Sleep(800);
                    }
                }

                // 2.重新派遣
                NewRetry.Do(() =>
                {
                    Sleep(1);
                    using var reRegion = CaptureToRectArea(true);
                    using var ra2 = reRegion.Find(RecognitionAssets.Get("AutoSkip", "Re", reRegion.Width, reRegion.Height));
                    if (ra2.IsEmpty())
                    {
                        throw new RetryException("未检测到弹出菜单");
                    }

                    ra2.Click();
                    Logger.LogInformation("探索派遣：{Text}", "再次派遣");
                }, TimeSpan.FromSeconds(1), 3);

                // 3.退出派遣页面 ESC
                Sleep(500);
                InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
                Logger.LogInformation("探索派遣：{Text}", "完成");

                // 退出后可能再弹一次"是否使用历练点"提示框,点掉"确认"
                Sleep(800);
                using (var finalRa = CaptureToRectArea(true))
                {
                    if (Bv.ClickBlackConfirmButton(finalRa))
                    {
                        Logger.LogInformation("探索派遣：{Text}", "确认使用历练点领取每日委托");
                    }
                }

                claimed = true;
                break;
            }

            if (!claimed)
            {
                // 没有可领取/可再次派遣的内容时,同样退出派遣页面,避免停留在派遣界面
                InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
                Logger.LogInformation("探索派遣：{Text}", "无可领取内容，退出派遣页面");
            }
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
        }
        finally
        {
            TaskContext.Instance().Runtime?.MaskWindowDrawingBoard.ClearAll();
        }
    }
}
