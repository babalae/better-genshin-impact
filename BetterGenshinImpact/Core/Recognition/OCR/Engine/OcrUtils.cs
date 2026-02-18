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
    /// <param name="tensorMemoryOwner">tensor的Memory，用完需要释放</param>
    /// <returns></returns>
    public static Tensor<float> ToTensorYapDnn(Mat inputImage, out IMemoryOwner<float> tensorMemoryOwner)
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
        tensorMemoryOwner = MemoryPool<float>.Shared.Rent(nCols);
        // 内存复制，如果直接传指针构建的话速度还不如多复制一份
        blob.AsSpan<float>().CopyTo(tensorMemoryOwner.Memory.Span);
        return new DenseTensor<float>(tensorMemoryOwner.Memory[..nCols], [1, 1, 32, 384]);
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

    /// <summary>
    /// 从标签列表构建字符串→索引字典，供 Rec 模糊匹配使用。
    /// 索引从1开始（0为CTC空白符），空格字符为 labels.Count+1。
    /// </summary>
    /// <param name="labels">识别模型的标签列表</param>
    /// <param name="labelLengths">各标签的字符长度集合（降序排列，用于从长到短贪心匹配）</param>
    public static IReadOnlyDictionary<string, int> CreateLabelDict(
        IReadOnlyList<string> labels, out int[] labelLengths)
    {
        var dict = new Dictionary<string, int>();
        var lengths = new HashSet<int>();
        for (var i = 0; i < labels.Count; i++)
        {
            if (labels[i] == " ") continue;
            var len = labels[i].Length;
            if (len > 0) lengths.Add(len);
            dict[labels[i]] = i + 1;
        }
        // 空格字符对应索引 labels.Count + 1
        dict[" "] = labels.Count + 1;
        lengths.Add(1);
        // 降序：先尝试更长的标签
        labelLengths = lengths.OrderByDescending(x => x).ToArray();
        return dict;
    }

    /// <summary>
    /// 根据额外权重字典，创建与标签列表等长的权重数组（用于加权推理分数）。
    /// 未指定权重的标签默认为 1.0。
    /// </summary>
    public static float[] CreateWeights(
        Dictionary<string, float> extraWeights, IReadOnlyDictionary<string, int> labelDict, int labelCount)
    {
        var result = new float[labelCount + 2];
        Array.Fill(result, 1.0f);
        foreach (var (key, value) in extraWeights)
        {
            if (!labelDict.TryGetValue(key, out var index)) continue;
            if (index >= 0 && index < result.Length)
            {
                result[index] = value;
            }
        }
        return result;
    }

    /// <summary>
    /// 将目标字符串映射为标签索引序列。
    /// 使用贪心从长到短匹配，无法映射的字符会被跳过。
    /// </summary>
    /// <param name="target">目标字符串</param>
    /// <param name="labelDict">标签→索引字典（由 CreateLabelDict 生成）</param>
    /// <param name="labelLengths">标签长度集合，降序排列（由 CreateLabelDict 生成）</param>
    public static int[] MapStringToLabelIndices(
        string target,
        IReadOnlyDictionary<string, int> labelDict,
        int[] labelLengths)
    {
        var chars = target.ToCharArray();
        var targetIndices = new int[chars.Length];
        Array.Fill(targetIndices, -1);
        var index = 0;
        while (index < chars.Length)
        {
            var found = false;
            foreach (var labelLength in labelLengths)
            {
                if (index + labelLength > chars.Length) continue;
                var subStr = new string(chars, index, labelLength);
                if (!labelDict.TryGetValue(subStr, out var labelIndex)) continue;
                targetIndices[index] = labelIndex;
                index += labelLength;
                found = true;
                break;
            }
            if (!found) index++;
        }

        return targetIndices.Where(x => x != -1).ToArray();
    }

    /// <summary>
    /// 动态规划最大子序列匹配。
    /// 在 result 序列中找到 target 的最大置信度子序列匹配，返回归一化分数 (0~1)。
    /// </summary>
    /// <param name="result">OCR 输出的 (labelIndex, confidence) 序列</param>
    /// <param name="target">目标标签索引序列</param>
    /// <param name="availableCount">归一化分母（通常为 target.Length，得到每个目标字符的平均置信度）</param>
    public static double GetMaxScoreDp((int, float)[] result, int[] target, int availableCount)
    {
        if (target.Length == 0 || availableCount <= 0) return 0;

        var dp = new double[target.Length + 1];
        dp[0] = 0;
        for (var j = 1; j <= target.Length; j++)
            dp[j] = -255d; // 不可达

        foreach (var (index, confidence) in result)
        {
            // 逆序更新，避免同一 result 元素被多次使用
            for (var j = target.Length; j >= 1; j--)
            {
                if (index != target[j - 1]) continue;
                if (!(dp[j - 1] > -200)) continue; // 前序不可达
                var newSum = dp[j - 1] + confidence;
                if (newSum > dp[j]) dp[j] = newSum;
            }
        }

        if (dp[target.Length] <= -200) return 0; // 无法完整匹配
        return dp[target.Length] / availableCount;
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