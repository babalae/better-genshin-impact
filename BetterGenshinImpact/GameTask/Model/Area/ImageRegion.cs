using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Image = SixLabors.ImageSharp.Image;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.GameTask.Model.Area;

public class ImageRegion : Region
{
    private Mat? _cacheGreyMat;
    private Image<Rgb24>? _cacheImage;

    public Mat SrcMat { get; }

    public Mat CacheGreyMat
    {
        get
        {
            if (_cacheGreyMat != null)
                return _cacheGreyMat;
            _cacheGreyMat = new Mat();
            Cv2.CvtColor(SrcMat, _cacheGreyMat, ColorConversionCodes.BGR2GRAY);
            return _cacheGreyMat;
        }
    }

    public unsafe Image<Rgb24> CacheImage
    {
        get
        {
            if (_cacheImage != null)
                return _cacheImage;

            using var mat = SrcMat.CvtColor(ColorConversionCodes.BGR2RGB);
            var bufferSize = (int)SrcMat.Step() * SrcMat.Height;
            using var image = Image.WrapMemory<Rgb24>(mat.DataPointer, bufferSize, mat.Width, mat.Height);
            _cacheImage = image.Clone();

            return _cacheImage;
        }
    }

    public ImageRegion(Mat mat, int x, int y, Region? owner = null, INodeConverter? converter = null,
        DrawContent? drawContent = null) : base(x, y, mat.Width, mat.Height, owner, converter, drawContent)
    {
        SrcMat = mat;
    }

    /// <summary>
    /// 剪裁派生
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="w"></param>
    /// <param name="h"></param>
    /// <returns></returns>
    public ImageRegion DeriveCrop(int x, int y, int w, int h)
    {
        var rect = new Rect(x, y, w, h).ClampTo(SrcMat);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), $"DeriveCrop 裁剪区域无效: ({x},{y},{w},{h})，图像大小: {SrcMat.Cols}x{SrcMat.Rows}");
        }
        return new ImageRegion(new Mat(SrcMat, rect), rect.X, rect.Y, this, new TranslationConverter(rect.X, rect.Y));
    }

    public ImageRegion DeriveCrop(double dx, double dy, double dw, double dh)
    {
        var x = (int)Math.Round(dx);
        var y = (int)Math.Round(dy);
        var w = (int)Math.Round(dw);
        var h = (int)Math.Round(dh);
        var rect = new Rect(x, y, w, h).ClampTo(SrcMat);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), $"DeriveCrop 裁剪区域无效: ({x},{y},{w},{h})，图像大小: {SrcMat.Cols}x{SrcMat.Rows}");
        }
        return new ImageRegion(new Mat(SrcMat, rect), rect.X, rect.Y, this, new TranslationConverter(rect.X, rect.Y));
    }

    public ImageRegion DeriveCrop(Rect rect)
    {
        return DeriveCrop(rect.X, rect.Y, rect.Width, rect.Height);
    }

    // public ImageRegion Derive(Mat mat, int x, int y)
    // {
    //     return new ImageRegion(mat, x, y, this, new TranslationConverter(x, y));
    // }

    /// <summary>
    /// 在本区域内查找最优识别对象
    /// 或者对该区域进行识别
    /// 匹配
    /// RecognitionTypes.TemplateMatch
    /// RecognitionTypes.OcrMatch
    /// 识别
    /// RecognitionTypes.Ocr
    /// </summary>
    /// <param name="ro"></param>
    /// <param name="successAction">成功找到后做什么</param>
    /// <param name="failAction">失败后做什么</param>
    /// <returns>返回最优的一个识别结果RectArea</returns>
    /// <exception cref="Exception"></exception>
    public Region Find(RecognitionObject ro, Action<Region>? successAction = null, Action? failAction = null)
    {
        if (ro == null)
        {
            throw new Exception("识别对象不能为null");
        }

        if (RecognitionTypes.TemplateMatch.Equals(ro.RecognitionType))
        {
            Mat roi;
            Mat? template;
            if (ro.Use3Channels)
            {
                template = ro.TemplateImageMat;
                roi = SrcMat;
                Cv2.CvtColor(roi, roi, ColorConversionCodes.BGRA2BGR);
            }
            else
            {
                if (ro.UseBinaryMatch)
                {
                    roi = new Mat();
                    Cv2.Threshold(CacheGreyMat, roi, ro.BinaryThreshold, 255, ThresholdTypes.Binary);
                }
                else
                {
                    roi = CacheGreyMat;
                }
                
                template = ro.TemplateImageGreyMat;
            }

            if (template == null)
            {
                throw new Exception($"[TemplateMatch]识别对象{ro.Name}的模板图片不能为null");
            }

            if (ro.RegionOfInterest != default)
            {
                // TODO roi 是可以加缓存的
                if (!(0 <= ro.RegionOfInterest.X && 0 <= ro.RegionOfInterest.Width &&
                      ro.RegionOfInterest.X + ro.RegionOfInterest.Width <= roi.Cols
                      && 0 <= ro.RegionOfInterest.Y && 0 <= ro.RegionOfInterest.Height &&
                      ro.RegionOfInterest.Y + ro.RegionOfInterest.Height <= roi.Rows))
                {
                    TaskControl.Logger.LogError("在图像{W1}x{H1}中查找模板,名称：{Name},ROI位置{X2}x{Y2},区域{H2}x{W2},边界溢出！",
                        roi.Width, roi.Height, ro.Name, ro.RegionOfInterest.X, ro.RegionOfInterest.Y,
                        ro.RegionOfInterest.Width, ro.RegionOfInterest.Height);
                }

                roi = new Mat(roi, ro.RegionOfInterest);
            }

            var p = MatchTemplateHelper.MatchTemplate(roi, template, ro.TemplateMatchMode, ro.MaskMat, ro.Threshold);
            if (p != new Point())
            {
                var newRa = Derive(p.X + ro.RegionOfInterest.X, p.Y + ro.RegionOfInterest.Y, template.Width,
                    template.Height);
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    newRa.DrawSelf(ro.Name, ro.DrawOnWindowPen);
                }

                successAction?.Invoke(newRa);
                return newRa;
            }
            else
            {
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    drawContent.RemoveRect(ro.Name);
                }

                failAction?.Invoke();
                return new Region();
            }
        }
        else if (RecognitionTypes.OcrMatch.Equals(ro.RecognitionType))
        {
            if (ro.AllContainMatchText.Count == 0 && ro.OneContainMatchText.Count == 0 && ro.RegexMatchText.Count == 0)
            {
                throw new Exception($"[OCR]识别对象{ro.Name}的匹配文本不能全为空");
            }

            var roi = SrcMat;
            if (ro.RegionOfInterest != default)
            {
                roi = new Mat(SrcMat, ro.RegionOfInterest);
            }

            var result = OcrFactory.Paddle.OcrResult(roi);
            var text = StringUtils.RemoveAllSpace(result.Text);
            // 替换可能出错的文本
            foreach (var entry in ro.ReplaceDictionary)
            {
                foreach (var replaceStr in entry.Value)
                {
                    text = text.Replace(replaceStr, entry.Key);
                }
            }

            int successContainCount = 0, successRegexCount = 0;
            bool successOneContain = false;
            // 包含匹配 全部包含才成功
            foreach (var s in ro.AllContainMatchText)
            {
                if (text.Contains(s))
                {
                    successContainCount++;
                }
            }

            // 包含匹配 包含一个就成功
            foreach (var s in ro.OneContainMatchText)
            {
                if (text.Contains(s))
                {
                    successOneContain = true;
                    break;
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

            if (successContainCount == ro.AllContainMatchText.Count
                && successRegexCount == ro.RegexMatchText.Count
                && (ro.OneContainMatchText.Count == 0 || successOneContain))
            {
                var newRa = Derive(ro.RegionOfInterest);
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    // 画出OCR识别到的区域
                    var drawList = result.Regions.Select(item =>
                        this.ToRectDrawable(item.Rect.BoundingRect() + ro.RegionOfInterest.Location, ro.Name,
                            ro.DrawOnWindowPen)).ToList();
                    drawContent.PutOrRemoveRectList(ro.Name, drawList);
                }

                successAction?.Invoke(newRa);
                return newRa;
            }
            else
            {
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    drawContent.RemoveRect(ro.Name);
                }

                failAction?.Invoke();
                return new Region();
            }
        }
        else if (RecognitionTypes.Ocr.Equals(ro.RecognitionType) ||
                 RecognitionTypes.ColorRangeAndOcr.Equals(ro.RecognitionType))
        {
            Mat roi;
            if (RecognitionTypes.ColorRangeAndOcr.Equals(ro.RecognitionType))
            {
                roi = SrcMat;
                if (ro.RegionOfInterest != default)
                {
                    roi = new Mat(SrcMat, ro.RegionOfInterest);
                }

                roi = roi.Clone();
                if (ro.ColorConversionCode != ColorConversionCodes.BGRA2BGR)
                {
                    Cv2.CvtColor(roi, roi, ro.ColorConversionCode);
                }

                Cv2.InRange(roi, ro.LowerColor, ro.UpperColor, roi);
            }
            else
            {
                roi = SrcMat;
                if (ro.RegionOfInterest != default)
                {
                    roi = new Mat(SrcMat, ro.RegionOfInterest);
                }
            }

            var result = OcrFactory.Paddle.OcrResult(roi);
            var text = StringUtils.RemoveAllSpace(result.Text);

            if (!string.IsNullOrEmpty(text))
            {
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    // 画出OCR识别到的区域
                    var drawList = result.Regions.Select(item =>
                        this.ToRectDrawable(item.Rect.BoundingRect() + ro.RegionOfInterest.Location, ro.Name,
                            ro.DrawOnWindowPen)).ToList();
                    drawContent.PutOrRemoveRectList(ro.Name, drawList);
                }

                if (ro.RegionOfInterest != default)
                {
                    var newRa = Derive(ro.RegionOfInterest);
                    newRa.Text = text;
                    successAction?.Invoke(newRa);
                    return newRa;
                }
                else
                {
                    this.Text = text;
                    successAction?.Invoke(this);
                    return this;
                }
            }
            else
            {
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    drawContent.RemoveRect(ro.Name);
                }

                failAction?.Invoke();
                return new Region();
            }
        }
        else
        {
            throw new Exception($"ImageRegion不支持的识别类型{ro.RecognitionType}");
        }
    }

    /// <summary>
    /// 在本区域内查找识别对象
    /// 返回所有找到的结果
    /// 仅支持:
    /// RecognitionTypes.TemplateMatch
    /// RecognitionTypes.Ocr
    /// </summary>
    /// <param name="ro"></param>
    /// <param name="successAction">成功找到后做什么</param>
    /// <param name="failAction">失败后做什么</param>
    /// <returns>无内嵌图片的 RectArea List</returns>
    /// <exception cref="Exception"></exception>
    public List<Region> FindMulti(RecognitionObject ro, Action<List<Region>>? successAction = null,
        Action? failAction = null)
    {
        if (ro == null)
        {
            throw new Exception("识别对象不能为null");
        }

        if (RecognitionTypes.TemplateMatch.Equals(ro.RecognitionType))
        {
            Mat roi;
            Mat? template;
            if (ro.Use3Channels)
            {
                template = ro.TemplateImageMat;
                roi = SrcMat;
                Cv2.CvtColor(roi, roi, ColorConversionCodes.BGRA2BGR);
            }
            else
            {
                template = ro.TemplateImageGreyMat;
                roi = CacheGreyMat;
            }

            if (template == null)
            {
                throw new Exception($"[TemplateMatch]识别对象{ro.Name}的模板图片不能为null");
            }

            if (ro.RegionOfInterest != default)
            {
                roi = new Mat(roi, ro.RegionOfInterest);
            }

            var rectList =
                MatchTemplateHelper.MatchOnePicForOnePic(roi, template, ro.TemplateMatchMode, ro.MaskMat, ro.Threshold);
            if (rectList.Count > 0)
            {
                var resRaList = rectList.Select(r => this.Derive(r + ro.RegionOfInterest.Location)).ToList();

                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    VisionContext.Instance().DrawContent.PutOrRemoveRectList(ro.Name,
                        resRaList.Select(ra => ra.SelfToRectDrawable(ro.Name)).ToList());
                }

                successAction?.Invoke(resRaList);
                return resRaList;
            }
            else
            {
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    VisionContext.Instance().DrawContent.RemoveRect(ro.Name);
                }

                failAction?.Invoke();
                return [];
            }
        }
        else if (RecognitionTypes.Ocr.Equals(ro.RecognitionType))
        {
            var roi = SrcMat;
            if (ro.RegionOfInterest != default)
            {
                roi = new Mat(SrcMat, ro.RegionOfInterest);
            }

            var result = OcrFactory.Paddle.OcrResult(roi);

            if (result.Regions.Length > 0)
            {
                var resRaList = result.Regions.Select(r =>
                {
                    var newRa = this.Derive(r.Rect.BoundingRect() + ro.RegionOfInterest.Location);
                    newRa.Text = r.Text;
                    foreach (var entry in ro.ReplaceDictionary)
                    {
                        foreach (var replaceStr in entry.Value)
                        {
                            newRa.Text = newRa.Text.Replace(replaceStr, entry.Key);
                        }
                    }
                    return newRa;
                }).ToList();
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    // 画出OCR识别到的区域
                    var drawList = result.Regions.Select(item =>
                        this.ToRectDrawable(item.Rect.BoundingRect() + ro.RegionOfInterest.Location, ro.Name,
                            ro.DrawOnWindowPen)).ToList();
                    VisionContext.Instance().DrawContent.PutOrRemoveRectList(ro.Name, drawList);
                }

                successAction?.Invoke(resRaList);
                return resRaList;
            }
            else
            {
                if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
                {
                    VisionContext.Instance().DrawContent.RemoveRect(ro.Name);
                }

                failAction?.Invoke();
                return [];
            }
        }
        else
        {
            throw new Exception($"RectArea多目标识别不支持的识别类型{ro.RecognitionType}");
        }
    }

    public new void Dispose()
    {
        _cacheImage?.Dispose();
        _cacheGreyMat?.Dispose();
        SrcMat.Dispose();
    }
}
