using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;

namespace Vision.Recognition.Task
{
    /// <summary>
    /// 触发器接口
    /// * 可以用于任务的触发、任务触发前的控件展示
    /// * 也可以是任务的本身
    /// </summary>
    public interface ITaskTrigger
    {
        bool IsEnabled { get; set; }

        int Priority { get; }

        /// <summary>
        /// 当前是否处于独占模式
        /// </summary>
        bool IsExclusive { get; }

        void Init(ITaskContext context);

        void OnCapture(Bitmap bitmap, int frameIndex);

        void OnCapture(Mat matSrc, int frameIndex);
    }
}
