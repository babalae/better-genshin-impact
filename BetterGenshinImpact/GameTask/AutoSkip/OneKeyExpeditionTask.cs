using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoSkip;

public class OneKeyExpeditionTask
{
    /// <param name="dailyClaimed">
    /// 本次是否已在凯瑟琳对话中同帧领取了每日委托。只有领了每日委托才会弹"是否使用历练点领取每日委托"的确认框,
    /// 因此只有该值为 true 时才需要轮询/点击确认框,避免纯探索派遣流程白等。
    /// </param>
    public void Run(bool dailyClaimed = false)
    {
        try
        {
            SystemControl.ActivateWindow();

            if (dailyClaimed)
            {
                // 从凯瑟琳对话进入派遣页后,先给界面 500ms 稳定时间
                Sleep(500);
                // 进入派遣页面后可能先弹出"是否使用历练点领取每日委托"的提示框,
                // 且每日委托需先领。由 NewRetry 立即检测,失败后按 1s 重试。
                PollAndClickDailyConfirm(3, "进入派遣页面");
            }

            // 1.全部领取:同页面检测
            Sleep(1000);
            if (!TryClickCollect())
            {
                // 没有可领取/可再次派遣的内容时,退出派遣页面,避免停留在派遣界面
                InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
                Logger.LogInformation("探索派遣：{Text}", "无可领取内容，退出派遣页面");
                return;
            }

            // 2.重新派遣:点击"全部领取"后先等动画,再由 NewRetry 立即检测
            Sleep(1000);
            NewRetry.Do(() =>
            {
                using var reRegion = CaptureToRectArea(true);
                using var ra2 = reRegion.Find(RecognitionAssets.Get("AutoSkip", "Re", reRegion.Width, reRegion.Height));
                if (ra2.IsEmpty())
                {
                    throw new RetryException("未检测到弹出菜单");
                }

                ra2.Click();
                Logger.LogInformation("探索派遣：{Text}", "再次派遣");
            }, TimeSpan.FromSeconds(1), 3);

            // 3.点击"再次派遣"后等 1000ms,按 ESC 退出派遣页面
            Sleep(1000);
            InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
            Logger.LogInformation("探索派遣：{Text}", "完成");

            if (dailyClaimed)
            {
                // 按下 ESC 后先等 1000ms,再检测可能残留的历练点确认框
                Sleep(1000);
                PollAndClickDailyConfirm(2, "退出派遣页面");
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

    /// <summary>
    /// 通过 NewRetry 检测并点掉「是否使用历练点领取每日委托」确认框。
    /// </summary>
    /// <param name="maxTries">最多检测次数</param>
    /// <param name="stage">日志用的阶段名，便于从日志判断是哪个环节等的久</param>
    /// <returns>是否命中并点击</returns>
    private static bool PollAndClickDailyConfirm(int maxTries, string stage)
    {
        var sw = Stopwatch.StartNew();
        var attempt = 0;
        try
        {
            return NewRetry.Do<bool>(() =>
            {
                attempt++;
                using var confirmRa = CaptureToRectArea(true);
                if (!Bv.ClickBlackConfirmButton(confirmRa))
                {
                    throw new RetryException("未检测到历练点确认框");
                }

                Logger.LogInformation("探索派遣：{Text}", "确认使用历练点领取每日委托");
                Logger.LogDebug("历练点确认框：{Stage} 第 {Try} 次检测命中，用时 {Ms}ms", stage, attempt, sw.ElapsedMilliseconds);
                return true;
            }, TimeSpan.FromSeconds(1), maxTries);
        }

        catch (RetryException)
        {
            Logger.LogDebug("历练点确认框：{Stage} {Tries} 次检测未命中，用时 {Ms}ms", stage, attempt, sw.ElapsedMilliseconds);
            return false;
        }
    }

    private static bool TryClickCollect()
    {
        var attempt = 0;
        var loggedMissingCollect = false;
        try
        {
            NewRetry.Do<bool>(() =>
            {
                attempt++;
                using var region = CaptureToRectArea(true);
                using var ra = region.Find(RecognitionAssets.Get("AutoSkip", "Collect", region.Width, region.Height));
                if (!ra.IsEmpty())
                {
                    ra.Click();
                    Logger.LogInformation("探索派遣：{Text}", "全部领取");
                    return true;
                }

                if (!loggedMissingCollect)
                {
                    Logger.LogInformation("探索派遣：{Text}", "未找到领取按钮");
                    loggedMissingCollect = true;
                }

                throw new RetryException("未检测到全部领取");
            }, TimeSpan.FromSeconds(1), 3);

            return true;
        }
        catch (RetryException)
        {
            return false;
        }
    }
}
