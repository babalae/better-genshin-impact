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
            //if (!Directory.Exists(folder))
            //    return;

            //Directory.GetFiles(folder, "BGI.Task.*.dll",
            //        SearchOption.AllDirectories)
            //    .ToList()
            //    .ForEach(
            //        lib =>
            //        {
            //            (from t in Assembly.LoadFrom(lib).GetExportedTypes()
            //                    where !t.IsInterface && !t.IsAbstract
            //                    where typeof(ITaskTrigger).IsAssignableFrom(t)
            //                    select t).ToList()
            //                .ForEach(type => LoadedTriggers.Add(type.CreateInstance<ITaskTrigger>()));
            //        });

            List<ITaskTrigger> loadedTriggers = new();
            loadedTriggers.Add(new AutoSkip.AutoSkipTrigger());

            return loadedTriggers.OrderByDescending(i => i.Priority).ToList();
        }

    }
}
