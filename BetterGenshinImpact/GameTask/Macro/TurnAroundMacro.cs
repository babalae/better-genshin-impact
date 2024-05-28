using BetterGenshinImpact.Core.Simulator;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Macro
{
    public class TurnAroundMacro
    {
        public static void Done()
        {
            Simulation.SendInput.Mouse.MoveMouseBy(TaskContext.Instance().Config.MacroConfig.RunaroundMouseXInterval, 0);
            Thread.Sleep(TaskContext.Instance().Config.MacroConfig.RunaroundInterval);
        }
    }
}