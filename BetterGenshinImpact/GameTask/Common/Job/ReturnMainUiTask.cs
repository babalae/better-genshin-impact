using BetterGenshinImpact.Core.Input;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class ReturnMainUiTask
{
    public string Name => "返回主界面";

    public Task Start(CancellationToken ct) => Start(ct, waitForWorld: true);

    internal async Task Start(CancellationToken ct, bool waitForWorld, InstrumentQuestRecovery? quest = null)
    {
        using (var initialCapture = CaptureToRectArea())
        {
            if (await InstrumentExitRecovery.TryExitAsync(initialCapture, ct, waitForWorld, quest: quest))
                return;
        }

        for (var i = 0; i < 8; i++)
        {
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(900, ct);

            var region = CaptureToRectArea();

            try
            {
                if (await InstrumentExitRecovery.TryExitAsync(region, ct, waitForWorld, quest: quest))
                    return;

                using var exitDoor = region.Find(ElementRecognition.Get("BtnExitDoor", region));
                if (exitDoor.IsExist())
                {
                    exitDoor.Click();
                    await Delay(5000, ct);
                    region.Dispose();
                    region = CaptureToRectArea();
                    if (await InstrumentExitRecovery.TryExitAsync(region, ct, waitForWorld, quest: quest))
                        return;
                }
            }
            finally
            {
                region.Dispose();
            }
        }
        await Delay(500, ct);
        using (var beforeEnter = CaptureToRectArea())
        {
            if (await InstrumentExitRecovery.TryExitAsync(beforeEnter, ct, waitForWorld, quest: quest))
                return;
        }

        InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_RETURN);
        await Delay(500, ct);
        using var beforeEscape = CaptureToRectArea();
        if (!await InstrumentExitRecovery.TryExitAsync(beforeEscape, ct, waitForWorld, quest: quest))
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
    }
}
