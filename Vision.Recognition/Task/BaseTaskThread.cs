using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Vision.Recognition.Task
{
    /// <summary>
    /// 和触发器的区别：任务不需要持续捕获游戏图像
    /// </summary>
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