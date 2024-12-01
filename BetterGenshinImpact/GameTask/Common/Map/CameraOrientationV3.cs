using System.Numerics;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Common.Map;

public class CameraOrientationV3
{
    private bool debugEnable;
    private static int tplSize = 210;
    private int tplOutRad = 78;
    private int tplInnRad = 19;
    private float[] alphaParams1 = { 18.632f, 20.157f, 24.093f, 34.617f, 38.566f, 41.94f, 47.654f, 51.087f, 58.561f, 63.925f, 67.759f, 71.77f, 75.214f };
    private int rLength = 60;
    private static int thetaLength = 360;
    private int peakWidth = thetaLength / 4;
    private Mat rotationRemapDataX;
    private Mat rotationRemapDataY;
    private int width = tplSize;
    private int height = tplSize;
    private Mat alphaMask1;
    private Mat alphaMask2;

    public CameraOrientationV3(bool debugEnable = false)
    {
        this.debugEnable = debugEnable;
        GeneratePoints();
        CreateAlphaMask();
    }

    public float Compute(Mat bgrMat)
    {
        var mat = new Mat(bgrMat, MapAssets.Instance.MimiMapRect);
        return PredictRotation(mat);
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
        Mat maskMat = mask.Repeat(inputMats[0].Rows, 1);
        Mat[] outputMats = inputMats
            .Select(mat => new Mat(mat.Size(), MatType.CV_32F))
            .ToArray();

        for (int i = 0; i < outputMats.Length; i++)
        {
            var outputMat = outputMats[i];
            Cv2.Divide(outputMat - bkg, maskMat, outputMat, 255, MatType.CV_32F);
            outputMats[i] += bkg;
        }

        return outputMats;
    }

    public static Mat BgrToHue(Mat[] bgrChannels)
    {
        Mat hueMat = new Mat();

        using (Mat cmax = new Mat())
        using (Mat cmin = new Mat())
        using (Mat sum = new Mat())
        {
            Cv2.Max(bgrChannels[2], bgrChannels[1], cmax);
            Cv2.Max(cmax, bgrChannels[0], cmax);
            Cv2.Min(bgrChannels[2], bgrChannels[1], cmin);
            Cv2.Min(cmin, bgrChannels[0], cmin);

            var delta = cmax - cmin;
            Cv2.Add(delta, bgrChannels[1], sum, null, MatType.CV_16U);
            Cv2.Add(sum, bgrChannels[2], sum, null, MatType.CV_16U);
            Cv2.Subtract(sum, cmin, sum, null, MatType.CV_16U);
            Cv2.Subtract(sum, bgrChannels[0], sum, null, MatType.CV_16U);

            Cv2.Divide(sum, delta, hueMat, 60, MatType.CV_32F);

            var mask = new Mat();
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
        Mat thetaRad = new Mat(thetaLength, 1, MatType.CV_32F, LinearSpaced(0, 360, thetaLength, false));
        Mat rMat = r.Repeat(thetaLength, 1);
        Mat thetaRadMat = thetaRad.Repeat(1, rLength);
        rotationRemapDataX = new Mat();
        rotationRemapDataY = new Mat();
        Cv2.PolarToCart(rMat, thetaRadMat, rotationRemapDataX, rotationRemapDataY, true);
    }

    private void CreateAlphaMask()
    {
        var values = LinearSpaced(tplInnRad, tplOutRad, rLength);
        alphaMask1 = new Mat(1, rLength, MatType.CV_32F, values.Select(v => 
        {
            var index = Array.BinarySearch(alphaParams1, v);
            return (float)(229 + (index < 0 ? ~index : index));
        }).ToArray());
        alphaMask2 = new Mat(1, rLength, MatType.CV_32F, values.Select(v => (float)(111.7 + 1.836 * v)).ToArray());
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

    public float PredictRotation(Mat image, float confidence = 0.3f)
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
            Cv2.Divide(faBgr[0] + faBgr[0] + faBgr[2], 3, faImg, 1, MatType.CV_32F);
            var fbImg = ApplyMask([faImg], alphaMask2, 255.0f);

            int[] channels = { 0, 0 };
            Mat mask = null;
            int[] histSize = { 360, 256 };
            Rangef[] ranges = { new Rangef(0, 360), new Rangef(0, 256) };
            Cv2.CalcHist([hImg, faImg], channels, mask, histA, 2, histSize, ranges);
            Cv2.CalcHist([hImg, fbImg[0]], channels, mask, histB, 2, histSize, ranges);

            unsafe
            {
                byte* dataPtrH = hImg.DataPointer;
                byte* dataPtrFa = faImg.DataPointer;
                byte* dataPtrFb = fbImg[0].DataPointer;
                byte* dataPtrHistA = histA.DataPointer;
                byte* dataPtrHistB = histB.DataPointer;
                byte* dataPtrResult = result.DataPointer;
                for (int i = 0; i < thetaLength * rLength; i++)
                {
                    float h = dataPtrH[i];
                    float fa = dataPtrFa[i];
                    float fb = dataPtrFa[i];
                    if (h >= 0 && h < 360 && fa >= 0 && fa < 256 && fb >= 0 && fb < 256)
                    {
                        var ha = dataPtrHistA[(int)h * 256 + (int)fa];
                        var hb = dataPtrHistB[(int)h * 256 + (int)fb];

                        if (ha > hb)
                        {
                            dataPtrResult[i] = 0x0;
                        }
                        else if (ha == hb)
                        {
                            dataPtrResult[i] = 0x7F;
                        }
                        else
                        {
                            dataPtrResult[i] = 0xFF;
                        }
                    }
                }
            }

            Cv2.Reduce(result, resultMean, 0, ReduceTypes.Avg, MatType.CV_32F);
            resultMean.GetArray(out float[] mean);
            float[] diff = mean.Zip(RightShift(mean, peakWidth), (a, b) => a - b).ToArray();

            float[] conv = CumulativeSum(diff).ToArray();
            float meanSum = mean.Skip(thetaLength - peakWidth).Sum();
            for (int i = 0; i < conv.Length; i++)
            {
                conv[i] += meanSum;
            }

            int maxIndex = Array.IndexOf(conv, conv.Max());
            float degree = ((float)maxIndex + 0.5f) / thetaLength * 360 - 45;
            if (degree < 0)
            {
                degree = 360 + degree;
            }

            float rotationConfidence = conv[maxIndex] / (peakWidth * 255);
            if (rotationConfidence < confidence)
            {
                Console.WriteLine($"置信度{rotationConfidence}<{confidence}, 不可靠视角 {degree}");
                return degree;
            }

            return degree;
        }
    }
}