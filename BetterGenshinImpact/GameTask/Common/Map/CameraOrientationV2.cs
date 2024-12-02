using System;
using System.Collections.Generic;
using NumpyDotNet;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map;

public class CameraOrientationV2
{
    private double tplSize = 216;
    private double tplOutRad = 78;
    private int tplInnRad = 19;
    private ndarray alphaParams1 = np.array(new[] { 18.632, 20.157, 24.093, 34.617, 38.566, 41.94, 47.654, 51.087, 58.561, 63.925, 67.759, 71.77, 75.214 });

    private double rLength = 60;
    private int thetaLength = 360;
    private int peakWidth;
    private ndarray rotationRemapDataX;
    private ndarray rotationRemapDataY;

    private ndarray alphaMask1;
    private ndarray alphaMask2;
    private double rotation;
    private double rotationConfidence;

    public CameraOrientationV2()
    {
        this.peakWidth = thetaLength / 4;

        var points = GeneratePoints(tplSize / 2.0, tplSize / 2.0, tplInnRad, tplOutRad, rLength, thetaLength);
        this.rotationRemapDataX = points.Item1;
        this.rotationRemapDataY = points.Item2;

        var masks = CreatAlphaMask();
        this.alphaMask1 = masks.Item1;
        this.alphaMask2 = masks.Item2;
    }

    private ndarray ApplyMask(ndarray img, ndarray mask, double bkg)
    {
        return (img.astype(np.Float32) - bkg) / mask * 255 + bkg;
    }

    private ndarray Bgr2h(ndarray bgr)
    {
        var cmax = np.max(bgr, axis: -1);
        var cmin = np.min(bgr, axis: -1);
        var mask = cmax > cmin;

        var hue = np.zeros_like(cmax).astype(np.Float32);
        // var maskIndices = np.where(mask);

        hue[mask] = ((cmax[mask] - 2 * (ndarray)cmin[mask] - ((ndarray)bgr["...", 2])[mask] + ((ndarray)bgr["...", 1])[mask] + ((ndarray)bgr["...", 0])[mask]) / ((ndarray)cmax[mask] - (ndarray)cmin[mask])) * 60;

        hue[np.where(~mask)] = -1;
        hue = (ndarray)np.where((ndarray)bgr["...", 1] >= (ndarray)bgr["...", 0], hue, 360 - hue);

        return hue;
    }

    private (ndarray, ndarray) GeneratePoints(double x, double y, double d1, double d2, double n, double m)
    {
        var r = np.linspace(d1, d2, ref n);
        var theta = np.linspace(0, 360, ref m, endpoint: false);
        var thetaRad = np.radians(theta);

        var meshgrid = np.meshgrid([r, thetaRad]);
        var rGrid = meshgrid[0];
        var thetaGrid = meshgrid[1];

        var xCartesian = rGrid * np.cos(thetaGrid) + x;
        var yCartesian = rGrid * np.sin(thetaGrid) + y;

        return (xCartesian.astype(np.Float32), yCartesian.astype(np.Float32));
    }

    private (ndarray, ndarray) CreatAlphaMask()
    {
        var values = np.linspace(tplInnRad, tplOutRad, ref rLength);
        var alphaMask1 = (229 + np.searchsorted(alphaParams1, values)).astype(np.Float32);
        var alphaMask2 = (111.7 + 1.836 * values).astype(np.Float32);

        return (alphaMask1, alphaMask2);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="image">3通道图像</param>
    /// <param name="confidence"></param>
    /// <returns></returns>
    public double? PredictRotation(Mat image, double confidence = 0.3)
    {
        Mat remap = new();
        Cv2.Remap(image, remap, InputArray.Create(rotationRemapDataX.AsFloatArray()), InputArray.Create(rotationRemapDataY.AsFloatArray()),
            InterpolationFlags.Lanczos4);
        Cv2.Split(remap, out var bgrChannels);
        List<float[]> remapChannels = [];
        foreach (var channel in bgrChannels)
        {
            Mat floatChannel = new Mat();
            channel.ConvertTo(floatChannel, MatType.CV_32F);
            floatChannel.GetArray<float>(out var channelArray);
            remapChannels.Add(channelArray);
        }
        // TODO float[,,] -> ndarray
        
        var bgrImg = ApplyMask(np.array(remapChannels), alphaMask1.reshape(-1, 1), 0);
        var hImg = Bgr2h(bgrImg);
        var faImg = np.mean(bgrImg, axis: 2);
        var fbImg = ApplyMask(faImg, alphaMask2, 255);

        var histA = new Mat();
        var histB = new Mat();

        Rangef[] ranges = [new(0, 360), new(0, 256)];
        int[] histSize = [360, 256];
        Cv2.CalcHist([InputArray.Create(hImg.AsFloatArray()).GetMat(), InputArray.Create(faImg.AsFloatArray()).GetMat()], [0, 1], null, histA, 2, histSize, ranges);
        Cv2.CalcHist([InputArray.Create(hImg.AsFloatArray()).GetMat(), InputArray.Create(fbImg.AsFloatArray()).GetMat()], [0, 1], null, histB, 2, histSize, ranges);

        var result = np.zeros((thetaLength, rLength), np.UInt8);
        var h = np.floor(hImg).astype(np.Int32);
        var fa = np.floor(faImg).astype(np.Int32);
        var fb = np.floor(fbImg).astype(np.Int32);

        var flag = (h >= 0) & (h < 360) & (fa >= 0) & (fa < 256) & (fb >= 0) & (fb < 256);

        var histANp = np.array(histA);
        var histBNp = np.array(histB);
        result[flag] = np.select(
            [
                (ndarray)histANp[h[flag], fa[flag]] > (ndarray)histBNp[h[flag], fb[flag]],
                ((ndarray)histANp[h[flag], fa[flag]]).Equals((ndarray)histBNp[h[flag], fb[flag]]),
                (ndarray)histANp[h[flag], fa[flag]] < (ndarray)histBNp[h[flag], fb[flag]]
            ],
            [
                np.array(0),
                np.array(128),
                np.array(255)
            ]
        ).astype(np.UInt8);

        // 处理结果计算
        var resultMean = np.mean(result, axis: 1);
        var resultConv = np.cumsum(resultMean - np.roll(resultMean, peakWidth)) + (double)np.sum((ndarray)resultMean[-peakWidth + ":"]);

        var maxInd = np.argmax(resultConv);
        var degree = ((int)maxInd + 0.5) / thetaLength * 360 - 45;

        if (degree < 0)
            degree = 360 + degree;

        this.rotation = degree;
        this.rotationConfidence = (float)resultConv[(int)maxInd] / (peakWidth * 255);

        if (this.rotationConfidence < confidence)
        {
            Console.WriteLine($"置信度{this.rotationConfidence}<{confidence}, 不可靠视角 {this.rotation}");
            return null;
        }

        return degree;
    }
}