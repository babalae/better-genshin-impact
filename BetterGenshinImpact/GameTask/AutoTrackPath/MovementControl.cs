using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Model;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public class MovementControl : Singleton<MovementControl>
{
    private bool _wDown = false;

    public void WDown()
    {
        if (!_wDown)
        {
            _wDown = true;
            Simulation.SendInput.Keyboard.KeyDown(User32.VK.VK_W);
        }
    }

    public void WUp()
    {
        if (_wDown)
        {
            _wDown = false;
            Simulation.SendInput.Keyboard.KeyUp(User32.VK.VK_W);
        }
    }

    public void SpacePress()
    {
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
    }
}
