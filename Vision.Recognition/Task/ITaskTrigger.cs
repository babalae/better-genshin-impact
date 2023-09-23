using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;
using Vision.Recognition.Helper.OpenCv;

namespace Vision.Recognition.Task
{
    /// <summary>
    /// 触发器接口
    /// * 可以用于任务的触发、任务触发前的控件展示
    /// * 也可以是任务的本身
    ///
    /// 需要短时间内持续循环获取游戏图像的，使用触发器；
    /// 需要休眠等待且有一定流程的，应该使用<see cref="BaseTaskThread"/>
    /// </summary>
    public interface ITaskTrigger
    {
        /// <summary>
        /// 触发器名称
        /// </summary>
        string Name { get; }
        /// <summary>
        /// 是否处于启用状态
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// 执行优先级，越大越先执行
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 当前是否处于独占模式
        /// </summary>
        bool IsExclusive { get; }

        /// <summary>
        /// 初始化
        /// </summary>
        void Init();

        /// <summary>
        /// 捕获图像后操作
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="frameIndex"></param>
        void OnCapture(Bitmap bitmap, int frameIndex)
        {
            OnCapture(bitmap.ToMat(), frameIndex);
        }
        /// <summary>
        /// 捕获图像后操作
        /// </summary>
        /// <param name="matSrc">OpenCV的图片对象</param>
        /// <param name="frameIndex"></param>
        void OnCapture(Mat matSrc, int frameIndex);
    }
}
