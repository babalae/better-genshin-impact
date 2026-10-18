using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Service;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Macro
{
    public class TurnAroundMacro
    {
        /// <summary>
        /// 配置根。静态方法没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
        /// </summary>
        private static AllConfig Config => ConfigService.Config!;

        public static void Done()
        {
            if (Config.MacroConfig.RunaroundMouseXInterval == 0)
            {
                Config.MacroConfig.RunaroundMouseXInterval = 1;
            }

            InputHub.Foreground.Mouse.MoveMouseBy(Config.MacroConfig.RunaroundMouseXInterval, 0);
            Thread.Sleep(Config.MacroConfig.RunaroundInterval);
        }
    }
}
