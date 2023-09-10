using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vision.Recognition
{
    public class DrawContent
    {
        /// <summary>
        /// 在遮罩窗口上绘制的矩形
        /// </summary>
        public ConcurrentBag<System.Windows.Rect> RectList { get; set; } = new();
        /// <summary>
        /// 在遮罩窗口上绘制的文本
        /// </summary>
        public ConcurrentBag<Tuple<System.Windows.Point, string>> TextList { get; set; } = new();
    }
}
