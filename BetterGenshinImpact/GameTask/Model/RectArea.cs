// using BetterGenshinImpact.Core.Recognition;
// using BetterGenshinImpact.Core.Recognition.OCR;
// using BetterGenshinImpact.Core.Recognition.OpenCv;
// using BetterGenshinImpact.Helpers;
// using BetterGenshinImpact.Helpers.Extensions;
// using BetterGenshinImpact.View.Drawable;
// using OpenCvSharp;
// using OpenCvSharp.Extensions;
// using Sdcb.PaddleOCR;
// using System;
// using System.Collections.Generic;
// using System.Drawing;
// using System.Linq;
// using System.Text.RegularExpressions;
// using Point = OpenCvSharp.Point;
//
// namespace BetterGenshinImpact.GameTask.Model;
//
// /// <summary>
// /// 屏幕上的某块矩形区域或者点，方便用于识别以及坐标系转换
// /// 一般层级如下：
// /// 桌面 -> 窗口捕获区域 -> 窗口内的矩形区域 -> 矩形区域内识别到的图像区域
// /// </summary>
// [Serializable]
// public class RectArea : IDisposable
// {
//     /// <summary>
//     /// 当前所属的坐标系层级
//     /// 桌面 = 0
//     /// 游戏窗口 = 1
//     /// 顶层一定是桌面
//     /// Desktop -> GameCaptureArea -> Part -> ?
//     /// </summary>
//     public int CoordinateLevelNum { get; set; } = 0;
//
//     public int X { get; set; }
//     public int Y { get; set; }
//     public int Width { get; set; }
//     public int Height { get; set; }
//
//     public RectArea? Owner { get; set; }
//
//     private Bitmap? _srcBitmap;
//     private Mat? _srcMat;
//     private Mat? _srcGreyMat;
//
//     public Bitmap SrcBitmap
//     {
//         get
//         {
//             if (_srcBitmap != null)
//             {
//                 return _srcBitmap;
//             }
//
//             if (_srcMat == null)
//             {
//                 throw new Exception("SrcBitmap和SrcMat不能同时为空");
//             }
//
//             _srcBitmap = _srcMat.ToBitmap();
//             return _srcBitmap;
//         }
//     }
//
//     public Mat SrcMat
//     {
//         get
//         {
//             if (_srcMat != null)
//             {
//                 return _srcMat;
//             }
//
//             if (_srcBitmap == null)
//             {
//                 throw new Exception("SrcBitmap和SrcMat不能同时为空");
//             }
//
//             _srcMat = _srcBitmap.ToMat();
//             return _srcMat;
//         }
//     }
//
//     public Mat SrcGreyMat
//     {
//         get
//         {
//             _srcGreyMat ??= new Mat();
//             Cv2.CvtColor(SrcMat, _srcGreyMat, ColorConversionCodes.BGR2GRAY);
//             return _srcGreyMat;
//         }
//     }
//
//     /// <summary>
//     /// 存放OCR识别的结果文本
//     /// </summary>
//     public string Text { get; set; } = string.Empty;
//
//     public RectArea()
//     {
//     }
//
//     public RectArea(int x, int y, int width, int height, RectArea? owner = null)
//     {
//         X = x;
//         Y = y;
//         Width = width;
//         Height = height;
//         Owner = owner;
//         CoordinateLevelNum = owner?.CoordinateLevelNum + 1 ?? 0;
//     }
//
//     public RectArea(Bitmap bitmap, int x, int y, RectArea? owner = null) : this(x, y, 0, 0, owner)
//     {
//         _srcBitmap = bitmap;
//         Width = bitmap.Width;
//         Height = bitmap.Height;
//     }
//
//     public RectArea(Mat mat, int x, int y, RectArea? owner = null) : this(x, y, 0, 0, owner)
//     {
//         _srcMat = mat;
//         Width = mat.Width;
//         Height = mat.Height;
//     }
//
//     public RectArea(Mat mat, Point p, RectArea? owner = null) : this(mat, p.X, p.Y, owner)
//     {
//     }
//
//     public RectArea(Rect rect, RectArea? owner = null) : this(rect.X, rect.Y, rect.Width, rect.Height, owner)
//     {
//     }
//
//     //public RectArea(Mat mat, RectArea? owner = null)
//     //{
//     //    _srcMat = mat;
//     //    X = 0;
//     //    Y = 0;
//     //    Width = mat.Width;
//     //    Height = mat.Height;
//     //    Owner = owner;
//     //    CoordinateLevelNum = owner?.CoordinateLevelNum + 1 ?? 0;
//     //}
//
//     public Rect ConvertRelativePositionTo(int coordinateLevelNum)
//     {
//         int newX = X, newY = Y;
//         var father = Owner;
//         while (true)
//         {
//             if (father == null)
//             {
//                 throw new Exception("找不到对应的坐标系");
//             }
//
//             if (father.CoordinateLevelNum == coordinateLevelNum)
//             {
//                 break;
//             }
//
//             newX += father.X;
//             newY += father.Y;
//
//             father = father.Owner;
//         }
//
//         return new Rect(newX, newY, Width, Height);
//     }
//
//     public Rect ConvertRelativePositionToDesktop()
//     {
//         return ConvertRelativePositionTo(0);
//     }
//
//     public Rect ConvertRelativePositionToCaptureArea()
//     {
//         return ConvertRelativePositionTo(1);
//     }
//
//     public Rect ToRect()
//     {
//         return new Rect(X, Y, Width, Height);
//     }
//
//     public bool PositionIsInDesktop()
//     {
//         return CoordinateLevelNum == 0;
//     }
//
//     public bool IsEmpty()
//     {
//         return Width == 0 && Height == 0 && X == 0 && Y == 0;
//     }
//
//     /// <summary>
//     /// 语义化包装
//     /// </summary>
//     /// <returns></returns>
//     public bool IsExist()
//     {
//         return !IsEmpty();
//     }
//
//     public bool HasImage()
//     {
//         return _srcBitmap != null || _srcMat != null;
//     }
//
//     /// <summary>
//     /// 在本区域内查找最优识别对象
//     /// </summary>
//     /// <param name="ro"></param>
//     /// <param name="successAction">成功找到后做什么</param>
//     /// <param name="failAction">失败后做什么</param>
//     /// <returns>返回最优的一个识别结果RectArea</returns>
//     /// <exception cref="Exception"></exception>
//     public RectArea Find(RecognitionObject ro, Action<RectArea>? successAction = null, Action? failAction = null)
//     {
//         if (!HasImage())
//         {
//             throw new Exception("当前对象内没有图像内容，无法完成 Find 操作");
//         }
//
//         if (ro == null)
//         {
//             throw new Exception("识别对象不能为null");
//         }
//
//         if (RecognitionTypes.TemplateMatch.Equals(ro.RecognitionType))
//         {
//             Mat roi;
//             Mat? template;
//             if (ro.Use3Channels)
//             {
//                 template = ro.TemplateImageMat;
//                 roi = SrcMat;
//                 Cv2.CvtColor(roi, roi, ColorConversionCodes.BGRA2BGR);
//             }
//             else
//             {
//                 template = ro.TemplateImageGreyMat;
//                 roi = SrcGreyMat;
//             }
//
//             if (template == null)
//             {
//                 throw new Exception($"[TemplateMatch]识别对象{ro.Name}的模板图片不能为null");
//             }
//
//             if (ro.RegionOfInterest != Rect.Empty)
//             {
//                 // TODO roi 是可以加缓存的
//                 // if (!(0 <= ro.RegionOfInterest.X && 0 <= ro.RegionOfInterest.Width && ro.RegionOfInterest.X + ro.RegionOfInterest.Width <= roi.Cols
//                 //       && 0 <= ro.RegionOfInterest.Y && 0 <= ro.RegionOfInterest.Height && ro.RegionOfInterest.Y + ro.RegionOfInterest.Height <= roi.Rows))
//                 // {
//                 //     Logger.LogError("输入图像{W1}x{H1},模板ROI位置{X2}x{Y2},区域{H2}x{W2},边界溢出！", roi.Width, roi.Height, ro.RegionOfInterest.X, ro.RegionOfInterest.Y, ro.RegionOfInterest.Width, ro.RegionOfInterest.Height);
//                 // }
//                 roi = new Mat(roi, ro.RegionOfInterest);
//             }
//
//             var p = MatchTemplateHelper.MatchTemplate(roi, template, ro.TemplateMatchMode, ro.MaskMat, ro.Threshold);
//             if (p != new Point())
//             {
//                 var newRa = new RectArea(template.Clone(), p.X + ro.RegionOfInterest.X, p.Y + ro.RegionOfInterest.Y, this);
//                 if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
//                 {
//                     VisionContext.Instance().DrawContent.PutRect(ro.Name, newRa
//                         .ConvertRelativePositionToCaptureArea()
//                         .ToRectDrawable(ro.DrawOnWindowPen, ro.Name));
//                 }
//
//                 successAction?.Invoke(newRa);
//                 return newRa;
//             }
//             else
//             {
//                 if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
//                 {
//                     VisionContext.Instance().DrawContent.RemoveRect(ro.Name);
//                 }
//
//                 failAction?.Invoke();
//                 return new RectArea();
//             }
//         }
//         else if (RecognitionTypes.OcrMatch.Equals(ro.RecognitionType))
//         {
//             if (ro.AllContainMatchText.Count == 0 && ro.OneContainMatchText.Count == 0 && ro.RegexMatchText.Count == 0)
//             {
//                 throw new Exception($"[OCR]识别对象{ro.Name}的匹配文本不能全为空");
//             }
//
//             var roi = SrcGreyMat;
//             if (ro.RegionOfInterest != Rect.Empty)
//             {
//                 roi = new Mat(SrcGreyMat, ro.RegionOfInterest);
//             }
//
//             var result = OcrFactory.Paddle.OcrResult(roi);
//             var text = StringUtils.RemoveAllSpace(result.Text);
//             // 替换可能出错的文本
//             foreach (var entry in ro.ReplaceDictionary)
//             {
//                 foreach (var replaceStr in entry.Value)
//                 {
//                     text = text.Replace(replaceStr, entry.Key);
//                 }
//             }
//
//             int successContainCount = 0, successRegexCount = 0;
//             bool successOneContain = false;
//             // 包含匹配 全部包含才成功
//             foreach (var s in ro.AllContainMatchText)
//             {
//                 if (text.Contains(s))
//                 {
//                     successContainCount++;
//                 }
//             }
//
//             // 包含匹配 包含一个就成功
//             foreach (var s in ro.OneContainMatchText)
//             {
//                 if (text.Contains(s))
//                 {
//                     successOneContain = true;
//                     break;
//                 }
//             }
//
//             // 正则匹配
//             foreach (var re in ro.RegexMatchText)
//             {
//                 if (Regex.IsMatch(text, re))
//                 {
//                     successRegexCount++;
//                 }
//             }
//
//             if (successContainCount == ro.AllContainMatchText.Count
//                 && successRegexCount == ro.RegexMatchText.Count
//                 && (ro.OneContainMatchText.Count == 0 || successOneContain))
//             {
//                 var newRa = new RectArea(roi, X + ro.RegionOfInterest.X, Y + ro.RegionOfInterest.Y, this);
//                 if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
//                 {
//                     VisionContext.Instance().DrawContent.PutOrRemoveRectList(ro.Name, result.ToRectDrawableListOffset(ro.RegionOfInterest.X, ro.RegionOfInterest.Y));
//                 }
//
//                 successAction?.Invoke(newRa);
//                 return newRa;
//             }
//             else
//             {
//                 if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
//                 {
//                     VisionContext.Instance().DrawContent.RemoveRect(ro.Name);
//                 }
//
//                 failAction?.Invoke();
//                 return new RectArea();
//             }
//         }
//         else
//         {
//             throw new Exception($"RectArea不支持的识别类型{ro.RecognitionType}");
//         }
//     }
//
//     /// <summary>
//     /// 在本区域内查找识别对象
//     /// 返回所有找到的结果
//     /// 仅支持:
//     /// RecognitionTypes.TemplateMatch
//     /// RecognitionTypes.Ocr
//     /// </summary>
//     /// <param name="ro"></param>
//     /// <param name="successAction">成功找到后做什么</param>
//     /// <param name="failAction">失败后做什么</param>
//     /// <returns>无内嵌图片的 RectArea List</returns>
//     /// <exception cref="Exception"></exception>
//     public List<RectArea> FindMulti(RecognitionObject ro, Action<List<RectArea>>? successAction = null, Action? failAction = null)
//     {
//         if (!HasImage())
//         {
//             throw new Exception("当前对象内没有图像内容，无法完成 Find 操作");
//         }
//
//         if (ro == null)
//         {
//             throw new Exception("识别对象不能为null");
//         }
//
//         if (RecognitionTypes.TemplateMatch.Equals(ro.RecognitionType))
//         {
//             Mat roi;
//             Mat? template;
//             if (ro.Use3Channels)
//             {
//                 template = ro.TemplateImageMat;
//                 roi = SrcMat;
//                 Cv2.CvtColor(roi, roi, ColorConversionCodes.BGRA2BGR);
//             }
//             else
//             {
//                 template = ro.TemplateImageGreyMat;
//                 roi = SrcGreyMat;
//             }
//
//             if (template == null)
//             {
//                 throw new Exception($"[TemplateMatch]识别对象{ro.Name}的模板图片不能为null");
//             }
//
//             if (ro.RegionOfInterest != Rect.Empty)
//             {
//                 // TODO roi 是可以加缓存的
//                 roi = new Mat(roi, ro.RegionOfInterest);
//             }
//
//             var rectList = MatchTemplateHelper.MatchOnePicForOnePic(roi, template, ro.TemplateMatchMode, ro.MaskMat, ro.Threshold);
//             if (rectList.Count > 0)
//             {
//                 var resRaList = rectList.Select(r => this.Derive(r + new Point(ro.RegionOfInterest.X, ro.RegionOfInterest.Y))).ToList();
//
//                 if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
//                 {
//                     VisionContext.Instance().DrawContent.PutOrRemoveRectList(ro.Name,
//                         resRaList.Select(ra => ra.ConvertRelativePositionToCaptureArea()
//                             .ToRectDrawable(ro.DrawOnWindowPen, ro.Name)).ToList());
//                 }
//
//                 successAction?.Invoke(resRaList);
//                 return resRaList;
//             }
//             else
//             {
//                 if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
//                 {
//                     VisionContext.Instance().DrawContent.RemoveRect(ro.Name);
//                 }
//
//                 failAction?.Invoke();
//                 return [];
//             }
//         }
//         else if (RecognitionTypes.Ocr.Equals(ro.RecognitionType))
//         {
//             var roi = SrcGreyMat;
//             if (ro.RegionOfInterest != Rect.Empty)
//             {
//                 roi = new Mat(SrcGreyMat, ro.RegionOfInterest);
//             }
//
//             var result = OcrFactory.Paddle.OcrResult(roi);
//
//             if (result.Regions.Length > 0)
//             {
//                 var resRaList = result.Regions.Select(r =>
//                 {
//                     var newRa = this.Derive(r.Rect.BoundingRect() + new Point(ro.RegionOfInterest.X, ro.RegionOfInterest.Y));
//                     newRa.Text = r.Text;
//                     return newRa;
//                 }).ToList();
//                 if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
//                 {
//                     VisionContext.Instance().DrawContent.PutOrRemoveRectList(ro.Name, result.ToRectDrawableListOffset(ro.RegionOfInterest.X, ro.RegionOfInterest.Y));
//                 }
//
//                 successAction?.Invoke(resRaList);
//                 return resRaList;
//             }
//             else
//             {
//                 if (ro.DrawOnWindow && !string.IsNullOrEmpty(ro.Name))
//                 {
//                     VisionContext.Instance().DrawContent.RemoveRect(ro.Name);
//                 }
//
//                 failAction?.Invoke();
//                 return [];
//             }
//         }
//         else
//         {
//             throw new Exception($"RectArea多目标识别不支持的识别类型{ro.RecognitionType}");
//         }
//     }
//
//     /// <summary>
//     /// 找到识别对象并点击中心
//     /// </summary>
//     /// <param name="ro"></param>
//     /// <returns></returns>
//     public RectArea ClickCenter(RecognitionObject ro)
//     {
//         var ra = Find(ro);
//         if (!ra.IsEmpty())
//         {
//             ra.ClickCenter();
//         }
//
//         return ra;
//     }
//
//     /// <summary>
//     /// 当前对象点击中心
//     /// </summary>
//     public void ClickCenter()
//     {
//         // 把坐标系转换到桌面再点击
//         if (CoordinateLevelNum == 0)
//         {
//             ToRect().ClickCenter();
//         }
//         else
//         {
//             ConvertRelativePositionToDesktop().ClickCenter();
//         }
//     }
//
//     /// <summary>
//     /// 剪裁图片
//     /// </summary>
//     /// <param name="rect"></param>
//     /// <returns></returns>
//     public RectArea Crop(Rect rect)
//     {
//         return new RectArea(SrcMat[rect], rect.X, rect.Y, this);
//     }
//
//     /// <summary>
//     /// 派生区域（无图片）
//     /// </summary>
//     /// <param name="rect"></param>
//     /// <returns></returns>
//     public RectArea Derive(Rect rect)
//     {
//         return new RectArea(rect, this);
//     }
//
//     /// <summary>
//     /// 派生2x2区域（无图片）
//     /// 方便用于点击
//     /// </summary>
//     /// <param name="x"></param>
//     /// <param name="y"></param>
//     /// <returns></returns>
//     public RectArea DerivePoint(int x, int y)
//     {
//         return new RectArea(new Rect(x, y, 2, 2), this);
//     }
//
//     /// <summary>
//     /// OCR识别
//     /// </summary>
//     /// <returns>所有结果</returns>
//     public PaddleOcrResult OcrResult()
//     {
//         return OcrFactory.Paddle.OcrResult(SrcGreyMat);
//     }
//
//     public void Dispose()
//     {
//         _srcGreyMat?.Dispose();
//         _srcMat?.Dispose();
//         _srcBitmap?.Dispose();
//     }
// }
