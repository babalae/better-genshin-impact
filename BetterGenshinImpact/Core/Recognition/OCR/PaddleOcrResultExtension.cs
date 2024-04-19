using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public static class PaddleOcrResultExtension
{
    public static bool RegionHasText(this PaddleOcrResult result, ReadOnlySpan<char> text)
    {
        foreach (ref readonly PaddleOcrResultRegion item in result.Regions.AsSpan())
        {
            if (item.Text.AsSpan().Contains(text, StringComparison.InvariantCulture))
            {
                return true;
            }
        }

        return false;
    }

    public static PaddleOcrResultRegion FindRegionByText(this PaddleOcrResult result, ReadOnlySpan<char> text)
    {
        foreach (ref readonly PaddleOcrResultRegion item in result.Regions.AsSpan())
        {
            if (item.Text.AsSpan().Contains(text, StringComparison.InvariantCulture))
            {
                return item;
            }
        }

        return default;
    }

    public static Rect FindRectByText(this PaddleOcrResult result, string text)
    {
        foreach (ref PaddleOcrResultRegion item in result.Regions.AsSpan())
        {
            if (item.Text.Contains(text))
            {
                return item.Rect.BoundingRect();
            }
        }

        return default;
    }

    public static List<RectDrawable> ToRectDrawableList(this PaddleOcrResult result, Pen? pen = null)
    {
        return result.Regions.Select(item => item.Rect.BoundingRect().ToRectDrawable(pen)).ToList();
    }

    public static List<RectDrawable> ToRectDrawableListOffset(this PaddleOcrResult result, int offsetX, int offsetY, Pen? pen = null)
    {
        return result.Regions.Select(item => item.Rect.BoundingRect().ToRectDrawable(offsetX, offsetY, pen)).ToList();
    }

    public static PaddleOcrResultRect ToOcrResultRect(this PaddleOcrResultRegion region)
    {
        return new PaddleOcrResultRect(region.Rect.BoundingRect(), region.Text, region.Score);
    }
}
