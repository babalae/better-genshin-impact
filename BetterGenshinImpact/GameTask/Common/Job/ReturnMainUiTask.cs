using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
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
            await Delay(900, ct);

            var region = CaptureToRectArea();

            var exitDoor = region.Find(ElementAssets.Instance.BtnExitDoor.Value);
            if (exitDoor.IsExist())
            {
                exitDoor.Click();
                await Delay(5000, ct);
                region = CaptureToRectArea();
            }
            
            if (Bv.IsInMainUi(region))
            {
                region.Dispose();
                return;
            }
            else
            {
                region.Dispose();
            }
        }
        await Delay(500, ct);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_RETURN);
        await Delay(500, ct);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
    }
}
