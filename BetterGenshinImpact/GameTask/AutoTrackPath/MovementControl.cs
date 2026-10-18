using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Model;
using System;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

[Obsolete]
public class MovementControl : Singleton<MovementControl>
{
    private bool _wDown = false;

    public void WDown()
    {
        if (!_wDown)
        {
            _wDown = true;
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        }
    }

    public void WUp()
    {
        if (_wDown)
        {
            _wDown = false;
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        }
    }

    public void SpacePress()
    {
        InputHub.Foreground.SimulateAction(GIActions.Jump);
    }
}
