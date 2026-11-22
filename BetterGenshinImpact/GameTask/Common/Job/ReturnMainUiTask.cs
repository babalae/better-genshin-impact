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
        if (quest != null)
        {
            if (waitForWorld)
            {
                await RunnerContext.EnsureWaypointActionWorldAsync(ct, quest);
                return;
            }
            // 限时传送只接管已确认的普通菜单，演奏与未知过场交给外层在原步骤等待。
            var runtime = TaskContext.Instance().Runtime;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                using var screen = CaptureToRectArea();
                if (await InstrumentExitRecovery.TryExitAsync(screen, ct, waitForWorld: false, quest: quest)) return;
                if (!await InstrumentExitRecovery.TryCloseOrdinaryMenuAsync(runtime, ct))
                    throw new InstrumentExitRecoveryException("等待标记点的界面恢复", allowOrdinaryMenu: true);
                await Delay(900, ct);
            }
        }
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
