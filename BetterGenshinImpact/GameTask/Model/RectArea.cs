using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.Helpers.Extensions;
using OpenCvSharp;
using WindowsInput;
using static Vanara.PInvoke.Gdi32;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 屏幕上的某块矩形区域或者点
/// 一般层级如下：
/// 桌面 -> 窗口捕获区域 -> 窗口内的矩形区域 -> 矩形区域内识别到的图像区域
/// </summary>
[Serializable]
public class RectArea
{
    /// <summary>
    /// 当前所属的坐标系名称
    /// 顶层一定是桌面
    /// Desktop -> CaptureArea -> Part -> ?
    /// </summary>
    public string? CoordinateName { get; set; }

    /// <summary>
    /// 当前所属的坐标系层级
    /// 桌面 = 0
    /// </summary>
    public int CoordinateLevelNum { get; set; } = 0;

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public RectArea? Owner { get; set; }

    private Bitmap? _srcBitmap;
    private Mat? _srcMat;
    private Mat? _srcGreyMat;

    public Bitmap SrcBitmap
    {
        get
        {
            if (_srcBitmap != null)
            {
                return _srcBitmap;
            }

            if (_srcMat == null)
            {
                throw new Exception("SrcBitmap和SrcMat不能同时为空");
            }

            _srcBitmap = _srcMat.ToBitmap();
            return _srcBitmap;
        }
    }

    public Mat SrcMat
    {
        get
        {
            if (_srcMat != null)
            {
                return _srcMat;
            }

            if (_srcBitmap == null)
            {
                throw new Exception("SrcBitmap和SrcMat不能同时为空");
            }

            _srcMat = _srcBitmap.ToMat();
            return _srcMat;
        }
    }

    public Mat SrcGreyMat
    {
        get
        {
            _srcGreyMat ??= new Mat();
            Cv2.CvtColor(SrcMat, _srcGreyMat, ColorConversionCodes.BGR2GRAY);
            return _srcGreyMat;
        }
    }

    public RectArea()
    {
    }

    public RectArea(int x, int y, RectArea? owner = null)
    {
        X = x;
        Y = y;
        Width = 0;
        Height = 0;
        Owner = owner;
        CoordinateLevelNum = owner?.CoordinateLevelNum + 1 ?? 0;
    }

    public RectArea(Bitmap bitmap, int x, int y, RectArea? owner = null) : this(x, y, owner)
    {
        _srcBitmap = bitmap;
    }

    public RectArea(Mat mat, int x, int y, RectArea? owner = null) : this(x, y, owner)
    {
        _srcMat = mat;
    }

    public RectArea(Mat mat, Point p, RectArea? owner = null) : this(mat, p.X, p.Y, owner)
    {
    }


    public RectArea(Mat mat, RectArea? owner = null)
    {
        _srcMat = mat;
        X = 0;
        Y = 0;
        Width = mat.Width;
        Height = mat.Height;
        Owner = owner;
        CoordinateLevelNum = owner?.CoordinateLevelNum + 1 ?? 0;
    }

    public Rect ConvertRelativePositionTo(int coordinateLevelNum)
    {
        int newX = X, newY = Y;
        var father = Owner;
        while (true)
        {
            if (father == null)
            {
                throw new Exception("找不到对应的坐标系");
            }

            if (father.CoordinateLevelNum == coordinateLevelNum)
            {
                break;
            }

            newX += father.X;
            newY += father.Y;

            father = father.Owner;
        }

        return new Rect(newX, newY, Width, Height);
    }

    public Rect ConvertRelativePositionToDesktop()
    {
        return ConvertRelativePositionTo(0);
    }

    public Rect ToRect()
    {
        return new Rect(X, Y, Width, Height);
    }

    public bool PositionIsInDesktop()
    {
        return CoordinateLevelNum == 0;
    }

    public bool IsEmpty()
    {
        return Width == 0 || Height == 0;
    }

    public bool HasImage()
    {
        return _srcBitmap != null || _srcMat != null;
    }

    /// <summary>
    /// 在本区域内查找目标图像
    /// </summary>
    /// <param name="targetImageMat"></param>
    /// <returns></returns>
    public RectArea Find(Mat targetImageMat)
    {
        if (!HasImage())
        {
            throw new Exception("当前对象内没有图像内容，无法完成 Find 操作");
        }
        var p = MatchTemplateHelper.FindSingleTarget(SrcGreyMat, targetImageMat);
        return p is { X: > 0, Y: > 0 } ? new RectArea(targetImageMat, p.X - targetImageMat.Width / 2, p.Y - targetImageMat.Height / 2, this) : new RectArea();
    }

    /// <summary>
    /// 找到图像并点击中心
    /// </summary>
    /// <param name="targetImageMat"></param>
    /// <returns></returns>
    public RectArea ClickCenter(Mat targetImageMat)
    {
        var ra = Find(targetImageMat);
        if (!ra.IsEmpty())
        {
            ra.ClickCenter();
        }

        return ra;
    }

    /// <summary>
    /// 当前对象点击中心
    /// </summary>
    public void ClickCenter()
    {
        // 把坐标系转换到桌面再点击
        if (CoordinateLevelNum == 0)
        {
            ToRect().ClickCenter();
        }
        else
        {
            ConvertRelativePositionToDesktop().ClickCenter();
        }
    }


    /// <summary>
    /// 剪裁图片
    /// </summary>
    /// <param name="rect"></param>
    /// <returns></returns>
    public RectArea Crop(Rect rect)
    {
        return new RectArea(SrcMat[rect], this);
    }
}