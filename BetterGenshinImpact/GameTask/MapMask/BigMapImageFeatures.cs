using System;
using System.Threading;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>拥有描述子 Mat 的单张地图特征，保留计算尺度用于坐标换算。</summary>
internal sealed class BigMapImageFeatures : IDisposable
{
    /// <summary>关键点，坐标位于缩小后的图像空间。</summary>
    public KeyPoint[] KeyPoints { get; private set; } = [];

    /// <summary>即使未提取到关键点，也不能在同帧再次执行 SIFT。</summary>
    private bool _featuresExtracted;

    /// <summary>本实例负责释放的描述子。</summary>
    public required Mat Descriptors { get; init; }

    /// <summary>计算尺度的灰度图，外观核验时先对齐到截图尺度再平滑。</summary>
    public required Mat Appearance { get; init; }

    /// <summary>缩小图像的实际尺寸。</summary>
    public required Size Size { get; init; }

    /// <summary>缩小图像相对于原图的比例。</summary>
    public double ImageScale { get; init; } = 1;

    /// <summary>计算尺度下可见的地图范围；遮挡面板不参与关键点和外观核验。</summary>
    public required Rect MapArea { get; init; }

    /// <summary>只准备计算尺度图像；L3 成功时无需执行 SIFT。</summary>
    public static BigMapImageFeatures Prepare(Mat image, int maxSide, Rect? mapArea = null)
    {
        var scale = Math.Min(1, maxSide / (double)Math.Max(image.Width, image.Height));
        var descriptors = new Mat();
        var appearance = new Mat();
        try
        {
            Cv2.Resize(image, appearance, new Size(Math.Max(1, (int)Math.Round(image.Width * scale)),
                Math.Max(1, (int)Math.Round(image.Height * scale))), 0, 0, InterpolationFlags.Area);
            return new BigMapImageFeatures
            {
                Descriptors = descriptors, Appearance = appearance, Size = appearance.Size(), ImageScale = scale,
                MapArea = mapArea is { } area
                    ? new Rect((int)Math.Round(area.X * scale), (int)Math.Round(area.Y * scale),
                        (int)Math.Round(area.Width * scale), (int)Math.Round(area.Height * scale))
                    : new Rect(0, 0, appearance.Width, appearance.Height)
            };
        }
        catch
        {
            descriptors.Dispose();
            appearance.Dispose();
            throw;
        }
    }

    /// <summary>限长边提取特征；始终创建独立 Mat，不会释放或修改调用方截图。</summary>
    public static BigMapImageFeatures Extract(Feature2D sift, Mat image, int maxSide, CancellationToken cancellationToken = default)
    {
        var features = Prepare(image, maxSide);
        try
        {
            features.EnsureFeatures(sift, cancellationToken);
            return features;
        }
        catch
        {
            features.Dispose();
            throw;
        }
    }

    /// <summary>只有进入 L4/L5 时才提取截图特征，同帧的所有候选共享这一次计算。</summary>
    public void EnsureFeatures(Feature2D sift, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_featuresExtracted)
        {
            return;
        }

        using var mask = new Mat(Size, MatType.CV_8UC1, Scalar.All(0));
        using (var visible = new Mat(mask, MapArea))
        {
            visible.SetTo(Scalar.All(255));
        }
        sift.DetectAndCompute(Appearance, mask, out var keyPoints, Descriptors);
        cancellationToken.ThrowIfCancellationRequested();
        KeyPoints = keyPoints;
        _featuresExtracted = true;
    }

    /// <summary>释放本图像拥有的原生描述子。</summary>
    public void Dispose()
    {
        Descriptors.Dispose();
        Appearance.Dispose();
    }
}
