using System.Diagnostics;
using OpenCvSharp;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using Scalar = YamlDotNet.Core.Events.Scalar;

namespace OcrDmlProbe.Ocr;

/// <summary>与 BetterGI PaddleOcrService.RunAll 一致的完整管线（检测 → 透视裁剪 → 识别 → CTC 解码）。</summary>
public sealed class PaddlePipeline(DetProbe det, RecProbe rec, float dropScore = 0.5f) : IDisposable
{
    public record PipelineTimings(
        double TotalMs, DetTimings Det, double SortMs, double CropMs, RecTimings Rec);

    public float DropScore { get; set; } = dropScore;

    /// <summary>仅执行检测+排序+透视裁剪，返回裁剪图（调用方负责释放），供单独测量 Rec 成本。</summary>
    public Mat[] ExportCrops(Mat src)
    {
        var boxes = SortBoxes(det.RunBoxes(src, out _));
        var mats = new List<Mat>(boxes.Length);
        foreach (var box in boxes)
        {
            mats.Add(GetRotateCropImage(src, box.Points));
        }

        return [.. mats];
    }

    public OcrResult RunAll(Mat src, out PipelineTimings timings, int recognizeBatchSize = 0)
    {
        var startTime = Stopwatch.GetTimestamp();
        var boxes = SortBoxes(det.RunBoxes(src, out var detTimings));
        var afterDet = Stopwatch.GetTimestamp();
        var sortMs = Stopwatch.GetElapsedTime(startTime, afterDet).TotalMilliseconds - detTimings.TotalMs;
        List<Mat> mats = new(boxes.Length);
        try
        {
            foreach (var box in boxes)
            {
                mats.Add(GetRotateCropImage(src, box.Points));
            }

            var afterCrop = Stopwatch.GetTimestamp();
            var cropMs = Stopwatch.GetElapsedTime(afterDet, afterCrop).TotalMilliseconds;

            var recResults = rec.Run(mats.ToArray(), out var recTimings, recognizeBatchSize);
            var regions = recResults
                .Select((result, i) => new OcrResultRegion(boxes[i].Rect, result.Text, result.Score))
                .Where(region => region.Score >= DropScore)
                .ToArray();
            var totalMs = Stopwatch.GetElapsedTime(startTime, Stopwatch.GetTimestamp()).TotalMilliseconds;
            timings = new PipelineTimings(totalMs, detTimings, sortMs, cropMs, recTimings);
            return new OcrResult(regions);
        }
        finally
        {
            foreach (var mat in mats) mat.Dispose();
        }
    }

    /// <summary>与 PaddleOcrModelType.DefaultRecLabelFunc 一致：从 inference.yml 提取 character_dict。</summary>
    public static IReadOnlyList<string> LoadRecLabels(string recModelPath)
    {
        const string modelConfigFileName = "inference.yml";
        var configFilePath = Path.Combine(
            Path.GetDirectoryName(recModelPath) ??
            throw new InvalidOperationException("Cannot get model directory"),
            modelConfigFileName);

        if (!File.Exists(configFilePath))
            throw new FileNotFoundException(
                $"PaddleOCR config file {modelConfigFileName} not found: {configFilePath}");

        using var reader = new StreamReader(configFilePath);
        var parser = new Parser(reader);

        while (parser.MoveNext())
        {
            if (parser.Current is not Scalar { Value: "PostProcess" }) continue;
            parser.MoveNext(); // MappingStart
            while (parser.MoveNext())
            {
                if (parser.Current is not Scalar { Value: "character_dict" }) continue;
                parser.MoveNext(); // SequenceStart
                var result = new List<string>();
                while (parser.MoveNext())
                {
                    switch (parser.Current)
                    {
                        case SequenceEnd:
                            return result;
                        case Scalar charScalar:
                            result.Add(charScalar.Value);
                            break;
                    }
                }
            }
        }

        throw new InvalidOperationException("未在 YAML 的 PostProcess 部分找到 character_dict。");
    }

    /// <summary>按 PaddleOCR 官方 get_rotate_crop_image 的方式对检测框执行透视裁剪。</summary>
    private static Mat GetRotateCropImage(Mat src, Point2f[] points)
    {
        if (points.Length != 4)
        {
            throw new ArgumentException("OCR detection box must contain exactly four points.", nameof(points));
        }

        var cropWidth = (int)Math.Max(GetDistance(points[0], points[1]), GetDistance(points[2], points[3]));
        var cropHeight = (int)Math.Max(GetDistance(points[0], points[3]), GetDistance(points[1], points[2]));
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            throw new InvalidOperationException($"Invalid OCR crop size: {cropWidth}x{cropHeight}");
        }

        Point2f[] destinationPoints =
        [
            new(0, 0),
            new(cropWidth, 0),
            new(cropWidth, cropHeight),
            new(0, cropHeight)
        ];
        using var transform = Cv2.GetPerspectiveTransform(points, destinationPoints);
        var cropped = new Mat();
        try
        {
            Cv2.WarpPerspective(src, cropped, transform, new Size(cropWidth, cropHeight),
                InterpolationFlags.Cubic, BorderTypes.Replicate);

            if (cropped.Height * 1.0 / cropped.Width >= 1.5)
            {
                Cv2.Rotate(cropped, cropped, RotateFlags.Rotate90Counterclockwise);
            }

            return cropped;
        }
        catch
        {
            cropped.Dispose();
            throw;
        }
    }

    private static double GetDistance(Point2f first, Point2f second)
    {
        var deltaX = first.X - second.X;
        var deltaY = first.Y - second.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static PaddleOcrDetectionBox[] SortBoxes(PaddleOcrDetectionBox[] source)
    {
        var boxes = source
            .OrderBy(box => box.Points[0].Y)
            .ThenBy(box => box.Points[0].X)
            .ToArray();

        for (var i = 0; i < boxes.Length - 1; i++)
        {
            for (var j = i; j >= 0; j--)
            {
                if (Math.Abs(boxes[j + 1].Points[0].Y - boxes[j].Points[0].Y) < 10
                    && boxes[j + 1].Points[0].X < boxes[j].Points[0].X)
                {
                    (boxes[j], boxes[j + 1]) = (boxes[j + 1], boxes[j]);
                }
                else
                {
                    break;
                }
            }
        }

        return boxes;
    }

    public void Dispose()
    {
        det.Dispose();
        rec.Dispose();
    }
}
