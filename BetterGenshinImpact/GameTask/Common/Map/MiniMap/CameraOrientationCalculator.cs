using System.Linq;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.Common.Map.MiniMap;

using static MiniMapPreprocessorUtils;

public class CameraOrientationCalculator : IDisposable
{
    private const int TplOutRad = 78;
    private const int TplInnRad = 19;
    private const int RLength = 60;
    private const int ThetaLength = 180;
    private const int FLength = 256;
    private readonly Mat _rotationRemapDataX = new Mat();
    private readonly Mat _rotationRemapDataY = new Mat();
    private readonly Mat _alphaMask2Remap;

    public CameraOrientationCalculator()
    {
        float[] rArray = LinearSpaced(TplInnRad, TplOutRad, RLength);
        float[] thetaArray = LinearSpaced(0, 360, ThetaLength, false);

        using (var r = Mat.FromPixelData(1, RLength, MatType.CV_32F, rArray))
        using (var theta = Mat.FromPixelData(ThetaLength, 1, MatType.CV_32F, thetaArray))
        using (var rMat = r.Repeat(ThetaLength, 1))
        using (var thetaMat = theta.Repeat(1, RLength))
        {
            Cv2.PolarToCart(rMat, thetaMat, _rotationRemapDataX, _rotationRemapDataY, true);
        }
        Cv2.Add(_rotationRemapDataX, Size / 2, _rotationRemapDataX);
        Cv2.Add(_rotationRemapDataY, Size / 2, _rotationRemapDataY);

        using (var alphaMask2Row = Mat.FromPixelData(1, RLength, MatType.CV_32F, rArray.Select(v => (float)(111.7 + 1.836 * v)).ToArray()))
        {
            _alphaMask2Remap = alphaMask2Row.Repeat(ThetaLength, 1);
        }
    }

    public (float, float) PredictRotation(Mat outMat)
    {
        using var remap = new Mat();
        using var hImg = new Mat();
        using var faImg = new Mat();
        using var fbImg = new Mat();
        using var histA = new Mat();
        using var histB = new Mat();
        using var result = Mat.Zeros(1, ThetaLength, MatType.CV_32FC1).ToMat();
        using var resultShift = new Mat();
        using var mask = new Mat();
        using var maskSum = new Mat();
        Cv2.Remap(outMat, remap, _rotationRemapDataX, _rotationRemapDataY, InterpolationFlags.Nearest);
        Cv2.InRange(remap, new Scalar(0, 0, 0), new Scalar(1, 1, 1), mask);
        Cv2.BitwiseNot(mask,mask);
        Cv2.Reduce(mask, maskSum, ReduceDimension.Column, ReduceTypes.Sum, MatType.CV_32F);
        Cv2.Transpose(maskSum, maskSum);
        BgrToHue(remap, hImg, faImg);
        faImg.CopyTo(fbImg);
        ApplyMask(fbImg, _alphaMask2Remap, new Scalar(255.0f));
        int[] channels = [0, 1];
        int[] histSize = [ThetaLength, FLength];
        Rangef[] ranges = [new(0, 180), new(0, 256)];
        Cv2.CalcHist([hImg, faImg], channels, null, histA, 2, histSize, ranges);
        Cv2.CalcHist([hImg, fbImg], channels, null, histB, 2, histSize, ranges);
        unsafe
        {
            var hImgPtr = (float*)hImg.Data;
            var faImgPtr = (float*)faImg.Data;
            var fbImgPtr = (float*)fbImg.Data;
            var histAPtr = (float*)histA.Data;
            var histBPtr = (float*)histB.Data;
            var resultPtr = (float*)result.Data;
            var totalElements = ThetaLength * RLength;
            for (var i = 0; i < totalElements; i++)
            {
                float h = hImgPtr[i];
                float fa = faImgPtr[i];
                float fb = fbImgPtr[i];
                if (h is < 0 or >= 180 || fa is < 0 or >= 256 || fb is < 0 or >= 256) continue;
                float ha = histAPtr[(int)(h / 180 * ThetaLength) * FLength + (int)(fa / 256 * FLength)];
                float hb = histBPtr[(int)(h / 180 * ThetaLength) * FLength + (int)(fb / 256 * FLength)];
                resultPtr[i / RLength] += (ha > hb) ? 0.0f :
                    (Math.Abs(ha - hb) < 0.0001) ? 60.0f :
                    255.0f;
            }
        }
        Cv2.Divide(result, maskSum, result);
        Cv2.Resize(result, result, new Size(result.Width * 2, 1), 0, 0, InterpolationFlags.Cubic);
        var peakWidth = ThetaLength / 2 + 1;
        RightShiftCv(result, resultShift, peakWidth);
        var peakRegionSum = Cv2.Sum(resultShift[0, 1, 0, peakWidth]).Val0;
        Cv2.Subtract(result, resultShift, resultShift);
        Cv2.Integral(resultShift, result);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);
        var degree = (float)(maxLoc.X - 1) / ThetaLength * 180 - 45;
        var rotationConfidence = (float)(maxVal + peakRegionSum) / (peakWidth * RLength * 255);
        return (degree, rotationConfidence);
    }

    public void Dispose()
    {
        _rotationRemapDataX.Dispose();
        _rotationRemapDataY.Dispose();
        _alphaMask2Remap.Dispose();
    }
}