using System.Collections.Concurrent;
using System.Collections.Generic;

namespace BetterGenshinImpact.View.Drawable;

public class DrawContent
{
    /// <summary>
    /// 在遮罩窗口上绘制的矩形
    /// </summary>
    public ConcurrentDictionary<string, List<RectDrawable>> RectList { get; set; } = new();

    /// <summary>
    /// 在遮罩窗口上绘制的文本
    /// </summary>
    public ConcurrentDictionary<string, List<TextDrawable>> TextList { get; set; } = new();

    /// <summary>
    /// 在遮罩窗口上绘制的文本
    /// </summary>
    public ConcurrentDictionary<string, List<LineDrawable>> LineList { get; set; } = new();

    public virtual void PutRect(string key, RectDrawable newRect)
    {
        if (RectList.TryGetValue(key, out var prevRect))
        {
            if (prevRect.Count == 0 && newRect.Equals(prevRect[0]))
            {
                return;
            }
        }

        RectList[key] = [newRect];
        MaskWindow.Instance().Refresh();
    }

    public virtual void PutOrRemoveRectList(string key, List<RectDrawable>? list)
    {
        bool changed = false;

        if (RectList.TryGetValue(key, out var prevRect))
        {
            if (list == null)
            {
                RectList.TryRemove(key, out _);
                changed = true;
            }
            else if (prevRect.Count != list.Count)
            {
                RectList[key] = list;
                changed = true;
            }
            else
            {
                // 不逐一比较了，使用这个方法的地方，都是在每一帧都会刷新的
                RectList[key] = list;
                changed = true;
            }
        }
        else
        {
            if (list is { Count: > 0 })
            {
                RectList[key] = list;
                changed = true;
            }
        }

        if (changed)
        {
            MaskWindow.Instance().Refresh();
        }
    }

    public virtual void RemoveRect(string key)
    {
        if (RectList.TryGetValue(key, out _))
        {
            RectList.TryRemove(key, out _);
            MaskWindow.Instance().Refresh();
        }
    }

    public virtual void PutLine(string key, LineDrawable newLine)
    {
        if (LineList.TryGetValue(key, out var prev))
        {
            if (prev.Count == 0 && newLine.Equals(prev[0]))
            {
                return;
            }
        }

        LineList[key] = [newLine];
        MaskWindow.Instance().Refresh();
    }


    public virtual void RemoveLine(string key)
    {
        if (LineList.TryGetValue(key, out _))
        {
            LineList.TryRemove(key, out _);
            MaskWindow.Instance().Refresh();
        }
    }

    /// <summary>
    /// 更新文本列表
    /// </summary>
    public virtual void PutOrRemoveTextList(string key, List<TextDrawable>? list)
    {
        bool changed = false;
        if (TextList.TryGetValue(key, out _))
        {
            if (list == null || list.Count == 0)
            {
                TextList.TryRemove(key, out _);
                changed = true;
            }
            else
            {
                TextList[key] = list;
                changed = true;
            }
        }
        else
        {
            if (list is { Count: > 0 })
            {
                TextList[key] = list;
                changed = true;
            }
        }

        if (changed)
        {
            MaskWindow.Instance().Refresh();
        }
    }

    /// <summary>
    /// 清理所有绘制内容
    /// </summary>
    public virtual void ClearAll()
    {
        if (RectList.IsEmpty && TextList.IsEmpty && LineList.IsEmpty)
        {
            return;
        }
        RectList.Clear();
        TextList.Clear();
        LineList.Clear();
        MaskWindow.Instance().Refresh();
    }
}