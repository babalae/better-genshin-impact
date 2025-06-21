using System;
using System.Diagnostics;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.MiniMap;

public class MiniMapPreprocessor : IDisposable
{
    private static readonly MaskCalculator _maskCalculator = new();
    private static readonly CameraOrientationCalculator _coCalculator = new();
    
    public (float, float) PredictRotationWithConfidence(Mat miniMap)
    {
        using var mat = _maskCalculator.Process1(miniMap);
        return _coCalculator.PredictRotation(mat);
    }

    public float PredictRotation(Mat miniMap)
    {
        return PredictRotationWithConfidence(miniMap).Item1;
    }

    public (Mat, Mat) GetMiniMapAndMask(Mat miniMap)
    {
        //Debug.WriteLine($"输入图片尺寸为{miniMap.Size()} 类型为 {miniMap.Type()}");
        using var mat = _maskCalculator.Process1(miniMap);
        var (angle, _) = _coCalculator.PredictRotation(mat);
        return _maskCalculator.Process2(angle);
    }

    public void Dispose()
    {
        _coCalculator.Dispose();
        _maskCalculator.Dispose();
    }
}