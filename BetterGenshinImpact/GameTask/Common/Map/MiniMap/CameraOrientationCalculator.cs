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
    private const int ThetaLength = 360;
    private const int FLength = 256;
    private const int Scale = 2;
    private const int PeakWidth = ThetaLength / 4 * Scale + 1;
    private readonly Mat _rotationRemapDataX = new Mat();
    private readonly Mat _rotationRemapDataY = new Mat();
    private readonly Mat _alphaMask1Remap;
    private readonly Mat _alphaMask2Remap;

    public CameraOrientationCalculator()
    {
        float[] rArray = LinearSpaced(TplInnRad, TplOutRad, RLength);
        float[] thetaArray = LinearSpaced(0, 360, ThetaLength, false);
        float[] alphaParams1 = [18.632f, 20.157f, 24.093f, 34.617f, 38.566f, 41.94f, 47.654f, 51.087f, 58.561f, 63.925f, 67.759f, 71.77f, 75.214f];

        using (var r = Mat.FromPixelData(1, RLength, MatType.CV_32F, rArray))
        using (var theta = Mat.FromPixelData(ThetaLength, 1, MatType.CV_32F, thetaArray))
        using (var rMat = r.Repeat(ThetaLength, 1))
        using (var thetaMat = theta.Repeat(1, RLength))
        {
            Cv2.PolarToCart(rMat, thetaMat, _rotationRemapDataX, _rotationRemapDataY, true);
        }
        Cv2.Add(_rotationRemapDataX, Size / 2, _rotationRemapDataX);
        Cv2.Add(_rotationRemapDataY, Size / 2, _rotationRemapDataY);

        using (var alphaMask2Row = Mat.FromPixelData(1, RLength, MatType.CV_32F, rArray.Select(v => (float)(137 + 1.43 * v)).ToArray()))
        {
            _alphaMask2Remap = alphaMask2Row.Repeat(ThetaLength, 1);
        }
        
        using (var alphaMask1Row = Mat.FromPixelData(1, RLength, MatType.CV_32F, rArray.Select(v =>
               {
                   var index = Array.BinarySearch(alphaParams1, v);
                   return (float)(229 + (index < 0 ? ~index : index));
               }).ToArray()))
        {
            _alphaMask1Remap = alphaMask1Row.Repeat(ThetaLength, 1);
        }
    }

    public (float, float) PredictRotation(Mat src, Mat mask)
    {
        using var remapSrc = new Mat();
        using var remapMask = new Mat();
        using var hImg = new Mat();
        using var faImg = new Mat();
        using var fbImg = new Mat();
        using var histA = new Mat();
        using var histB = new Mat();
        using var result = Mat.Zeros(1, ThetaLength, MatType.CV_32FC1).ToMat();
        using var resultShift = new Mat();
        Cv2.Remap(src, remapSrc, _rotationRemapDataX, _rotationRemapDataY, InterpolationFlags.Linear);
        Cv2.Remap(mask, remapMask, _rotationRemapDataX, _rotationRemapDataY, InterpolationFlags.Nearest);
        BgrToHue(remapSrc, hImg, faImg);
        using var temp = faImg.Clone();
        ApplyMask(faImg, _alphaMask1Remap, new Scalar(0.0f));
        temp.CopyTo(faImg, remapMask);
        ApplyMask(temp, _alphaMask2Remap, new Scalar(255.0f));
        temp.CopyTo(fbImg);
        ApplyMask(fbImg, _alphaMask1Remap, new Scalar(0.0f));
        temp.CopyTo(fbImg, remapMask);
        int[] channels = [0, 1];
        int[] histSize = [FLength, FLength];
        Rangef[] ranges = [new(0, 256), new(0, 256)];
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
                if (h is < 0 or >= 256 || fa < 0 || fb >= 256) continue;
                if (fa >= 256)
                {
                    resultPtr[ i / RLength] += 255.0f;
                }
                else if (fb < 0)
                {
                    resultPtr[i / RLength] += -255.0f;
                }
                else
                {
                    float ha = histAPtr[(int)(h / 256 * FLength) * FLength + (int)(fa / 256 * FLength)];
                    float hb = histBPtr[(int)(h / 256 * FLength) * FLength + (int)(fb / 256 * FLength)];
                    resultPtr[i / RLength] += (ha > hb) ? 0.0f :
                        (Math.Abs(ha - hb) < 0.0001) ? 100.0f :
                        255.0f;
                }
            }
        }
        Cv2.Resize(result, result, new Size(result.Width * Scale, 1), 0, 0, InterpolationFlags.Cubic);
        RightShiftCv(result, resultShift, PeakWidth);
        var peakRegionSum = Cv2.Sum(resultShift[0, 1, 0, PeakWidth]).Val0;
        Cv2.Subtract(result, resultShift, resultShift);
        Cv2.Integral(resultShift, result);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);
        var degree = (float)(maxLoc.X - 1) / ThetaLength * 360 / Scale - 45;
        var rotationConfidence = (float)(maxVal + peakRegionSum) / (PeakWidth * RLength * 255);
        return (degree, rotationConfidence);
    }

    public void Dispose()
    {
        _rotationRemapDataX.Dispose();
        _rotationRemapDataY.Dispose();
        _alphaMask2Remap.Dispose();
    }
}