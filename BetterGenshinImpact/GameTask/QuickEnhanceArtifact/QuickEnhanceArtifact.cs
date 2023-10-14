using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;

namespace BetterGenshinImpact.GameTask.QuickEnhanceArtifact;

public class QuickEnhanceArtifact
{
    public static void Done()
    {
        Simulation.SendInput.Mouse.MoveMouseTo(65535, 65535 / 2d);
    }
}