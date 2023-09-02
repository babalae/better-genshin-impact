using System;
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
        public List<System.Windows.Rect> RectList { get; set; } = new();
        /// <summary>
        /// 在遮罩窗口上绘制的文本
        /// </summary>
        public List<Tuple<System.Windows.Point, string>> TextList { get; set; } = new();
    }
}
