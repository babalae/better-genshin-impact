using OpenCvSharp;
using System;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Common.Map.MiniMap;

using static MiniMapPreprocessorUtils;

public class MaskCalculator : IDisposable
{
    private readonly Mat _alphaMask1 = new Mat();
    private readonly Mat _alphaMask2 = new Mat();
    private readonly Mat _radius = new Mat();
    private readonly Mat _angle = new Mat();
    private readonly Mat _sectorMask = new Mat(Size, Size, MatType.CV_8UC3);
    private readonly Mat _circleMask = Mat.Zeros(Size, Size, MatType.CV_8UC1);
    private readonly Mat _kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
    private readonly Mat _outMatF = new Mat(Size, Size, MatType.CV_32FC3);
    private readonly Mat _outMask = new Mat(Size, Size, MatType.CV_8UC1);

    public MaskCalculator()
    {
        float[] xArray = LinearSpaced(-Size / 2.0f, Size / 2.0f, Size, false);
        int[] range = Enumerable.Range(0, 256).ToArray();
        float[] alphaParams1 = [18.632f, 20.157f, 24.093f, 34.617f, 38.566f, 41.94f, 47.654f, 51.087f, 58.561f, 63.925f, 67.759f, 71.77f, 75.214f];

        using (var x = Mat.FromPixelData(1, Size, MatType.CV_32FC1, xArray))
        using (var xMat = x.Repeat(Size, 1))
        using (var yMat = xMat.T())
        {
            Cv2.CartToPolar(xMat, yMat, _radius, _angle, true);
        }
        _radius.ConvertTo(_radius, MatType.CV_8UC1);
        Cv2.Divide(_angle, 2, _angle);
        _angle.ConvertTo(_angle, MatType.CV_8UC1);

        var lutData1 = range.Select(v =>
        {
            var index = Array.BinarySearch(alphaParams1, v);
            return (byte)Math.Min(229 + (index < 0 ? ~index : index), 255);
        }).ToArray();
        var lutData2 = range.Select(v => (byte)Math.Min(137 + 1.43 * v, 255)).ToArray();
        using var lut1 = Mat.FromPixelData(1, 256, MatType.CV_8UC1, lutData1);
        using var lut2 = Mat.FromPixelData(1, 256, MatType.CV_8UC1, lutData2);
        Cv2.LUT(_radius, lut1, _alphaMask1);
        Cv2.LUT(_radius, lut2, _alphaMask2);
        Cv2.CvtColor(_alphaMask1, _alphaMask1, ColorConversionCodes.GRAY2BGR);
        Cv2.CvtColor(_alphaMask2, _alphaMask2, ColorConversionCodes.GRAY2BGR);

        _circleMask.Circle(Size / 2, Size / 2, Size / 2, new Scalar(255), -1);
    }

    public (Mat, Mat) Process1(Mat bgrMat)
    {
        bgrMat = bgrMat[new Rect((bgrMat.Width - Size) / 2, (bgrMat.Width - Size) / 2, Size, Size)];
        bgrMat.ConvertTo(_outMatF, MatType.CV_32FC3);
        CreateIconMask(bgrMat);
        return (bgrMat, _outMask);
    }

    public (Mat, Mat) Process2(float angle)
    {
        CreatSectorMask((int)Math.Round(angle));
        ApplyMask(_outMatF, _sectorMask, new Scalar(255.0f, 255.0f, 255.0f));
        ApplyMask(_outMatF, _alphaMask1, new Scalar(0.0f, 0.0f, 0.0f));
        var outMat = new Mat();
        _outMatF.ConvertTo(outMat, MatType.CV_8UC3);
        Cv2.BitwiseNot(_outMask, _outMask);
        CreatBgMask(outMat);
        Cv2.BitwiseAnd(_circleMask, _outMask, _outMask);
        return (outMat, _outMask.Clone());
    }

    private void CreatSectorMask(int angle)
    {
        _alphaMask2.CopyTo(_sectorMask);
        Cv2.Ellipse(_sectorMask, new Point(Size / 2, Size / 2), new Size(Size, Size), 0, angle + 45.5, angle + 314.5, new Scalar(255, 255, 255), -1);
    }

    private void CreatBgMask(Mat bgrMat)
    {
        using var temp = new Mat();
        Cv2.InRange(bgrMat, new Scalar(165, 165, 55), new Scalar(180, 180, 75), temp);
        Cv2.MorphologyEx(temp, temp, MorphTypes.Open, Mat.Ones(2, 2, MatType.CV_8UC1));
        if (Cv2.CountNonZero(temp) == 0)
        {
            return;
        }
        var minDist = new byte[256];
        Array.Fill(minDist, (byte)255);
        CalculateMinDist(temp, minDist);
        using var lut = Mat.FromPixelData(1, 256, MatType.CV_8UC1, minDist);
        Cv2.LUT(_angle, lut, temp);
        Cv2.Compare(_radius, temp, temp, CmpType.LT);
        using var temp1 = new Mat();
        Cv2.InRange(bgrMat, Scalar.All(100), Scalar.All(255), temp1);
        Cv2.BitwiseOr(temp1, temp, _outMask, _outMask);
    }

    private void CreateIconMask(Mat bgrMat)
    {
        using var cmax = new Mat();
        using var cmin = new Mat();
        using var diff = new Mat();
        MaxMinChannels(bgrMat, cmax, cmin);
        Cv2.Compare(cmax, cmin, _outMask, CmpType.EQ);
        Cv2.InRange(cmax, new Scalar(50), new Scalar(127), diff);
        Cv2.BitwiseAnd(diff, _outMask, _outMask);
        Cv2.Subtract(cmax, cmin, diff);
        Cv2.Subtract(255, cmax, cmin);
        Cv2.Divide(cmin, 6, cmin);
        Cv2.Min(cmin, diff, diff);
        Cv2.Add(diff, 10, diff);
        cmax.SetTo(255, _outMask);
        Cv2.Divide(cmax, diff, cmax, 10);
        //Cv2.GaussianBlur(cmax,cmax, new Size(3,3), 0.5);
        //Cv2.MorphologyEx(cmax,cmax, MorphTypes.Open, Mat.Ones(2, 2, MatType.CV_8UC1));
        Cv2.Threshold(cmax, _outMask, 200, 255, ThresholdTypes.Binary);
        Cv2.Dilate(_outMask, _outMask, _kernel);
        Cv2.MorphologyEx(_outMask, _outMask, MorphTypes.Close, _kernel);
    }

    private unsafe void CalculateMinDist(Mat mask, byte[] minDist)
    {
        var maskPtr = (byte*)mask.Data;
        var anglePtr = (byte*)_angle.Data;
        var radiusPtr = (byte*)_radius.Data;
        long totalBytes = Size * Size;
        for (var i = 0; i < totalBytes; i++)
        {
            if (maskPtr[i] != 255) continue;
            byte a = anglePtr[i];
            byte r = radiusPtr[i];
            if (minDist[a] > r)
            {
                minDist[a] = r;
            }
        }
    }

    public void Dispose()
    {
        _alphaMask1.Dispose();
        _alphaMask2.Dispose();
        _radius.Dispose();
        _angle.Dispose();
        _sectorMask.Dispose();
        _circleMask.Dispose();
        _kernel.Dispose();
        _outMatF.Dispose();
        _outMask.Dispose();
    }
}