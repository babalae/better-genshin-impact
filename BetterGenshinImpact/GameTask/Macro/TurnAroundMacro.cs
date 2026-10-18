using BetterGenshinImpact.Core.Input;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Macro
{
    public class TurnAroundMacro
    {
        public static void Done()
        {
            if (TaskContext.Instance().Config.MacroConfig.RunaroundMouseXInterval == 0)
            {
                TaskContext.Instance().Config.MacroConfig.RunaroundMouseXInterval = 1;
            }

            InputHub.Foreground.Mouse.MoveMouseBy(TaskContext.Instance().Config.MacroConfig.RunaroundMouseXInterval, 0);
            Thread.Sleep(TaskContext.Instance().Config.MacroConfig.RunaroundInterval);
        }
    }
}
