using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

public class RecMatch : Rec
{
    private readonly int[] _labelLengths;
    private readonly IReadOnlyDictionary<string, int> _labelDict;
    private readonly float[]? _weights;

    private readonly CacheHelper.LruCache<string, int[]> _targetCache =
        new CacheHelper.LruCacheBuilder<string, int[]>().Build();


    public RecMatch(BgiOnnxModel model,
        IReadOnlyList<string> labels,
        OcrVersionConfig config,
        BgiOnnxFactory bgiOnnxFactory,
        float scoreThreshold = 0f,
        Dictionary<string, float>? extraWeights = null,
        bool allowDuplicateChar = false) : base(model, labels, config, bgiOnnxFactory, scoreThreshold, extraWeights,
        allowDuplicateChar)
    {
        _labelDict = OcrUtils.CreateLabelDict(labels, out var labelLengths);
        _labelLengths = labelLengths;
        _weights = ExtraWeights is null || ExtraWeights.Count <= 0
            ? null
            : OcrUtils.CreateWeights(ExtraWeights, Labels);
    }

    public int[] GetTarget(string target)
    {
        if (_targetCache.TryGet(target, out var cached))
        {
            if (cached is not null)
                return cached;
        }

        var chars = target.ToCharArray();
        var targetIndices = new int[chars.Length];
        Array.Fill(targetIndices, -1); // 初始化为0，表示未匹配
        var index = 0;
        while (index < chars.Length)
        {
            var found = false;
            foreach (var labelLength in _labelLengths)
            {
                // 检查当前字符是否可以匹配到标签
                if (index + labelLength > chars.Length) continue; // 超出范围，跳过
                var subStr = new string(chars, index, labelLength);
                if (!_labelDict.TryGetValue(subStr, out var labelIndex)) continue;
                targetIndices[index] = labelIndex; // 记录匹配的标签索引
                index += labelLength; // 移动到下一个字符位置
                found = true; // 标记已找到匹配
                break; // 找到匹配，跳出循环
            }

            if (!found)
            {
                index++; // 如果没有找到匹配，移动到下一个字符位置
            }
        }

        var result = targetIndices.Where(x => x != -1).ToArray(); // 过滤掉未匹配的0
        // 缓存结果
        _targetCache.Set(target, result);
        return result;
    }

    public double RunMatch(Mat[] srcs, string target, int batchSize = 0)
    {
        if (srcs.Length == 0) return 0;

        var chooseBatchSize = batchSize != 0 ? batchSize : Math.Min(8, Environment.ProcessorCount);
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(Det));
        }

        var targetIndexes = GetTarget(target);

        var orderedResults = srcs
            .Select((x, i) => (mat: x, i))
            .OrderBy(x => x.mat.Width)
            .Chunk(chooseBatchSize)
            .Select(x => (result: RunMultiMatch(x.Select(x1 => x1.mat).ToArray(), targetIndexes), ids: x.Select(x1 => x1.i).ToArray()))
            .SelectMany(x => x.result.Zip(x.ids, (result, i) => (result, i)))
            .OrderBy(x => x.i)
            .Select(x => x.result)
            .ToArray();

        return GetMaxScore(orderedResults, targetIndexes);
    }

    private (int, float)[][] RunMultiMatch(Mat[] srcs, int[] targetIndexes)
    {
        if (srcs.Length == 0) return [];
        CheckInputMats(srcs);
        var modelHeight = Config.Shape.Height;
        var maxWidth = GetMaxWidth(srcs);

        return srcs
            .AsParallel()
            .Select(src => RunSingle(src, maxWidth, modelHeight))
            .Select(resultTensor =>
            {
                // 后处理为索引-置信度序列
                var resultArray = resultTensor.Item2;
                var resultShape = resultTensor.Item1;

                var labelCount = resultShape[2];
                var charCount = resultShape[1];
                var dim = resultShape[0];
                var chars = new (int, float)[charCount * dim];
                Parallel.For(0, charCount * dim, n =>
                {
                    var maxIdx = new int[2];
                    double maxVal;
                    var subArray = new float[labelCount];
                    Array.Copy(resultArray, n * labelCount, subArray, 0, labelCount);
                    using (var row = Mat.FromPixelData(1, labelCount, MatType.CV_32FC1, subArray))
                    {
                        if (_weights is null)
                        {
                            row.MinMaxIdx(out _, out maxVal, [], maxIdx);
                        }
                        else
                        {
                            using var weightMat = Mat.FromPixelData(1, labelCount, MatType.CV_32FC1, _weights);
                            using Mat mat = row.Mul(weightMat);
                            mat.MinMaxIdx(out _, out maxVal, [], maxIdx);
                        }
                    }

                    chars[n] = (maxIdx[1], (float)maxVal);
                });

                // 过滤无效与低分项
                var filtered = chars.Where(t => t.Item1 != 0 && t.Item2 > ScoreThreshold).ToArray();
                return filtered;
            }).ToArray();
    }

    private double GetMaxScore((int, float)[][] result, int[] target)
    {
        // 把result打平
        var flatResult = result.SelectMany(x => x).ToArray();
        var resultAvailableCount = result.Count(item => item.Length != 0);
        return GetMaxScore(flatResult, target, Math.Max(resultAvailableCount, target.Length));
    }


    /// <summary>
    /// 计算结果与目标的最大匹配分数
    /// </summary>
    /// <param name="result">int代表index，float为置信度</param>
    /// <param name="target">要匹配的index</param>
    /// <param name="availableCount">包含的文本数</param>
    /// <returns>最大平均置信度</returns>
    private double GetMaxScore((int, float)[] result, int[] target, int availableCount = -1)

    {
        if (availableCount == -1)
        {
            availableCount = target.Length; // 默认使用目标长度
        }

        if (target.Length == 0) return 0; // 目标序列为空，直接返回0

        // 初始化dp数组，dp[j]表示匹配到target前j个元素的最大置信度和
        var dp = new double[target.Length + 1];

        // 初始状态：dp[0]=0（匹配0个元素），其他为负（不可达状态）
        dp[0] = 0;
        for (var j = 1; j <= target.Length; j++)
        {
            dp[j] = -255d;
        }

        // 遍历result的每个元素
        foreach (var tuple in result)
        {
            var index = tuple.Item1;
            var confidence = tuple.Item2;
            // 逆序更新dp（避免重复使用当前元素）
            for (var j = target.Length; j >= 1; j--)
            {
                if (index != target[j - 1]) continue;
                // 只有前序状态可达时才更新
                if (!(dp[j - 1] > -200)) continue;
                var newSum = dp[j - 1] + confidence;
                if (newSum > dp[j])
                {
                    dp[j] = newSum; // 更新当前状态
                }
            }
        }

        // 检查是否完整匹配
        if (dp[target.Length] <= -200)
        {
            return 0; // 无法完整匹配
        }

        // 计算平均置信度
        return dp[target.Length] / availableCount;
    }
}