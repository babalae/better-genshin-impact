using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

public class ReturnMainUiTask
{
    public string Name => "返回主界面";

    public async Task Start(CancellationToken ct)
    {
        if (Bv.IsInMainUi(CaptureToRectArea()))
        {
            return;
        }

        for (var i = 0; i < 8; i++)
        {
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await Delay(1000, ct);
            if (Bv.IsInMainUi(CaptureToRectArea()))
            {
                return;
            }
        }
        await Delay(500, ct);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_RETURN);
        await Delay(500, ct);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
    }
}
