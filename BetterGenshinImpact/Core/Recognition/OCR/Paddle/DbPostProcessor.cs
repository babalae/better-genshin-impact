using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

internal readonly record struct PaddleOcrDetectionBox(Point2f[] Points, float Score)
{
    public RotatedRect Rect => Cv2.MinAreaRect(Points);
}

/// <summary>
/// PaddleOCR DBPostProcess 的 C# 实现。
/// </summary>
internal sealed class DbPostProcessor(
    float threshold,
    float boxThreshold,
    int maxCandidates,
    float unclipRatio,
    int minSize,
    bool useDilation)
{
    public PaddleOcrDetectionBox[] Run(Mat pred, Size sourceSize)
    {
        using var bitmap = new Mat();
        Cv2.Compare(pred, threshold, bitmap, CmpType.GT);

        using var contourSource = new Mat();
        if (useDilation)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
            Cv2.Dilate(bitmap, contourSource, kernel);
        }
        else
        {
            bitmap.CopyTo(contourSource);
        }

        var contours = contourSource.FindContoursAsArray(RetrievalModes.List,
            ContourApproximationModes.ApproxSimple);
        var boxes = new List<PaddleOcrDetectionBox>(Math.Min(contours.Length, maxCandidates));

        foreach (var contour in contours.Take(maxCandidates))
        {
            var (points, shortSide) = GetMiniBox(contour.Select(point => new Point2f(point.X, point.Y)).ToArray());
            if (shortSide < minSize)
            {
                continue;
            }

            var score = GetScore(points, pred);
            if (score < boxThreshold)
            {
                continue;
            }

            var expandedPaths = Unclip(points, unclipRatio);
            if (expandedPaths.Count != 1)
            {
                continue;
            }

            var expandedPoints = expandedPaths[0]
                .Select(point => new Point2f(point.X, point.Y))
                .ToArray();
            var (expandedBox, expandedShortSide) = GetMiniBox(expandedPoints);
            if (expandedShortSide < minSize + 2)
            {
                continue;
            }

            var mappedBox = MapToSource(expandedBox, pred.Size(), sourceSize);
            if (!IsValidSourceBox(mappedBox, minSize))
            {
                continue;
            }

            boxes.Add(new PaddleOcrDetectionBox(mappedBox, score));
        }

        // Debug
        //{
        //	using Mat demo = contourSource.CvtColor(ColorConversionCodes.GRAY2RGB);
        //	demo.DrawContours(contours, -1, Scalar.Red);
        //	Image(demo).Dump();
        //}
        return boxes.ToArray();
    }

    private static (Point2f[] Points, float ShortSide) GetMiniBox(Point2f[] contour)
    {
        var rect = Cv2.MinAreaRect(contour);
        var points = rect.Points().OrderBy(point => point.X).ToArray();

        var index1 = points[1].Y > points[0].Y ? 0 : 1;
        var index4 = points[1].Y > points[0].Y ? 1 : 0;
        var index2 = points[3].Y > points[2].Y ? 2 : 3;
        var index3 = points[3].Y > points[2].Y ? 3 : 2;

        return ([points[index1], points[index2], points[index3], points[index4]],
            Math.Min(rect.Size.Width, rect.Size.Height));
    }

    private static float GetScore(Point2f[] box, Mat pred)
    {
        var xmin = Math.Clamp((int)Math.Floor(box.Min(point => point.X)), 0, pred.Width - 1);
        var xmax = Math.Clamp((int)Math.Ceiling(box.Max(point => point.X)), 0, pred.Width - 1);
        var ymin = Math.Clamp((int)Math.Floor(box.Min(point => point.Y)), 0, pred.Height - 1);
        var ymax = Math.Clamp((int)Math.Ceiling(box.Max(point => point.Y)), 0, pred.Height - 1);

        var localBox = box
            .Select(point => new Point((int)(point.X - xmin), (int)(point.Y - ymin)))
            .ToArray();
        using var mask = new Mat(ymax - ymin + 1, xmax - xmin + 1, MatType.CV_8UC1, Scalar.Black);
        mask.FillPoly([localBox], Scalar.White);
        using var cropped = pred[ymin, ymax + 1, xmin, xmax + 1];
        var score = (float)cropped.Mean(mask).Val0;

        // Debug
        //{
        //	using Mat cu = new Mat();
        //	cropped.ConvertTo(cu, MatType.CV_8UC1, 255);
        //	Util.HorizontalRun(true, Image(cu), Image(mask), score).Dump();
        //}
        return score;
    }

    private static Paths64 Unclip(Point2f[] box, float ratio)
    {
        var area = Math.Abs(GetSignedArea(box));
        var perimeter = GetPerimeter(box);
        if (area <= 0 || perimeter <= 0)
        {
            return [];
        }

        var distance = area * ratio / perimeter;
        Path64 path = new(box
            .Select(point => new Point64((long)point.X, (long)point.Y)));
        return Clipper.InflatePaths([path], distance, JoinType.Round, EndType.Polygon, 2.0, 0.25);
    }

    private static double GetSignedArea(Point2f[] points)
    {
        double area = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var next = points[(i + 1) % points.Length];
            area += points[i].X * next.Y - next.X * points[i].Y;
        }

        return area / 2.0;
    }

    private static double GetPerimeter(Point2f[] points)
    {
        double perimeter = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var next = points[(i + 1) % points.Length];
            var deltaX = points[i].X - next.X;
            var deltaY = points[i].Y - next.Y;
            perimeter += Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        return perimeter;
    }

    private static Point2f[] MapToSource(Point2f[] box, Size predictionSize, Size sourceSize)
    {
        var scaleX = 1.0 * sourceSize.Width / predictionSize.Width;
        var scaleY = 1.0 * sourceSize.Height / predictionSize.Height;
        return box.Select(point => new Point2f(
                Math.Clamp((float)Math.Round(point.X * scaleX), 0, sourceSize.Width - 1),
                Math.Clamp((float)Math.Round(point.Y * scaleY), 0, sourceSize.Height - 1)))
            .ToArray();
    }

    private static bool IsValidSourceBox(Point2f[] box, int minimumSize)
    {
        var width = (int)Math.Sqrt(
            Math.Pow(box[0].X - box[1].X, 2) + Math.Pow(box[0].Y - box[1].Y, 2));
        var height = (int)Math.Sqrt(
            Math.Pow(box[0].X - box[3].X, 2) + Math.Pow(box[0].Y - box[3].Y, 2));
        return width > minimumSize && height > minimumSize;
    }
}
