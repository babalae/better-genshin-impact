using System;
using BetterGenshinImpact.Core.Input;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class ReturnMainUiTask
{
    public string Name => "返回主界面";

    public Task Start(CancellationToken ct) => Start(ct, screen => InstrumentExitRecovery.TryExitAsync(screen, ct));

    internal async Task Start(CancellationToken ct, Func<ImageRegion, Task<bool>> tryExitInstrument)
    {
        using (var initialCapture = CaptureToRectArea())
        {
            if (await tryExitInstrument(initialCapture) || Bv.IsInMainUi(initialCapture))
                return;
        }

        for (var i = 0; i < 8; i++)
        {
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(900, ct);

            var region = CaptureToRectArea();

            try
            {
                if (await tryExitInstrument(region) || Bv.IsInMainUi(region))
                    return;

                using var exitDoor = region.Find(ElementRecognition.Get("BtnExitDoor", region));
                if (exitDoor.IsExist())
                {
                    exitDoor.Click();
                    await Delay(5000, ct);
                    region.Dispose();
                    region = CaptureToRectArea();
                    if (await tryExitInstrument(region) || Bv.IsInMainUi(region))
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
            if (await tryExitInstrument(beforeEnter) || Bv.IsInMainUi(beforeEnter))
                return;
        }

        InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_RETURN);
        await Delay(500, ct);
        using var beforeEscape = CaptureToRectArea();
        if (!await tryExitInstrument(beforeEscape) && !Bv.IsInMainUi(beforeEscape))
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
    }
}
