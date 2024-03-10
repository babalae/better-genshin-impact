using BetterGenshinImpact.GameTask.AutoSkip.Model;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public static class PaddleOcrResultExtension
{
    public static bool RegionHasText(this PaddleOcrResult result, string text)
    {
        foreach (var item in result.Regions)
            if (item.Text.Contains(text))
                return true;

        return false;
    }

    public static PaddleOcrResultRegion FindRegionByText(this PaddleOcrResult result, string text)
    {
        foreach (var item in result.Regions)
            if (item.Text.Contains(text))
                return item;

        return new PaddleOcrResultRegion();
    }

    //public static RotatedRect FindRotatedRectByText(this PaddleOcrResult result, string text)
    //{
    //    foreach (var item in result.Regions)
    //    {
    //        if (item.Text.Contains(text))
    //        {
    //            return item.Rect;
    //        }
    //    }

    //    return new RotatedRect();
    //}

    public static Rect FindRectByText(this PaddleOcrResult result, string text)
    {
        foreach (var item in result.Regions)
            if (item.Text.Contains(text))
                return item.Rect.BoundingRect();

        return Rect.Empty;
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
