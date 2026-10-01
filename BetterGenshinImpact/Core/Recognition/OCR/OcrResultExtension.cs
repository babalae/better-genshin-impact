using System;
using BetterGenshinImpact.GameTask.AutoSkip.Model;
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

    public static PaddleOcrResultRect ToOcrResultRect(this OcrResultRegion region)
    {
        return new PaddleOcrResultRect(region.Rect.BoundingRect(), region.Text, region.Score);
    }
}