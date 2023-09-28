using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vision.Recognition.Task;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoThrowRodTask : BaseTaskThread
    {
        public AutoThrowRodTask(CancellationTokenSource cts) : base(cts)
        {
        }
    }
}
