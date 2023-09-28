using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using Vision.Recognition.Task;

namespace BetterGenshinImpact.GameTask
{
    internal class GameTaskManager
    {
        public static List<ITaskTrigger> LoadTriggers()
        {
            List<ITaskTrigger> loadedTriggers = new()
            {
                new AutoPick.AutoPickTrigger(),
                new AutoSkip.AutoSkipTrigger(),
                new AutoFishing.AutoFishingTrigger()
            };

            loadedTriggers.ForEach(i => i.Init());

            return loadedTriggers.OrderByDescending(i => i.Priority).ToList();
        }

    }
}
