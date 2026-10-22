using System.Buffers;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace OcrDmlProbe.Ocr;

/// <summary>与 BetterGI OcrUtils 逐行一致的预处理/后处理工具（仅保留 PpOcr 用到的部分）。</summary>
public static class OcrUtils
{
    /// <summary>用于Det模型：归一化 + Z-Score 标准化 + BlobFromImage 转 Tensor。</summary>
    public static Tensor<float> NormalizeToTensorDnn(Mat src,
        float? scale,
        float[]? mean,
        float[]? std,
        out IMemoryOwner<float> tensorMemoryOwner, bool swapRb = false, bool crop = false, Size size = default)
    {
        scale ??= 0.00392156862745f;
        mean ??= [0.485f, 0.456f, 0.406f];
        std ??= [0.229f, 0.224f, 0.225f];
        using var rt = new ResourcesTracker();
        var channels = src.Channels();
        if (channels != 3)
            throw new ArgumentException($"图像通道数必须为3,当前为{channels}");
        var stdMat = rt.NewMat();
        Mat[] bgr = [];
        try
        {
            bgr = src.Split();
            for (var i = 0; i < bgr.Length; ++i)
                bgr[i].ConvertTo(bgr[i], MatType.CV_32FC1, 1 / std[i],
                    (0.0 - mean[i]) / std[i] / (float)scale);
            Cv2.Merge(bgr, stdMat);
        }
        finally
        {
            foreach (var channel in bgr) channel.Dispose();
        }

        var blob = rt.T(CvDnn.BlobFromImage(
            stdMat,
            (double)scale,
            size,
            default,
            swapRb,
            crop
        ));

        var total = (int)blob.Total();
        tensorMemoryOwner = MemoryPool<float>.Shared.Rent(total);
        blob.AsSpan<float>().CopyTo(tensorMemoryOwner.Memory.Span);
        return new DenseTensor<float>(
            tensorMemoryOwner.Memory[..total],
            new[] { 1, channels, stdMat.Rows, stdMat.Cols }
        );
    }

    /// <summary>用于PP-OCR的Rec模型：调整大小 + pad 到固定档宽 + 归一化到 -1~1 + 转 Tensor。</summary>
    public static Tensor<float> ResizeNormImg(Mat img, OcrShape imageShape,
        out IMemoryOwner<float> tensorMemoryOwner, bool padding = true,
        InterpolationFlags interpolation = InterpolationFlags.Linear)
    {
        using var rt = new ResourcesTracker();
        var imgH = imageShape.Height;
        var imgW = imageShape.Width;

        var h = img.Height;
        var w = img.Width;

        var resizedImage = rt.NewMat();
        Mat tensorSource;
        if (!padding)
        {
            Cv2.Resize(img, resizedImage, new Size(imgW, imgH), 0, 0, interpolation);
            tensorSource = resizedImage;
        }
        else
        {
            var ratio = w / (double)h;
            var resizedW = Math.Ceiling(imgH * ratio) > imgW ? imgW : (int)Math.Ceiling(imgH * ratio);
            Cv2.Resize(img, resizedImage, new Size(resizedW, imgH), 0, 0, interpolation);
            if (resizedW < imgW)
            {
                // pad 到目标宽度：张量形状固定为 [1,3,imgH,imgW]，CTC 解码在 pad 区域输出空白。
                var padded = rt.NewMat(new Size(imgW, imgH), resizedImage.Type(), Scalar.Black);
                using var roi = padded[new Rect(0, 0, resizedW, imgH)];
                resizedImage.CopyTo(roi);
                tensorSource = padded;
            }
            else
            {
                tensorSource = resizedImage;
            }
        }

        var blob = rt.T(CvDnn.BlobFromImage(
            tensorSource,
            2 / 255f,
            default,
            new Scalar(127.5, 127.5, 127.5),
            false,
            false
        ));

        var total = blob.Total();
        tensorMemoryOwner = MemoryPool<float>.Shared.Rent((int)total);
        blob.AsSpan<float>().CopyTo(tensorMemoryOwner.Memory.Span);
        return new DenseTensor<float>(
            tensorMemoryOwner.Memory[..(int)total],
            new[] { 1, tensorSource.Channels(), tensorSource.Rows, tensorSource.Cols }
        );
    }

    public static string GetLabelByIndex(int i, IReadOnlyList<string> labels)
    {
        return i switch
        {
            var x when x > 0 && x <= labels.Count => labels[x - 1],
            var x when x == labels.Count + 1 => " ",
            _ => throw new Exception(
                $"Unable to GetLabelByIndex: index {i} out of range {labels.Count}, OCR model or labels not matched?")
        };
    }

    public static Mat Tensor2Mat(Tensor<float> tensor)
    {
        var dimensions = tensor.Dimensions;
        if (dimensions.Length != 4 || dimensions[0] != 1 || dimensions[1] != 1)
            throw new ArgumentException($"wrong tensor shape: {string.Join(",", dimensions.ToArray())}");
        if (tensor is not DenseTensor<float> denseTensor)
            return Mat.FromPixelData(dimensions[2], dimensions[3], MatType.CV_32FC1, tensor.ToArray());
        var mat = new Mat(new Size(dimensions[3], dimensions[2]), MatType.CV_32FC1);
        denseTensor.Buffer.Span.CopyTo(mat.AsSpan<float>());
        return mat;
    }
}
