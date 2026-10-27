using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Recognition.OCR;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn rec 的输出形状解析与 CTC 贪心解码：从 <c>NcnnRec</c> 提取的纯函数，
///     与 ONNX Rec.DecodeOne 同语义，独立出来便于无原生依赖的单测覆盖。
/// </summary>
internal static class NcnnRecDecoder
{
    /// <summary>rec 主干下采样倍率：输出时间步 = 输入宽度 / 8。</summary>
    private const int Downsample = 8;

    /// <summary>
    ///     解析输出形状：优先用 shim 返回的 w/h/c（fp32 unpacked 后），
    ///     旧 DLL 或无元信息时按「时间步 = 桶宽 / 8 向上取整」推断。
    /// </summary>
    public static (int CharCount, int LabelCount) ResolveOutputShape(NcnnTensorInfo? info, int count, int width)
    {
        if (info != null)
        {
            // 二维输出（动态宽度模型的常态）：w=类别数，h=时间步
            if (info.Dims == 2 && info.Width > 1 && info.Height > 0 && (long)info.Width * info.Height == count)
                return (info.Height, info.Width);

            var labels = info.Channels > 0 ? info.Channels : 0;
            var steps = info.Width > 0 ? info.Width : 0;
            if (labels > 1 && steps > 0 && steps * labels == count) return (steps, labels);

            var fallbackSteps = Math.Max(1, (int)Math.Ceiling(width / (double)Downsample));
            if (labels > 1 && count % labels == 0 && count / labels > 0) return (count / labels, labels);
            if (count % fallbackSteps == 0) return (fallbackSteps, count / fallbackSteps);

            throw new InvalidOperationException(
                $"ncnn rec 输出维度异常: {info.Describe(count)}（桶宽 {width}）");
        }

        var charCount = Math.Max(1, (int)Math.Ceiling(width / (double)Downsample));
        if (count % charCount != 0)
            throw new InvalidOperationException($"ncnn rec 输出维度异常: {count} 不能被 {charCount} 整除");
        return (charCount, count / charCount);
    }

    /// <summary>CTC 贪心解码单行：并列取最小索引，连续重复去重，0 为空白；分数取命中字符均值。</summary>
    public static OcrRecognizerResult Decode(float[] data, int charCount, int labelCount, IReadOnlyList<string> labels)
    {
        System.Text.StringBuilder sb = new();
        var lastIndex = 0;
        float score = 0;
        for (var n = 0; n < charCount; ++n)
        {
            // 每个时间步对 labelCount（字典可达上万）求 argmax 是解码的主要成本，
            // 用两遍 SIMD（先归约最大值再定位首个命中）代替逐元素标量比较。
            var (maxVal, maxIndex) = ArgMax(data.AsSpan(n * labelCount, labelCount));

            if (maxIndex > 0 && !(n > 0 && maxIndex == lastIndex))
            {
                score += maxVal;
                sb.Append(GetLabel(maxIndex, labels));
            }

            lastIndex = maxIndex;
        }

        var text = sb.ToString();
        return new OcrRecognizerResult(text, text.Length > 0 ? score / text.Length : 0);
    }

    /// <summary>
    ///     行 argmax：第一遍向量化归约最大值，第二遍定位第一个命中槽（与标量版一致，并列取最小索引）。
    ///     行太短时 SIMD 无收益走标量；NaN 等异常值兜底回标量（logits 正常不含 NaN）。
    /// </summary>
    private static (float Max, int Index) ArgMax(ReadOnlySpan<float> row)
    {
        var lanes = Vector<float>.Count;
        if (row.Length < lanes * 2) return ArgMaxScalar(row);

        var vectors = MemoryMarshal.Cast<float, Vector<float>>(row);
        var maxVector = vectors[0];
        for (var i = 1; i < vectors.Length; i++) maxVector = Vector.Max(maxVector, vectors[i]);

        var max = maxVector[0];
        for (var i = 1; i < lanes; i++) max = MathF.Max(max, maxVector[i]);
        for (var i = vectors.Length * lanes; i < row.Length; i++) max = MathF.Max(max, row[i]);

        var target = new Vector<float>(max);
        for (var vi = 0; vi < vectors.Length; vi++)
        {
            if (!Vector.EqualsAny(vectors[vi], target)) continue;
            var baseIndex = vi * lanes;
            for (var c = 0; c < lanes; c++)
                if (vectors[vi][c] == max) return (max, baseIndex + c);
        }
        for (var i = vectors.Length * lanes; i < row.Length; i++)
            if (row[i] == max) return (max, i);

        // 理论不可达（NaN 等使 Vector.Max 与标量语义分叉时兜底），保持结果确定。
        return ArgMaxScalar(row);
    }

    private static (float Max, int Index) ArgMaxScalar(ReadOnlySpan<float> row)
    {
        var maxVal = float.MinValue;
        var maxIndex = 0;
        for (var c = 0; c < row.Length; c++)
        {
            if (row[c] > maxVal)
            {
                maxVal = row[c];
                maxIndex = c;
            }
        }
        return (maxVal, maxIndex);
    }

    /// <summary>与 OcrUtils.GetLabelByIndex 同语义；尾部填充类返回空串（v6 输出类比 字典+2 多 0~1 个）。</summary>
    private static string GetLabel(int index, IReadOnlyList<string> labels)
    {
        if (index > 0 && index <= labels.Count) return labels[index - 1];
        if (index == labels.Count + 1) return " ";
        return "";
    }
}
