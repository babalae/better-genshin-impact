using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Vision.Recognition
{
    /// <summary>
    /// Vision 上下文
    /// </summary>
    public class VisionContext
    {
        private static VisionContext? _uniqueInstance;
        private static readonly object Locker = new();

        private VisionContext()
        {
        }

        public static VisionContext Instance()
        {
            if (_uniqueInstance == null)
            {
                lock (Locker)
                {
                    _uniqueInstance ??= new VisionContext();
                }
            }

            return _uniqueInstance;
        }

        public ILogger? Log { get; set; }


        public bool Drawable { get; set; }

        /// <summary>
        /// 在遮罩窗口上绘制的矩形
        /// </summary>
        public List<Rect> DrawRectList { get; set; } = new();
        /// <summary>
        /// 在遮罩窗口上绘制的文本
        /// </summary>
        public List<Tuple<Point, string>> DrawTextList { get; set; } = new();
    }
}