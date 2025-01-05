using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Common.Map;

/// <summary>
/// author: https://github.com/Limint
/// </summary>
public class CameraOrientationFromLimint
{
    private static int tplSize = 216;
    private int tplOutRad = 78;
    private int tplInnRad = 19;
    private float[] alphaParams1 = { 18.632f, 20.157f, 24.093f, 34.617f, 38.566f, 41.94f, 47.654f, 51.087f, 58.561f, 63.925f, 67.759f, 71.77f, 75.214f };
    private int rLength = 60;
    private static int thetaLength = 360;
    private int peakWidth = thetaLength / 4;
    private int width = tplSize;
    private int height = tplSize;
    private Mat rotationRemapDataX;
    private Mat rotationRemapDataY;
    private Mat alphaMask1;
    private Mat alphaMask2;

    public CameraOrientationFromLimint()
    {
        GeneratePoints();
        CreateAlphaMask();
    }

    public static float[] RightShift(float[] array, int k)
    {
        return array.Skip(array.Length - k)
            .Concat(array.Take(array.Length - k))
            .ToArray();
    }

    public static IEnumerable<float> CumulativeSum(IEnumerable<float> source)
    {
        float sum = 0;
        foreach (var item in source)
        {
            sum += item;
            yield return sum;
        }
    }

    public static Mat[] ApplyMask(Mat[] inputMats, Mat mask, float bkg)
    {
        Mat[] outputMats = inputMats
            .Select(mat => new Mat(mat.Size(), MatType.CV_32F))
            .ToArray();

        for (int i = 0; i < outputMats.Length; i++)
        {
            var outputMat = outputMats[i];
            Cv2.Subtract(inputMats[i], bkg, outputMat, null, MatType.CV_32F);
            Cv2.Divide(outputMat, mask, outputMat, 255, MatType.CV_32F);
            Cv2.Add(outputMat, bkg, outputMat, null, MatType.CV_32F);
        }

        return outputMats;
    }

    public static Mat BgrToHue(Mat[] bgrChannels)
    {
        Mat hueMat = new Mat();

        using (Mat cmax = new Mat())
        using (Mat cmin = new Mat())
        using (Mat mask = new Mat())
        {
            Cv2.Max(bgrChannels[2], bgrChannels[1], cmax);
            Cv2.Max(cmax, bgrChannels[0], cmax);
            Cv2.Min(bgrChannels[2], bgrChannels[1], cmin);
            Cv2.Min(cmin, bgrChannels[0], cmin);

            Cv2.Divide(cmax - cmin - cmin - bgrChannels[0] + bgrChannels[1] + bgrChannels[2], cmax - cmin, hueMat, 60, MatType.CV_32F);

            Cv2.Compare(cmax, cmin, mask, CmpType.EQ);
            hueMat.SetTo(-1, mask);

            Cv2.Compare(bgrChannels[0], bgrChannels[1], mask, CmpType.GT);
            Cv2.Subtract(360, hueMat, hueMat, mask, MatType.CV_32F);
            return hueMat;
        }
    }

    static float[] LinearSpaced(float a, float b, int n, bool endpoint = true)
    {
        if (endpoint)
        {
            return Enumerable.Range(0, n).Select(i => a + (b - a) * (float)i / (n - 1)).ToArray();
        }
        else
        {
            return Enumerable.Range(0, n).Select(i => a + (b - a) * (float)i / n).ToArray();
        }
    }

    private void GeneratePoints()
    {
        Mat r = new Mat(1, rLength, MatType.CV_32F, LinearSpaced(tplInnRad, tplOutRad, rLength));
        Mat theta = new Mat(thetaLength, 1, MatType.CV_32F, LinearSpaced(0, 360, thetaLength, false));
        Mat rMat = r.Repeat(thetaLength, 1);
        Mat thetaMat = theta.Repeat(1, rLength);
        rotationRemapDataX = new Mat();
        rotationRemapDataY = new Mat();
        Cv2.PolarToCart(rMat, thetaMat, rotationRemapDataX, rotationRemapDataY, true);
        Cv2.Add(rotationRemapDataX, tplSize / 2, rotationRemapDataX, null, MatType.CV_32F);
        Cv2.Add(rotationRemapDataY, tplSize / 2, rotationRemapDataY, null, MatType.CV_32F);
    }

    private void CreateAlphaMask()
    {
        var values = LinearSpaced(tplInnRad, tplOutRad, rLength);
        var alphaMask1Row = new Mat(1, rLength, MatType.CV_32F, values.Select(v =>
        {
            var index = Array.BinarySearch(alphaParams1, v);
            return (float)(229 + (index < 0 ? ~index : index));
        }).ToArray());
        alphaMask1 = alphaMask1Row.Repeat(thetaLength, 1);
        var alphaMask2Row = new Mat(1, rLength, MatType.CV_32F, values.Select(v => (float)(111.7 + 1.836 * v)).ToArray());
        alphaMask2 = alphaMask2Row.Repeat(thetaLength, 1);
    }

    public void RotationRemapData(Mat image)
    {
        int height = image.Rows;
        int width = image.Cols;
        if (this.width != width)
        {
            Cv2.Multiply(rotationRemapDataX, (float)(width * 1.0 / this.width), rotationRemapDataX);
            this.width = width;
        }

        if (this.height != height)
        {
            Cv2.Multiply(rotationRemapDataY, (float)(height * 1.0 / this.height), rotationRemapDataY);
            this.height = height;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="image"></param>
    /// <returns>视角,置信度</returns>
    public (float, float) PredictRotationWithConfidence(Mat image)
    {
        using (Mat remap = new Mat())
        using (Mat faImg = new Mat())
        using (Mat histA = new Mat())
        using (Mat histB = new Mat())
        using (Mat result = new Mat(thetaLength, rLength, MatType.CV_8UC1, Scalar.Black))
        using (Mat resultMean = new Mat())
        {
            RotationRemapData(image);
            Cv2.Remap(image, remap, rotationRemapDataX, rotationRemapDataY, InterpolationFlags.Lanczos4);

            Cv2.Split(remap, out var bgrChannels);
            var faBgr = ApplyMask(bgrChannels, alphaMask1, 0.0f);
            var hImg = BgrToHue(faBgr);
            Cv2.Divide(faBgr[0] + faBgr[1] + faBgr[2], 3, faImg, 1, MatType.CV_32F);
            var fbImg = ApplyMask([faImg], alphaMask2, 255.0f)[0];

            int[] channels = [0, 1];
            int[] histSize = [360, 256];
            Rangef[] ranges = [new(0, 360), new(0, 256)];
            Cv2.CalcHist([hImg, faImg], channels, null, histA, 2, histSize, ranges);
            Cv2.CalcHist([hImg, fbImg], channels, null, histB, 2, histSize, ranges);

            for (int i = 0; i < thetaLength; i++)
            {
                for (int j = 0; j < rLength; j++)
                {
                    float h = hImg.At<float>(i, j);
                    float fa = faImg.At<float>(i, j);
                    float fb = fbImg.At<float>(i, j);
                    if (h >= 0 && h < 360 && fa >= 0 && fa < 256 && fb >= 0 && fb < 256)
                    {
                        var ha = histA.At<float>((int)h, (int)fa);
                        var hb = histB.At<float>((int)h, (int)fb);
                        if (ha > hb)
                        {
                            result.At<byte>(i, j) = 0x0;
                        }
                        else if (ha == hb)
                        {
                            result.At<byte>(i, j) = 0x7F;
                        }
                        else
                        {
                            result.At<byte>(i, j) = 0xFF;
                        }
                    }
                }
            }

            //Cv2.ImShow("Display Window", result);
            //Cv2.WaitKey(0);
            Cv2.Reduce(result, resultMean, ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_32F);
            resultMean.GetArray(out float[] mean);
            float[] diff = mean.Zip(RightShift(mean, peakWidth), (a, b) => a - b).ToArray();
            float[] conv = CumulativeSum(diff).ToArray();

            int maxIndex = Array.IndexOf(conv, conv.Max());
            float degree = (float)maxIndex / thetaLength * 360 - 45;
            if (degree < 0)
            {
                degree = 360 + degree;
            }

            float rotationConfidence = (conv[maxIndex] + mean.Skip(thetaLength - peakWidth).Sum()) / (peakWidth * 255);
            Debug.WriteLine($"视角度:{degree},置信度:{rotationConfidence}");
            // if (rotationConfidence < confidence)
            // {
            //     Debug.WriteLine($"置信度{rotationConfidence}<{confidence}, 不可靠视角 {degree}");
            //     return degree;
            // }

            return (degree, rotationConfidence);
        }
    }

    public float PredictRotation(Mat image)
    {
        return PredictRotationWithConfidence(image).Item1;
    }
}