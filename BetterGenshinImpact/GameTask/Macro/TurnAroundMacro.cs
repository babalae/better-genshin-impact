using BetterGenshinImpact.Core.Simulator;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Macro
{
    public class TurnAroundMacro
    {
        public static void Done()
        {
            Simulation.SendInputEx.Mouse.MoveMouseBy(500, 0);
            Thread.Sleep(50);
        }
    }
}