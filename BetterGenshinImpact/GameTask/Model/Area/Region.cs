using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.Core.Mask;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using BetterGenshinImpact.GameTask.Common;
using Vanara.PInvoke;

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

    public int Top
    {
        get => Y;
        set => Y = value;
    }

    /// <summary>
    /// Gets the y-coordinate that is the sum of the Y and Height property values of this Rect structure.
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// Gets the x-coordinate of the left edge of this Rect structure.
    /// </summary>
    public int Left
    {
        get => X;
        set => X = value;
    }

    /// <summary>
    /// Gets the x-coordinate that is the sum of X and Width property values of this Rect structure.
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// 存放OCR识别的结果文本
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 模板匹配得分，值越大表示匹配程度越高；非模板匹配结果为 null。
    /// </summary>
    public double? MatchScore { get; set; }

    public Region()
    {
    }

    public Region(int x, int y, int width, int height, Region? owner = null, INodeConverter? converter = null, IMaskWindowDrawingBoard? drawingBoard = null)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Prev = owner;
        PrevConverter = converter;
        DrawingBoard = drawingBoard ?? owner?.DrawingBoard ?? NullMaskWindowDrawingBoard.Instance;
    }

    public Region(Rect rect, Region? owner = null, INodeConverter? converter = null) : this(rect.X, rect.Y, rect.Width, rect.Height, owner, converter)
    {
    }

    public Region? Prev { get; }

    /// <summary>
    /// 本区域节点向上一个区域节点坐标的转换器
    /// </summary>
    public INodeConverter? PrevConverter { get; }

    /// <summary>
    /// 遮罩窗口绘制入口。根区域创建时传入，子区域自动继承；不在截图区域树下的 Region 为空实现
    /// </summary>
    public IMaskWindowDrawingBoard DrawingBoard { get; } = NullMaskWindowDrawingBoard.Instance;

    // public List<Region>? NextChildren { get; protected set; }

    /// <summary>
    /// 后台点击【自己】的中心
    /// </summary>
    public void BackgroundClick()
    {
        User32.GetCursorPos(out var p);
        this.Move();  // 必须移动实际鼠标（前台通道）
        InputHub.Background.Mouse.LeftButtonClick(); // 后台通道点击
        Thread.Sleep(10);
        DesktopRegion.DesktopRegionMove(p.X, p.Y); // 鼠标移动回原来位置
    }

    /// <summary>
    /// 点击【自己】的中心
    /// region.Derive(x,y).Click() 等效于 region.ClickTo(x,y)
    /// </summary>
    public Region Click()
    {
        // 相对自己是 0, 0 坐标
        ClickTo(0, 0, Width, Height);
        return this;
    }
    
    public Region DoubleClick()
    {
        // 相对自己是 0, 0 坐标
        ClickTo(0, 0, Width, Height);
        TaskControl.Sleep(60);
        ClickTo(0, 0, Width, Height);
        return this;
    }

    /// <summary>
    /// 点击区域内【指定位置】
    /// region.Derive(x,y).Click() 等效于 region.ClickTo(x,y)
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public void ClickTo(int x, int y)
    {
        ClickTo(x, y, 0, 0);
    }

    public void ClickTo(double dx, double dy)
    {
        var x = (int)Math.Round(dx);
        var y = (int)Math.Round(dy);
        ClickTo(x, y, 0, 0);
    }

    /// <summary>
    /// 点击区域内【指定矩形区域】的中心
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="w"></param>
    /// <param name="h"></param>
    /// <exception cref="Exception"></exception>
    public void ClickTo(int x, int y, int w, int h)
    {
        var res = ConvertRes<DesktopRegion>.ConvertPositionToTargetRegion(x, y, w, h, this);
        res.TargetRegion.DesktopRegionClick(res.X, res.Y, res.Width, res.Height);
    }

    /// <summary>
    /// 移动到【自己】的中心
    /// region.Derive(x,y).Move() 等效于 region.MoveTo(x,y)
    /// </summary>
    public void Move()
    {
        // 相对自己是 0, 0 坐标
        MoveTo(0, 0, Width, Height);
    }

    /// <summary>
    /// 移动到区域内【指定位置】
    /// region.Derive(x,y).Move() 等效于 region.MoveTo(x,y)
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    public void MoveTo(int x, int y)
    {
        MoveTo(x, y, 0, 0);
    }

    /// <summary>
    /// 移动到区域内【指定矩形区域】的中心
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="w"></param>
    /// <param name="h"></param>
    /// <exception cref="Exception"></exception>
    public void MoveTo(int x, int y, int w, int h)
    {
        var res = ConvertRes<DesktopRegion>.ConvertPositionToTargetRegion(x, y, w, h, this);
        res.TargetRegion.DesktopRegionMove(res.X, res.Y, res.Width, res.Height);
    }

    /// <summary>
    /// 直接在遮罩窗口绘制【自己】
    /// </summary>
    /// <param name="name">绘制分组名</param>
    /// <param name="pen">线条样式，为空时使用默认样式</param>
    public void DrawSelf(string name, Pen? pen = null)
    {
        // 相对自己是 0, 0 坐标
        DrawRect(0, 0, Width, Height, name, pen);
    }

    /// <summary>
    /// 直接在遮罩窗口绘制当前区域下的【指定区域】
    /// </summary>
    public void DrawRect(int x, int y, int w, int h, string name, Pen? pen = null)
    {
        DrawingBoard.Set(name, ToMaskWindowDrawingRect(x, y, w, h, pen));
    }

    public void DrawRect(Rect rect, string name, Pen? pen = null)
    {
        DrawingBoard.Set(name, ToMaskWindowDrawingRect(rect.X, rect.Y, rect.Width, rect.Height, pen));
    }

    /// <summary>
    /// 转换【自己】到遮罩窗口绘制矩形
    /// </summary>
    public MaskWindowDrawingRect SelfToMaskWindowDrawingRect(Pen? pen = null)
    {
        // 相对自己是 0, 0 坐标
        return ToMaskWindowDrawingRect(0, 0, Width, Height, pen);
    }

    /// <summary>
    /// 转换【指定区域】到遮罩窗口绘制矩形
    /// </summary>
    public MaskWindowDrawingRect ToMaskWindowDrawingRect(Rect rect, Pen? pen = null)
    {
        return ToMaskWindowDrawingRect(rect.X, rect.Y, rect.Width, rect.Height, pen);
    }

    /// <summary>
    /// 转换【指定区域】到遮罩窗口绘制矩形，坐标为游戏捕获区域的物理像素
    /// </summary>
    /// <exception cref="Exception">当前区域不在游戏捕获区域树下</exception>
    public MaskWindowDrawingRect ToMaskWindowDrawingRect(int x, int y, int w, int h, Pen? pen = null)
    {
        var res = ConvertRes<GameCaptureRegion>.ConvertPositionToTargetRegion(x, y, w, h, this);
        return new MaskWindowDrawingRect(
            new System.Windows.Rect(res.X, res.Y, res.Width, res.Height),
            MaskWindowDrawingStroke.FromPen(pen));
    }

    /// <summary>
    /// 转换【指定直线】到遮罩窗口绘制直线，坐标为游戏捕获区域的物理像素
    /// </summary>
    public MaskWindowDrawingLine ToMaskWindowDrawingLine(int x1, int y1, int x2, int y2, Pen? pen = null)
    {
        var res1 = ConvertRes<GameCaptureRegion>.ConvertPositionToTargetRegion(x1, y1, 0, 0, this);
        var res2 = ConvertRes<GameCaptureRegion>.ConvertPositionToTargetRegion(x2, y2, 0, 0, this);
        return new MaskWindowDrawingLine(
            new System.Windows.Point(res1.X, res1.Y),
            new System.Windows.Point(res2.X, res2.Y),
            MaskWindowDrawingStroke.FromPen(pen));
    }

    public void DrawLine(int x1, int y1, int x2, int y2, string name, Pen? pen = null)
    {
        DrawingBoard.Set(name, ToMaskWindowDrawingLine(x1, y1, x2, y2, pen));
    }

    public Rect ConvertSelfPositionToGameCaptureRegion()
    {
        return ConvertPositionToGameCaptureRegion(X, Y, Width, Height);
    }

    public Rect ConvertPositionToGameCaptureRegion(int x, int y, int w, int h)
    {
        var res = ConvertRes<GameCaptureRegion>.ConvertPositionToTargetRegion(x, y, w, h, this);
        return res.ToRect();
    }

    public (int, int) ConvertPositionToGameCaptureRegion(int x, int y)
    {
        var res = ConvertRes<GameCaptureRegion>.ConvertPositionToTargetRegion(x, y, 0, 0, this);
        return (res.X, res.Y);
    }

    public (int, int) ConvertPositionToDesktopRegion(int x, int y)
    {
        var res = ConvertRes<DesktopRegion>.ConvertPositionToTargetRegion(x, y, 0, 0, this);
        return (res.X, res.Y);
    }

    public Rect ToRect()
    {
        return new Rect(X, Y, Width, Height);
    }

    /// <summary>
    /// 生成一个新的区域
    /// 请使用 using var newRegion
    /// </summary>
    /// <returns></returns>
    public ImageRegion ToImageRegion()
    {
        if (this is ImageRegion imageRegion)
        {
            Debug.WriteLine("ToImageRegion 但已经是 ImageRegion");
            return imageRegion;
        }

        var res = ConvertRes<ImageRegion>.ConvertPositionToTargetRegion(0, 0, Width, Height, this);
        var newRegion = new ImageRegion(new Mat(res.TargetRegion.SrcMat, res.ToRect()), X, Y, Prev, PrevConverter);
        return newRegion;
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

    public virtual void Dispose()
    {
        // 子节点全部释放
        // NextChildren?.ForEach(x => x.Dispose());
    }

    /// <summary>
    /// 派生一个点类型的区域
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public Region Derive(int x, int y)
    {
        return Derive(x, y, 0, 0);
    }

    /// <summary>
    /// 派生一个矩形类型的区域
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="w"></param>
    /// <param name="h"></param>
    /// <returns></returns>
    public Region Derive(int x, int y, int w, int h)
    {
        return new Region(x, y, w, h, this, new TranslationConverter(x, y), DrawingBoard);
    }

    public Region Derive(Rect rect)
    {
        return Derive(rect.X, rect.Y, rect.Width, rect.Height);
    }
}
