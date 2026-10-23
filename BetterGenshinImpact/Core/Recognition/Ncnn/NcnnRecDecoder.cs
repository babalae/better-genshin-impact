using System;
using System.Collections.Generic;
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
    ///     旧 DLL 或无元信息时按「时间步 = 桶宽 / 8」推断。
    /// </summary>
    public static (int CharCount, int LabelCount) ResolveOutputShape(NcnnTensorInfo? info, int count, int width)
    {
        if (info != null)
        {
            var labels = info.Channels > 0 ? info.Channels : 0;
            var steps = info.Width > 0 ? info.Width : 0;
            if (labels > 1 && steps > 0 && steps * labels == count) return (steps, labels);

            var fallbackSteps = Math.Max(1, width / Downsample);
            if (labels > 1 && count % labels == 0 && count / labels > 0) return (count / labels, labels);
            if (count % fallbackSteps == 0) return (fallbackSteps, count / fallbackSteps);

            throw new InvalidOperationException(
                $"ncnn rec 输出维度异常: dims={info.Dims} {info.ShapeText} count={count}（桶宽 {width}）");
        }

        var charCount = Math.Max(1, width / Downsample);
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
            var rowOffset = n * labelCount;
            var maxVal = float.MinValue;
            var maxIndex = 0;
            for (var c = 0; c < labelCount; c++)
            {
                var value = data[rowOffset + c];
                if (value > maxVal)
                {
                    maxVal = value;
                    maxIndex = c;
                }
            }

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

    /// <summary>与 OcrUtils.GetLabelByIndex 同语义；尾部填充类返回空串（v6 输出类比 字典+2 多 0~1 个）。</summary>
    private static string GetLabel(int index, IReadOnlyList<string> labels)
    {
        if (index > 0 && index <= labels.Count) return labels[index - 1];
        if (index == labels.Count + 1) return " ";
        return "";
    }
}
