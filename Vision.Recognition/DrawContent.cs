 using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Vision.Recognition
{
    public class DrawContent
    {
        /// <summary>
        /// 在遮罩窗口上绘制的矩形
        /// </summary>
        public ConcurrentDictionary<string, RectDrawable> RectList { get; set; } = new();

        /// <summary>
        /// 在遮罩窗口上绘制的文本
        /// </summary>
        public ConcurrentDictionary<string, TextDrawable> TextList { get; set; } = new();

        public void PutRect(string key, RectDrawable newRect)
        {
            if (RectList.TryGetValue(key, out var prevRect))
            {
                if (newRect.Equals(prevRect))
                {
                    return;
                }
            }

            RectList[key] = newRect;
            MaskWindow.Instance().Refresh();
        }

        public void PutOrRemoveRectList(List<(string, RectDrawable)> list)
        {
            bool changed = false;
            list.ForEach(item =>
            {
                var newRect = item.Item2;
                if (newRect.IsEmpty)
                {
                    if (RectList.TryGetValue(item.Item1, out _))
                    {
                        RectList.TryRemove(item.Item1, out _);
                        changed = true;
                    }
                }
                else
                {
                    if (RectList.TryGetValue(item.Item1, out var prevRect))
                    {
                        if (newRect.Equals(prevRect))
                        {
                            return;
                        }
                    }
                    RectList[item.Item1] = newRect;
                    changed = true;
                }
            });
            if (changed)
            {
                MaskWindow.Instance().Refresh();
            }
        }

        public void RemoveRect(string key)
        {
            if (RectList.TryGetValue(key, out _))
            {
                RectList.TryRemove(key, out _);
                MaskWindow.Instance().Refresh();
            }
        }

        public void PutText(string key, TextDrawable newText)
        {
            if (TextList.TryGetValue(key, out var prevText))
            {
                if (newText.Equals(prevText))
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