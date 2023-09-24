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
        public ConcurrentDictionary<string, System.Windows.Rect> RectList { get; set; } = new();

        /// <summary>
        /// 在遮罩窗口上绘制的文本
        /// </summary>
        public ConcurrentDictionary<string, (System.Windows.Point, string)> TextList { get; set; } = new();

        public void PutRect(string key, System.Windows.Rect newRect)
        {
            if (RectList.TryGetValue(key, out var prevRect))
            {
                if (newRect == prevRect)
                {
                    return;
                }
            }

            RectList[key] = newRect;
            MaskWindow.Instance().Refresh();
        }

        public void RemoveRect(string key)
        {
            if (RectList.TryGetValue(key, out _))
            {
                RectList.TryRemove(key, out _);
                MaskWindow.Instance().Refresh();
            }
        }

        public void PutText(string key, (System.Windows.Point, string) newText)
        {
            if (TextList.TryGetValue(key, out var prevText))
            {
                if (newText.Item1 == prevText.Item1 && newText.Item2 == prevText.Item2)
                {
                    return;
                }
            }

            TextList[key] = newText;
            MaskWindow.Instance().Refresh();
        }

        public void RemoveText(string key)
        {
            if (TextList.TryGetValue(key, out _))
            {
                TextList.TryRemove(key, out _);
                MaskWindow.Instance().Refresh();
            }
        }
    }
}