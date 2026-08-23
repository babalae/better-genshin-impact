using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Model.GameUI;

/// <summary>
/// 背包物品数量裁剪识别的图像处理参数。
/// </summary>
/// <param name="Scale">数量区域进入预处理流程前的放大倍数。</param>
/// <param name="MaxSaturation">HSV 掩码允许的最大饱和度，用于保留灰色数字并排除彩色图案。</param>
/// <param name="MaxValue">HSV 掩码允许的最大明度，用于提取深色数字前景。</param>
/// <param name="MinComponentArea">有效前景连通域的最小外接矩形面积。</param>
/// <param name="MinComponentHeight">有效前景连通域的最小高度。</param>
/// <param name="MinComponentBottomRatio">连通域底边相对数量区域高度的最小比例，用于排除上方噪点。</param>
/// <param name="NarrowOneMaxAspectRatio">单个窄连通域判定为数字 1 候选的最大宽高比。</param>
/// <param name="SevenMinAspectRatio">OCR 将数字 7 误读为 1 时，结构修正要求的最小宽高比。</param>
/// <param name="NormalizedHeight">送入无检测器 OCR 前的标准化数字高度。</param>
/// <param name="VerticalPadding">标准化画布在数字上下两侧保留的像素数。</param>
internal sealed record GridItemCountRecognizerOptions(
    int Scale = 3,
    int MaxSaturation = 140,
    int MaxValue = 210,
    int MinComponentArea = 20,
    int MinComponentHeight = 12,
    double MinComponentBottomRatio = 0.55,
    double NarrowOneMaxAspectRatio = 0.45,
    double SevenMinAspectRatio = 0.65,
    int NormalizedHeight = 48,
    int VerticalPadding = 6);

/// <summary>
/// 背包物品数量裁剪识别的结果及诊断信息。
/// </summary>
internal sealed class GridItemCountRecognitionResult : IDisposable
{
    /// <summary>
    /// 无检测器 OCR 返回的原始文本。
    /// </summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>
    /// 严格解析或结构修正后的数量；识别失败时为 -2。
    /// </summary>
    public int Count { get; init; } = -2;

    /// <summary>
    /// 通过过滤并参与整体前景计算的连通域数量。
    /// </summary>
    public int ComponentCount { get; init; }

    /// <summary>
    /// 所有有效数字前景合并后的外接矩形。
    /// </summary>
    public Rect ForegroundBounds { get; init; }

    /// <summary>
    /// 失败或结构修正原因；普通识别成功时为空。
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// 送入 OCR 的标准化数字图，由当前结果负责释放。
    /// </summary>
    public Mat? NormalizedImage { get; init; }

    /// <summary>
    /// 释放结果持有的标准化数字图。
    /// </summary>
    public void Dispose()
    {
        NormalizedImage?.Dispose();
    }
}

/// <summary>
/// 针对固定背包格子数量区域的裁剪 OCR 识别器。
/// </summary>
internal static class GridItemCountRecognizer
{
    /// <summary>
    /// 经实测使用的默认预处理参数。
    /// </summary>
    internal static readonly GridItemCountRecognizerOptions DefaultOptions = new();

    /// <summary>
    /// 提取并标准化物品格子底部的数字前景，再使用无检测器 OCR 识别数量。
    /// </summary>
    /// <param name="item">完整物品格子图像。</param>
    /// <param name="ocrService">提供无检测器识别能力的 OCR 服务。</param>
    /// <param name="options">可选的图像处理参数；为空时使用默认参数。</param>
    /// <returns>包含数量、OCR 原文、结构信息和标准化图片的识别结果。</returns>
    public static GridItemCountRecognitionResult RecognizeCropped(
        Mat item,
        IOcrService ocrService,
        GridItemCountRecognizerOptions? options = null)
    {
        options ??= DefaultOptions;

        // 固定裁剪格子底部的数量区域并放大，避免图标和星级区域干扰数字提取。
        using Mat resized = CropCountArea(item, options.Scale);
        using Mat hsv = resized.CvtColor(ColorConversionCodes.BGR2HSV);

        // 通过低饱和度、低明度条件提取深灰色数字；不做闭运算，避免相邻笔画粘连。
        using Mat mask = hsv.InRange(
            new Scalar(0, 0, 0),
            new Scalar(180, options.MaxSaturation, options.MaxValue));

        Point[][] contours = mask.FindContoursAsArray(
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        // 过滤过小、过矮或远离数量区域底部的轮廓，排除顶部标记和零散噪点。
        List<Rect> components = contours
            .Select(Cv2.BoundingRect)
            .Where(rect => rect.Width > 1
                           && rect.Height >= options.MinComponentHeight
                           && rect.Width * rect.Height >= options.MinComponentArea
                           && rect.Bottom >= mask.Height * options.MinComponentBottomRatio)
            .OrderBy(rect => rect.X)
            .ToList();

        if (components.Count == 0)
        {
            return new GridItemCountRecognitionResult { Reason = "EMPTY" };
        }

        // 合并数字前景并紧裁，随后反色、等比例缩放到固定高度。
        Rect bounds = Union(components);
        using Mat foreground = mask.SubMat(bounds);
        using Mat inverted = new();
        Cv2.BitwiseNot(foreground, inverted);
        int scaledWidth = Math.Max(1,
            (int)Math.Round((double)inverted.Width * options.NormalizedHeight / inverted.Height));
        using Mat scaled = inverted.Resize(
            new Size(scaledWidth, options.NormalizedHeight), interpolation: InterpolationFlags.Nearest);
        int horizontalPadding = options.NormalizedHeight / 2;
        // 在白色画布中增加稳定留白，避免窄字符紧贴 OCR 输入边缘。
        Mat normalized = new(
            options.NormalizedHeight + options.VerticalPadding * 2,
            scaledWidth + horizontalPadding * 2,
            MatType.CV_8UC1,
            Scalar.White);
        using (Mat target = normalized.SubMat(
                   options.VerticalPadding,
                   options.VerticalPadding + scaled.Height,
                   horizontalPadding,
                   horizontalPadding + scaled.Width))
        {
            scaled.CopyTo(target);
        }

        try
        {
            // 固定区域已经完成前景定位，直接调用无检测器 OCR，避免单字符 1 被检测阶段漏掉。
            string rawText = ocrService.OcrWithoutDetector(normalized);
            string normalizedText = NormalizeNumberText(rawText);
            bool isNarrowOne = components.Count == 1
                               && (double)bounds.Width / bounds.Height <= options.NarrowOneMaxAspectRatio;
            bool isWideSeven = components.Count == 1
                               && normalizedText == "1"
                               && (double)bounds.Width / bounds.Height >= options.SevenMinAspectRatio;

            // 单个窄竖形前景可确定为 1，用于纠正 OCR 补零或无法解析的结果。
            if (isNarrowOne && (!TryParseStrict(normalizedText, out int narrowCount)
                                || narrowCount != 1))
            {
                return new GridItemCountRecognitionResult
                {
                    RawText = rawText,
                    Count = 1,
                    ComponentCount = components.Count,
                    ForegroundBounds = bounds,
                    Reason = "NARROW_ONE",
                    NormalizedImage = normalized,
                };
            }

            if (!TryParseStrict(normalizedText, out int count))
            {
                return new GridItemCountRecognitionResult
                {
                    RawText = rawText,
                    ComponentCount = components.Count,
                    ForegroundBounds = bounds,
                    Reason = "PARSE",
                    NormalizedImage = normalized,
                };
            }

            // 单连通域明显较宽却被 OCR 识别为 1 时，按数字 7 的结构特征修正。
            if (isWideSeven)
            {
                return new GridItemCountRecognitionResult
                {
                    RawText = rawText,
                    Count = 7,
                    ComponentCount = components.Count,
                    ForegroundBounds = bounds,
                    Reason = "WIDE_SEVEN",
                    NormalizedImage = normalized,
                };
            }

            return new GridItemCountRecognitionResult
            {
                RawText = rawText,
                Count = count,
                ComponentCount = components.Count,
                ForegroundBounds = bounds,
                NormalizedImage = normalized,
            };
        }
        catch
        {
            // 结果尚未取得所有权时由当前方法负责清理异常路径。
            normalized.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 按背包格子固定比例裁剪底部数量区域并放大。
    /// </summary>
    /// <param name="item">完整物品格子图像。</param>
    /// <param name="scale">输出图像的放大倍数。</param>
    /// <returns>由调用方负责释放的数量区域图像。</returns>
    internal static Mat CropCountArea(Mat item, int scale)
    {
        using Mat countArea = item.SubMat(
            item.Height * 128 / 153,
            item.Height * 150 / 153,
            item.Width * 5 / 125,
            item.Width * 120 / 125);
        return countArea.Resize(new Size(countArea.Width * scale, countArea.Height * scale));
    }

    /// <summary>
    /// 将全角数字转换为半角后，严格解析完整的纯数字文本。
    /// </summary>
    /// <param name="text">待解析的 OCR 文本。</param>
    /// <param name="count">解析成功时得到的数量。</param>
    /// <returns>文本全部由数字组成且可转换为 <see cref="int"/> 时返回 true。</returns>
    internal static bool TryParseStrict(string text, out int count)
    {
        string normalized = NormalizeNumberText(text);
        if (normalized.Length == 0 || normalized.Any(c => c is < '0' or > '9'))
        {
            count = default;
            return false;
        }

        return int.TryParse(normalized, out count);
    }

    private static string NormalizeNumberText(string text)
    {
        return StringUtils.ConvertFullWidthNumToHalfWidth(text ?? string.Empty);
    }

    private static Rect Union(IReadOnlyList<Rect> rects)
    {
        int left = rects.Min(rect => rect.Left);
        int top = rects.Min(rect => rect.Top);
        int right = rects.Max(rect => rect.Right);
        int bottom = rects.Max(rect => rect.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

}
