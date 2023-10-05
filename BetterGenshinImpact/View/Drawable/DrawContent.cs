using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BetterGenshinImpact.View.Drawable;

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


    /// <summary>
    /// 清理所有绘制内容
    /// </summary>
    public void ClearAll()
    {
        RectList.Clear();
        TextList.Clear();
        MaskWindow.Instance().Refresh();
    }
}