using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Drawing;
using System.Text.RegularExpressions;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 屏幕上的某块矩形区域或者点
/// 一般层级如下：
/// 桌面 -> 窗口捕获区域 -> 窗口内的矩形区域 -> 矩形区域内识别到的图像区域
/// </summary>
[Serializable]
public class RectArea : IDisposable
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

    public RectArea(int x, int y, int width, int height, RectArea? owner = null)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Owner = owner;
        CoordinateLevelNum = owner?.CoordinateLevelNum + 1 ?? 0;
    }

    public RectArea(Bitmap bitmap, int x, int y, RectArea? owner = null) : this(x, y, 0, 0, owner)
    {
        _srcBitmap = bitmap;
        Width = bitmap.Width;
        Height = bitmap.Height;
    }

    public RectArea(Mat mat, int x, int y, RectArea? owner = null) : this(x, y, 0, 0, owner)
    {
        _srcMat = mat;
        Width = mat.Width;
        Height = mat.Height;
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

    public Rect ConvertRelativePositionToCaptureArea()
    {
        return ConvertRelativePositionTo(1);
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
        return Width == 0 && Height == 0 && X == 0 && Y == 0;
    }

    public bool HasImage()
    {
        return _srcBitmap != null || _srcMat != null;
    }

    ///// <summary>
    ///// 在本区域内查找目标图像
    ///// </summary>
    ///// <param name="targetImageMat"></param>
    ///// <returns></returns>
    //[Obsolete]
    //public RectArea Find(Mat targetImageMat)
    //{
    //    if (!HasImage())
    //    {
    //        throw new Exception("当前对象内没有图像内容，无法完成 Find 操作");
    //    }

    //    var p = OldMatchTemplateHelper.FindSingleTarget(SrcGreyMat, targetImageMat);
    //    return p is { X: > 0, Y: > 0 } ? new RectArea(targetImageMat, p.X - targetImageMat.Width / 2, p.Y - targetImageMat.Height / 2, this) : new RectArea();
    //}

    /// <summary>
    /// 在本区域内查找识别对象
    /// </summary>
    /// <param name="ro"></param>
    /// <param name="action"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public RectArea Find(RecognitionObject ro, Action<RectArea>? action = null)
    {
        if (!HasImage())
        {
            throw new Exception("当前对象内没有图像内容，无法完成 Find 操作");
        }

        if (ro == null)
        {
            throw new Exception("识别对象不能为null");
        }

        if (RecognitionTypes.TemplateMatch.Equals(ro.RecognitionType))
        {
            if (ro.TemplateImageGreyMat == null)
            {
                throw new Exception($"[TemplateMatch]识别对象{ro.Name}的模板图片不能为null");
            }

            var roi = SrcGreyMat;
            if (ro.RegionOfInterest != Rect.Empty)
            {
                // TODO roi 是可以加缓存的
                roi = new Mat(SrcGreyMat, ro.RegionOfInterest);
            }

            var p = MatchTemplateHelper.MatchTemplate(roi, ro.TemplateImageGreyMat, ro.TemplateMatchMode, ro.MaskMat, ro.Threshold);
            if (p is { X: > 0, Y: > 0 })
            {
                var newRa = new RectArea(ro.TemplateImageGreyMat.Clone(), p.X + ro.RegionOfInterest.X, p.Y + ro.RegionOfInterest.Y, this);
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    VisionContext.Instance().DrawContent.PutRect(ro.Name, newRa
                        .ConvertRelativePositionToCaptureArea()
                        .ToRectDrawable(ro.DrawOnWindowPen, ro.Name));
                }

                action?.Invoke(newRa);
                return newRa;
            }
            else
            {
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    VisionContext.Instance().DrawContent.RemoveRect(ro.Name);
                }

                return new RectArea();
            }
        }
        else if (RecognitionTypes.Ocr.Equals(ro.RecognitionType))
        {
            if (ro.ContainMatchText.Count == 0 && ro.RegexMatchText.Count == 0)
            {
                throw new Exception($"[OCR]识别对象{ro.Name}的匹配文本不能全为空");
            }

            var roi = SrcMat;
            if (ro.RegionOfInterest != Rect.Empty)
            {
                roi = new Mat(SrcMat, ro.RegionOfInterest);
            }

            var text = OcrFactory.Paddle.Ocr(roi);
            text = StringUtils.RemoveAllSpace(text);
            // 替换可能出错的文本
            foreach (var entry in ro.ReplaceDictionary)
            {
                foreach (var replaceStr in entry.Value)
                {
                    text = text.Replace(entry.Key, replaceStr);
                }
            }

            // 包含匹配
            int successContainCount = 0, successRegexCount = 0;
            foreach (var s in ro.ContainMatchText)
            {
                if (text.Contains(s))
                {
                    successContainCount++;
                }
            }

            // 正则匹配
            foreach (var re in ro.RegexMatchText)
            {
                if (Regex.IsMatch(text, re))
                {
                    successRegexCount++;
                }
            }

            if (successContainCount == ro.ContainMatchText.Count && successRegexCount == ro.RegexMatchText.Count)
            {
                var newRa = new RectArea(roi, X + ro.RegionOfInterest.X, Y + ro.RegionOfInterest.Y, this);
                action?.Invoke(newRa);
                return newRa;
            }
            else
            {
                return new RectArea();
            }
        }
        else
        {
            throw new Exception($"RectArea不支持的识别类型{ro.RecognitionType}");
        }
    }

    /// <summary>
    /// 找到识别对象并点击中心
    /// </summary>
    /// <param name="ro"></param>
    /// <returns></returns>
    public RectArea ClickCenter(RecognitionObject ro)
    {
        var ra = Find(ro);
        if (!ra.IsEmpty())
        {
            ra.ClickCenter();
        }

        return ra;
    }

    ///// <summary>
    ///// 找到图像并点击中心
    ///// </summary>
    ///// <param name="targetImageMat"></param>
    ///// <returns></returns>
    //[Obsolete]
    //public RectArea ClickCenter(Mat targetImageMat)
    //{
    //    var ra = Find(targetImageMat);
    //    if (!ra.IsEmpty())
    //    {
    //        ra.ClickCenter();
    //    }

    //    return ra;
    //}

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

    public void Dispose()
    {
        _srcGreyMat?.Dispose();
        _srcMat?.Dispose();
        _srcBitmap?.Dispose();
    }
}