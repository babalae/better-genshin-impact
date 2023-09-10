using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Vision.Recognition.Task
{
    public class BaseTaskThread
    {
        protected ILogger<BaseTaskThread> Log;

        protected CancellationTokenSource Cancellation;

        protected ITaskContext TaskContext;

        public BaseTaskThread(ILogger<BaseTaskThread> logger, CancellationTokenSource cts, ITaskContext taskContext)
        {
            Log = logger;
            Cancellation = cts;
            TaskContext = taskContext;
        }
    }
}