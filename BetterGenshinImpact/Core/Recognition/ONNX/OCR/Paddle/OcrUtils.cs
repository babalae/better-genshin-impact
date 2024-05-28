using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.ONNX.OCR.Paddle;

class OcrUtils
{
    public static Tensor<float> SubstractMeanNormalize(Mat src, float[] meanVals, float[] normVals)
    {
        return ToTensorUnsafe(src, meanVals, normVals);
    }

    public static Tensor<float> ToTensorUnsafe(Mat src, float[] meanVals, float[] normVals)
    {
        var channels = src.Channels();
        var rows = src.Rows;
        var cols = src.Cols;

        Tensor<float> inputTensor = new DenseTensor<float>([1, channels, rows, cols]);

        unsafe
        {
            var b = (byte*)src.Ptr(0).ToPointer();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        var value = b[(r * cols + c) * channels + ch];
                        var data = value * normVals[ch] - meanVals[ch] * normVals[ch];
                        inputTensor[0, ch, r, c] = data;
                    }
                }
            }
        }

        return inputTensor;
    }

    public static Mat MakePadding(Mat src, int padding)
    {
        if (padding <= 0) return src;
        Scalar paddingScalar = new Scalar(255, 255, 255);
        Mat paddingSrc = new Mat();
        Cv2.CopyMakeBorder(src, paddingSrc, padding, padding, padding, padding, BorderTypes.Isolated, paddingScalar);
        return paddingSrc;
    }

    public static int GetThickness(Mat boxImg)
    {
        int minSize = boxImg.Cols > boxImg.Rows ? boxImg.Rows : boxImg.Cols;
        int thickness = minSize / 1000 + 2;
        return thickness;
    }

    public static void DrawTextBox(Mat boxImg, List<Point> box, int thickness)
    {
        if (box == null || box.Count == 0)
        {
            return;
        }
        var color = new Scalar(0, 0, 255);//B(0) G(0) R(255)
        Cv2.Line(boxImg, box[0], box[1], color, thickness);
        Cv2.Line(boxImg, box[1], box[2], color, thickness);
        Cv2.Line(boxImg, box[2], box[3], color, thickness);
        Cv2.Line(boxImg, box[3], box[0], color, thickness);
    }

    public static void DrawTextBoxes(Mat src, List<TextBox> textBoxes, int thickness)
    {
        for (int i = 0; i < textBoxes.Count; i++)
        {
            TextBox t = textBoxes[i];
            DrawTextBox(src, t.Points, thickness);
        }
    }

    public static List<Mat> GetPartImages(Mat src, List<TextBox> textBoxes)
    {
        List<Mat> partImages = new List<Mat>();
        for (int i = 0; i < textBoxes.Count; ++i)
        {
            Mat partImg = GetRotateCropImage(src, textBoxes[i].Points);
            //Mat partImg = new Mat();
            //GetRoiFromBox(src, partImg, textBoxes[i].Points);
            partImages.Add(partImg);
        }
        return partImages;
    }

    public static Mat GetRotateCropImage(Mat src, List<Point> box)
    {
        List<Point> points = new List<Point>();
        points.AddRange(box);

        int[] collectX = { box[0].X, box[1].X, box[2].X, box[3].X };
        int[] collectY = { box[0].Y, box[1].Y, box[2].Y, box[3].Y };
        int left = collectX.Min();
        int right = collectX.Max();
        int top = collectY.Min();
        int bottom = collectY.Max();

        Rect rect = new Rect(left, top, right - left, bottom - top);
        Mat imgCrop = new Mat(src, rect);

        for (int i = 0; i < points.Count; i++)
        {
            var pt = points[i];
            pt.X -= left;
            pt.Y -= top;
            points[i] = pt;
        }

        int imgCropWidth = (int)(Math.Sqrt(Math.Pow(points[0].X - points[1].X, 2) +
                                           Math.Pow(points[0].Y - points[1].Y, 2)));
        int imgCropHeight = (int)(Math.Sqrt(Math.Pow(points[0].X - points[3].X, 2) +
                                            Math.Pow(points[0].Y - points[3].Y, 2)));

        var ptsDst0 = new Point2f(0, 0);
        var ptsDst1 = new Point2f(imgCropWidth, 0);
        var ptsDst2 = new Point2f(imgCropWidth, imgCropHeight);
        var ptsDst3 = new Point2f(0, imgCropHeight);

        Point2f[] ptsDst = { ptsDst0, ptsDst1, ptsDst2, ptsDst3 };

        var ptsSrc0 = new Point2f(points[0].X, points[0].Y);
        var ptsSrc1 = new Point2f(points[1].X, points[1].Y);
        var ptsSrc2 = new Point2f(points[2].X, points[2].Y);
        var ptsSrc3 = new Point2f(points[3].X, points[3].Y);

        Point2f[] ptsSrc = { ptsSrc0, ptsSrc1, ptsSrc2, ptsSrc3 };

        Mat M = Cv2.GetPerspectiveTransform(ptsSrc, ptsDst);

        Mat partImg = new Mat();
        Cv2.WarpPerspective(imgCrop, partImg, M,
            new Size(imgCropWidth, imgCropHeight), InterpolationFlags.Nearest, BorderTypes.Replicate);

        if (partImg.Rows >= partImg.Cols * 1.5)
        {
            Mat srcCopy = new Mat();
            Cv2.Transpose(partImg, srcCopy);
            Cv2.Flip(srcCopy, srcCopy, 0);
            return srcCopy;
        }
        else
        {
            return partImg;
        }
    }

    public static Mat MatRotateClockWise180(Mat src)
    {
        Cv2.Flip(src, src, FlipMode.Y);
        Cv2.Flip(src, src, FlipMode.X);
        return src;
    }

    public static Mat MatRotateClockWise90(Mat src)
    {
        Cv2.Rotate(src, src, RotateFlags.Rotate90Clockwise);
        return src;
    }
}
