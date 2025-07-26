using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace BetterGenshinImpact.Core.Recognition.OCR.Engine;

public static class OcrUtils
{
    /// <summary>
    ///     预处理速度比unsafe快5倍以上,且吃的资源还少
    /// </summary>
    /// <param name="inputImage">输入图像，若不是灰度图会转换</param>
    /// <param name="tensorMemoryOwnser">tensor的Memory，用完需要释放</param>
    /// <returns></returns>
    public static Tensor<float> ToTensorYapDnn(Mat inputImage, out IMemoryOwner<float> tensorMemoryOwnser)
    {
        using var rt = new ResourcesTracker();
        Mat dst;
        // 221*32是个什么鬼
        if (inputImage.Channels() > 1)
        {
            var resize = rt.T(ResizeHelper.ResizeTo(inputImage, 221, 32));
            dst = rt.NewMat(resize.Size(), MatType.CV_8UC1, Scalar.Black);
            Cv2.CvtColor(resize, dst, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            dst = rt.T(ResizeHelper.ResizeTo(inputImage, 221, 32));
        }

        // 填充到 384x32
        var padded = rt.NewMat(new Size(384, 32), MatType.CV_8UC1, Scalar.Black);
        padded[new Rect(0, 0, 221, 32)] = dst;
        // 使用向量运算代替循环
        var blob = rt.T(CvDnn.BlobFromImage(padded, 1.0 / 255.0, default, default, false, false));
        var nCols = padded.Cols * padded.Rows;
        tensorMemoryOwnser = MemoryPool<float>.Shared.Rent(nCols);
        // 内存复制，如果直接传指针构建的话速度还不如多复制一份
        blob.AsSpan<float>().CopyTo(tensorMemoryOwnser.Memory.Span);
        return new DenseTensor<float>(tensorMemoryOwnser.Memory[..nCols], [1, 1, 32, 384]);
    }

    /// <summary>
    ///     用于Det模型
    ///     归一化,标准化并返回Tensor。
    ///     <br />
    ///     归一化:固定范围归一化
    ///     <br />
    ///     标准化:
    ///     Z-Score Normalization
    /// </summary>
    public static Tensor<float> NormalizeToTensorDnn(Mat src,
        float? scale, // scale float32
        float[]? mean, //mean
        float[]? std, //std
        out IMemoryOwner<float> tensorMemoryOwner, bool swapRb = false, bool crop = false, Size size = default)

    {
        scale ??= 0.00392156862745f;
        mean ??= [0.485f, 0.456f, 0.406f];
        std ??= [0.229f, 0.224f, 0.225f];
        using var rt = new ResourcesTracker();
        // 获取图像参数
        var channels = src.Channels();
        if (channels != 3)
            throw new ArgumentException($"图像通道数必须为3,当前为{channels}");
        // var data = rt.T(OcrOperationImpl.NormalizeImageOperation(src, scale, mean, std));
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

        //stdMat.GetArray<float>(out var data);
        // 使用DNN模块创建blob
        var blob = rt.T(CvDnn.BlobFromImage(
            stdMat,
            (double)scale,
            size,
            default,
            swapRb,
            crop
        ));

        // 租用内存并复制数据
        var total = (int)blob.Total();
        tensorMemoryOwner = MemoryPool<float>.Shared.Rent(total);
        blob.AsSpan<float>().CopyTo(tensorMemoryOwner.Memory.Span);
        // 计算输出形状
        return new DenseTensor<float>(
            tensorMemoryOwner.Memory[..total],
            new[] { 1, channels, stdMat.Rows, stdMat.Cols }
        );
    }

    /// <summary>
    ///     不支持通道转换
    ///     <br />
    ///     用于PP-OCR的Rec模型，调整大小之后再归一化到-1~1，之后转换为Tensor
    /// </summary>
    public static Tensor<float> ResizeNormImg(Mat img, OcrShape imageShape,
        out IMemoryOwner<float> tensorMemoryOwner, bool padding = true,
        InterpolationFlags interpolation = InterpolationFlags.Linear)
    {
        using var rt = new ResourcesTracker();
        // var imgC = imageShape.Channel;
        var imgH = imageShape.Height;
        var imgW = imageShape.Width;

        var h = img.Height;
        var w = img.Width;

        var resizedImage = rt.NewMat();
        if (!padding)
        {
            Cv2.Resize(img, resizedImage, new Size(imgW, imgH), 0, 0, interpolation);
            // resized_w = imgW;
        }
        else
        {
            var ratio = w / (double)h;
            var resizedW = Math.Ceiling(imgH * ratio) > imgW ? imgW : (int)Math.Ceiling(imgH * ratio);
            Cv2.Resize(img, resizedImage, new Size(resizedW, imgH), 0, 0, interpolation);
        }

        /*
          resized_image  / 255
    resized_image -= 0.5
    resized_image /= 0.5
         */
        // 归一化到 +-1
        // resizedImage.ConvertTo(resizedImage, MatType.CV_32F, 2 / 255f, 1);
        var blob = rt.T(CvDnn.BlobFromImage(
            resizedImage,
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
            new[] { 1, resizedImage.Channels(), resizedImage.Rows, resizedImage.Cols }
        );
    }

    /// <summary>
    ///     Gets a label by its index.
    /// </summary>
    /// <param name="i">The index of the label.</param>
    /// <param name="labels">The labels to search for the index.</param>
    /// <returns>The label at the specified index.</returns>
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