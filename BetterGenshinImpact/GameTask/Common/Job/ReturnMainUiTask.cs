using BetterGenshinImpact.Core.Input;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 返回主界面
/// </summary>
/// <param name="runtime">本次运行使用的运行环境</param>
public class ReturnMainUiTask(GameRuntime runtime)
{
    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime = runtime;

    public string Name => "返回主界面";

    public async Task Start(CancellationToken ct)
    {
        using var initialCapture = _runtime.CaptureToRectArea();
        if (Bv.IsInMainUi(initialCapture))
        {
            return;
        }

        for (var i = 0; i < 8; i++)
        {
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await _runtime.Delay(900, ct);

            var region = _runtime.CaptureToRectArea();

            try
            {
                var exitDoor = region.Find(ElementRecognition.Get("BtnExitDoor", region));
                if (exitDoor.IsExist())
                {
                    exitDoor.Click();
                    await _runtime.Delay(5000, ct);
                    region.Dispose();
                    region = _runtime.CaptureToRectArea();
                }

                if (Bv.IsInMainUi(region))
                {
                    return;
                }
            }
            finally
            {
                region.Dispose();
            }
        }
        await _runtime.Delay(500, ct);
        InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_RETURN);
        await _runtime.Delay(500, ct);
        InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
    }
}
