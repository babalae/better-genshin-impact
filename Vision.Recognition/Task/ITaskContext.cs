using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vision.Recognition.Task
{
    /// <summary>
    /// 任务上下文
    /// </summary>
    public interface ITaskContext
    {

        Bitmap Capture();
    }
}
