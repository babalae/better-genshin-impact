using System;
using System.Diagnostics;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.MiniMap;

public class MiniMapPreprocessor : IDisposable
{
    private readonly MaskCalculator _maskCalculator = new();
    private readonly CameraOrientationCalculator _coCalculator = new();
    private bool _disposed;
    
    public (float, float) PredictRotationWithConfidence(Mat miniMap)
    {
        var (src, mask) = _maskCalculator.Process1(miniMap);
        using (src)
        {
            return _coCalculator.PredictRotation(src, mask);
        }
    }

    public float PredictRotation(Mat miniMap)
    {
        return PredictRotationWithConfidence(miniMap).Item1;
    }

    public (Mat, Mat) GetMiniMapAndMask(Mat miniMap)
    {
        //Debug.WriteLine($"输入图片尺寸为{miniMap.Size()} 类型为 {miniMap.Type()}");
        var (src, mask) = _maskCalculator.Process1(miniMap);
        using (src)
        {
            var (angle, _) = _coCalculator.PredictRotation(src, mask);
            return _maskCalculator.Process2(angle);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coCalculator.Dispose();
        _maskCalculator.Dispose();
    }
}
