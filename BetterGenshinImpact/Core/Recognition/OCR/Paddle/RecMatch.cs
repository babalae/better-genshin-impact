using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

/// <summary>
/// 基于动态规划的 OCR 模糊匹配识别器。
/// 将目标字符串与模型原始输出序列做子序列匹配，返回 0~1 的置信度分数。
/// 比先识别再字符串匹配更能容忍 OCR 噪声。
/// </summary>
public class RecMatch : Rec
{
    private readonly int[] _labelLengths;
    private readonly IReadOnlyDictionary<string, int> _labelDict;
    private readonly float[]? _weights;

    private readonly CacheHelper.LruCache<string, int[]> _targetCache =
        new CacheHelper.LruCacheBuilder<string, int[]>().Build();

    public RecMatch(
        BgiOnnxModel model,
        IReadOnlyList<string> labels,
        OcrVersionConfig config,
        BgiOnnxFactory bgiOnnxFactory,
        Dictionary<string, float>? extraWeights = null)
        : base(model, labels, config, bgiOnnxFactory)
    {
        _labelDict = OcrUtils.CreateLabelDict(labels, out var labelLengths);
        _labelLengths = labelLengths;
        _weights = extraWeights is null || extraWeights.Count == 0
            ? null
            : OcrUtils.CreateWeights(extraWeights, labels);
    }

    /// <summary>
    /// 将目标字符串转换为标签索引序列，利用 LRU 缓存加速重复查询。
    /// 无法映射到标签的字符会被跳过。
    /// </summary>
    public int[] GetTarget(string target)
    {
        if (_targetCache.TryGet(target, out var cached) && cached is not null)
            return cached;

        var chars = target.ToCharArray();
        var targetIndices = new int[chars.Length];
        Array.Fill(targetIndices, -1);
        var index = 0;
        while (index < chars.Length)
        {
            var found = false;
            foreach (var labelLength in _labelLengths)
            {
                if (index + labelLength > chars.Length) continue;
                var subStr = new string(chars, index, labelLength);
                if (!_labelDict.TryGetValue(subStr, out var labelIndex)) continue;
                targetIndices[index] = labelIndex;
                index += labelLength;
                found = true;
                break;
            }
            if (!found) index++;
        }

        var result = targetIndices.Where(x => x != -1).ToArray();
        _targetCache.Set(target, result);
        return result;
    }

    /// <summary>
    /// 对一批图像执行模糊匹配，返回与目标字符串的最大平均置信度 (0~1)。
    /// </summary>
    /// <param name="srcs">待匹配图像数组</param>
    /// <param name="target">目标字符串</param>
    /// <param name="batchSize">每批推理图像数，0表示自动</param>
    public double RunMatch(Mat[] srcs, string target, int batchSize = 0)
    {
        if (srcs.Length == 0) return 0;
        var targetIndexes = GetTarget(target);
        if (targetIndexes.Length == 0) return 0;

        var chooseBatchSize = batchSize != 0 ? batchSize : Math.Min(8, Environment.ProcessorCount);

        var charLevelResults = srcs
            .Select((x, i) => (mat: x, i))
            .OrderBy(x => x.mat.Width)
            .Chunk(chooseBatchSize)
            .Select(chunk =>
            {
                var mats = chunk.Select(x => x.mat).ToArray();
                var inference = RunInference(mats);
                var charLevel = ProcessToCharLevel(inference);
                return (result: charLevel, ids: chunk.Select(x => x.i).ToArray());
            })
            .SelectMany(x => x.result.Zip(x.ids, (r, i) => (r, i)))
            .OrderBy(x => x.i)
            .Select(x => x.r)
            .ToArray();

        return GetMaxScoreFlat(charLevelResults, targetIndexes);
    }

    /// <summary>
    /// 将 ONNX 原始张量转换为字符级别的 (labelIndex, confidence) 数组。
    /// 过滤 CTC 空白符（index=0），不做 CTC 重复折叠（保留所有帧）。
    /// </summary>
    private (int, float)[][] ProcessToCharLevel((int[], float[])[] resultTensors)
    {
        return resultTensors.Select(resultTensor =>
        {
            var resultArray = resultTensor.Item2;
            var resultShape = resultTensor.Item1;
            var labelCount = resultShape[2];
            var charCount = resultShape[1];
            var dim = resultShape[0];

            GCHandle dataHandle = default;
            try
            {
                dataHandle = GCHandle.Alloc(resultArray, GCHandleType.Pinned);
                var dataPtr = dataHandle.AddrOfPinnedObject();
                var chars = new (int, float)[charCount * dim];
                for (var n = 0; n < charCount * dim; n++)
                {
                    using var row = Mat.FromPixelData(1, labelCount, MatType.CV_32FC1,
                        dataPtr + n * labelCount * sizeof(float));
                    var maxIdx = new int[2];
                    double maxVal;
                    if (_weights is null)
                    {
                        row.MinMaxIdx(out _, out maxVal, [], maxIdx);
                    }
                    else
                    {
                        using var weightMat = Mat.FromPixelData(1, labelCount, MatType.CV_32FC1, _weights);
                        using Mat weighted = row.Mul(weightMat);
                        weighted.MinMaxIdx(out _, out maxVal, [], maxIdx);
                    }
                    chars[n] = (maxIdx[1], (float)maxVal);
                }
                // 过滤 CTC 空白符（index=0）
                return chars.Where(t => t.Item1 != 0).ToArray();
            }
            finally
            {
                dataHandle.Free();
            }
        }).ToArray();
    }

    /// <summary>
    /// 将多张图像的字符级别结果展平后，计算与 target 的最大匹配分数。
    /// </summary>
    private double GetMaxScoreFlat((int, float)[][] result, int[] target)
    {
        var flatResult = result.SelectMany(x => x).ToArray();
        var availableCount = Math.Max(result.Count(item => item.Length != 0), target.Length);
        return GetMaxScoreDP(flatResult, target, availableCount);
    }

    /// <summary>
    /// 动态规划最大子序列匹配。
    /// dp[j] = 在 result 序列中找到 target[0..j-1] 的最大置信度之和。
    /// 最终返回 dp[target.Length] / availableCount，范围 0~1。
    /// </summary>
    private static double GetMaxScoreDP((int, float)[] result, int[] target, int availableCount)
    {
        if (target.Length == 0) return 0;

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
}
