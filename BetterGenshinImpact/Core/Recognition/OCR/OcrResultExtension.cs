using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public static class OcrResultExtension
{
    public static bool RegionHasText(this OcrResult result, ReadOnlySpan<char> text)
    {
        foreach (ref readonly var item in result.Regions.AsSpan())
            if (item.Text.AsSpan().Contains(text, StringComparison.InvariantCulture))
                return true;

        return false;
    }

    public static OcrResultRegion FindRegionByText(this OcrResult result, ReadOnlySpan<char> text)
    {
        foreach (ref readonly var item in result.Regions.AsSpan())
            if (item.Text.AsSpan().Contains(text, StringComparison.InvariantCulture))
                return item;

        return default;
    }

    public static Rect FindRectByText(this OcrResult result, string text)
    {
        foreach (ref var item in result.Regions.AsSpan())
            if (item.Text.Contains(text))
                return item.Rect.BoundingRect();

        return default;
    }

    public static List<RectDrawable> ToRectDrawableList(this OcrResult result, Pen? pen = null)
    {
        return result.Regions.Select(item => item.Rect.BoundingRect().ToRectDrawable(pen)).ToList();
    }

    public static List<RectDrawable> ToRectDrawableListOffset(this OcrResult result, int offsetX, int offsetY,
        Pen? pen = null)
    {
        return result.Regions.Select(item => item.Rect.BoundingRect().ToRectDrawable(offsetX, offsetY, pen)).ToList();
    }

    public static PaddleOcrResultRect ToOcrResultRect(this OcrResultRegion region)
    {
        return new PaddleOcrResultRect(region.Rect.BoundingRect(), region.Text, region.Score);
    }
}