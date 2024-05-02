using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace BetterGenshinImpact.GameTask.Model.Area;

/// <summary>
/// 区域基类
/// 用于描述一个区域，可以是一个矩形，也可以是一个点
/// </summary>
public class Region : IDisposable
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// 存放OCR识别的结果文本
    /// </summary>
    public string Text { get; set; } = string.Empty;

    public Region()
    {
    }

    public Region(int x, int y, int width, int height, Region? owner = null, INodeConverter? converter = null)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Prev = owner;
        PrevConverter = converter;
    }

    public Region(Rect rect, Region? owner = null, INodeConverter? converter = null) : this(rect.X, rect.Y, rect.Width, rect.Height, owner, converter)
    {
    }

    public Region? Prev { get; }

    public INodeConverter? PrevConverter { get; }

    public List<Region>? NextChildren { get; protected set; }

    /// <summary>
    /// 点击【自己】的中心
    /// </summary>
    public void Click()
    {
        Click(X, Y, Width, Height);
    }

    /// <summary>
    /// 点击区域内【指定位置】
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public void Click(int x, int y)
    {
        Click(x, y, 0, 0);
    }

    /// <summary>
    /// 点击区域内【指定矩形区域】的中心
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="w"></param>
    /// <param name="h"></param>
    /// <exception cref="Exception"></exception>
    public void Click(int x, int y, int w, int h)
    {
        var node = this;
        while (node != null)
        {
            if (node is DesktopRegion)
            {
                break;
            }

            if (node.PrevConverter != null)
            {
                (x, y, w, h) = node.PrevConverter.ToPrev(x, y, w, h);
            }
            else
            {
                throw new Exception("PrevConverter is null");
            }

            node = node.Prev;
        }

        if (node is DesktopRegion desktopRegion)
        {
            desktopRegion.DesktopRegionClick(x, y, w, h);
        }
        else
        {
            throw new Exception("Desktop Region not found");
        }
    }

    public void DrawRect(string name, Pen? pen = null)
    {
        DrawRect(X, Y, Width, Height, name, pen);
    }

    public void DrawRect(int x, int y, int w, int h, string name, Pen? pen = null)
    {
        var drawable = ToRectDrawable(x, y, w, h, name, pen);
        VisionContext.Instance().DrawContent.PutRect(name, drawable);
    }

    /// <summary>
    /// 转换【自己】到遮罩窗口绘制矩形
    /// </summary>
    /// <param name="name"></param>
    /// <param name="pen"></param>
    /// <returns></returns>
    public RectDrawable ToRectDrawable(string name, Pen? pen = null)
    {
        return ToRectDrawable(X, Y, Width, Height, name, pen);
    }

    /// <summary>
    /// 转换【指定区域】到遮罩窗口绘制矩形
    /// </summary>
    /// <param name="rect"></param>
    /// <param name="name"></param>
    /// <param name="pen"></param>
    /// <returns></returns>
    public RectDrawable ToRectDrawable(Rect rect, string name, Pen? pen = null)
    {
        return ToRectDrawable(rect.X, rect.Y, rect.Width, rect.Height, name, pen);
    }

    /// <summary>
    /// 转换【指定区域】到遮罩窗口绘制矩形
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="w"></param>
    /// <param name="h"></param>
    /// <param name="name"></param>
    /// <param name="pen"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public RectDrawable ToRectDrawable(int x, int y, int w, int h, string name, Pen? pen = null)
    {
        var node = this;
        while (node != null)
        {
            if (node is GameCaptureRegion)
            {
                break;
            }

            if (node.PrevConverter != null)
            {
                (x, y, w, h) = node.PrevConverter.ToPrev(x, y, w, h);
            }
            else
            {
                throw new Exception("PrevConverter is null");
            }
            node = node.Prev;
        }

        if (node is GameCaptureRegion gameCaptureRegion)
        {
            return gameCaptureRegion.ConvertToRectDrawable(x, y, w, h, pen, name);
        }
        else
        {
            throw new Exception("Game Capture Region not found");
        }
    }

    public Rect ToRect()
    {
        return new Rect(X, Y, Width, Height);
    }

    public bool IsEmpty()
    {
        return Width == 0 && Height == 0 && X == 0 && Y == 0;
    }

    /// <summary>
    /// 语义化包装
    /// </summary>
    /// <returns></returns>
    public bool IsExist()
    {
        return !IsEmpty();
    }

    public void Dispose()
    {
        // 子节点全部释放
        NextChildren?.ForEach(x => x.Dispose());
    }

    public Region Derive(int x, int y, int w, int h)
    {
        var child = new Region(x, y, w, h, this, new TranslationConverter(X, Y));
        return AddChild(child);
    }

    public Region Derive(Rect rect)
    {
        var child = new Region(rect.X, rect.Y, rect.Width, rect.Height, this, new TranslationConverter(X, Y));
        return AddChild(child);
    }

    protected Region AddChild(Region region)
    {
        NextChildren ??= [];
        NextChildren.Add(region);
        return region;
    }
}
