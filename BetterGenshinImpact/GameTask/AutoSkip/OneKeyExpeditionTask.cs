using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using System;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoSkip;

/// <summary>
/// 一键领取并重新派遣探索派遣
/// </summary>
/// <param name="runtime">本次运行使用的运行环境，截图、等待与窗口激活都只使用它</param>
public class OneKeyExpeditionTask(GameRuntime runtime)
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    /// <summary>
    /// 在探索派遣界面领取全部奖励并再次派遣
    /// </summary>
    public void Run()
    {
        try
        {
            _runtime.Window.Activate();

            for (int i = 0; i < 2; i++)
            {
                // 1.全部领取
                var region = _runtime.CaptureToRectArea();
                // Cv2.ImWrite($"log/ts.png", region.SrcMat);
                var ra = region.Find(RecognitionAssets.Get("AutoSkip", "Collect", region.Width, region.Height));
                if (!ra.IsEmpty())
                {
                    ra.Click();
                    Logger.LogInformation("探索派遣：{Text}", "全部领取");
                    _runtime.Sleep(1100);
                    // 2.重新派遣
                    NewRetry.Do(() =>
                    {
                        _runtime.Sleep(1);
                        region = _runtime.CaptureToRectArea();
                        var ra2 = region.Find(RecognitionAssets.Get("AutoSkip", "Re", region.Width, region.Height));
                        if (ra2.IsEmpty())
                        {
                            throw new RetryException("未检测到弹出菜单");
                        }
                        else
                        {
                            ra2.Click();
                            Logger.LogInformation("探索派遣：{Text}", "再次派遣");
                        }
                    }, TimeSpan.FromSeconds(1), 3);

                    // 3.退出派遣页面 ESC
                    _runtime.Sleep(500);
                    InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
                    Logger.LogInformation("探索派遣：{Text}", "完成");
                    break;
                }
                else
                {
                    Logger.LogInformation("探索派遣：{Text}", "未找到领取按钮");
                    if (i == 0)
                    {
                        Logger.LogInformation("探索派遣：{Text}", "等待1s后重试");
                        _runtime.Sleep(1000);
                    }
                    continue;
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
        }
        finally
        {
            _runtime.MaskWindowDrawingBoard.ClearAll();
        }
    }
}
