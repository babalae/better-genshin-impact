using BetterGenshinImpact.Core.Simulator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Macro
{
    public class TurnAroundMacro
    {
        public static void Done()
        {
            Simulation.SendInput.Mouse.MoveMouseBy(600, 0);
        }
    }
}