using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

public class Rec(
    BgiOnnxModel model,
    IReadOnlyList<string> labels,
    OcrVersionConfig config,
    Dictionary<string, float>? extraWeights, // 额外的权重参数
    float scoreThreshold, // 识别结果分数阈值，低于该值的结果将被过滤掉
    BgiOnnxFactory bgiOnnxFactory)
    : IDisposable
{
    private readonly InferenceSession _session = bgiOnnxFactory.CreateInferenceSession(model, true);

    private readonly float[]? _weights = extraWeights is null || extraWeights.Count <= 0
        ? null
        : OcrUtils.CreateWeights(extraWeights, labels);

    private bool _disposed;

    // _labels = File.ReadAllLines(labelFilePath);

    public void Dispose()
    {
        if (_disposed) return;
        lock (_session)
        {
            _session.Dispose();
        }

        GC.SuppressFinalize(this);
        _disposed = true;
    }


    ~Rec()
    {
        Dispose();
    }

    /// <summary>
    ///     Run OCR recognition on multiple images in batches.
    /// </summary>
    /// <param name="srcs">Array of images for OCR recognition.</param>
    /// <param name="batchSize">Size of the batch to run OCR recognition on.</param>
    /// <returns>Array of <see cref="OcrRecognizerResult" /> instances corresponding to OCR recognition results of the images.</returns>
    public OcrRecognizerResult[] Run(Mat[] srcs, int batchSize = 0)
    {
        if (srcs.Length == 0) return [];

        var chooseBatchSize = batchSize != 0 ? batchSize : Math.Min(8, Environment.ProcessorCount);
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Det));
        }

        return srcs
            .Select((x, i) => (mat: x, i))
            .OrderBy(x => x.mat.Width)
            .Chunk(chooseBatchSize)
            .Select(x => (result: RunMulti(x.Select(x1 => x1.mat).ToArray()), ids: x.Select(x1 => x1.i).ToArray()))
            .SelectMany(x => x.result.Zip(x.ids, (result, i) => (result, i)))
            .OrderBy(x => x.i)
            .Select(x => x.result)
            .ToArray();
    }

    public OcrRecognizerResult Run(Mat src)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Det));
        }

        return RunMulti([src]).Single();
    }

    private OcrRecognizerResult[] RunMulti(Mat[] srcs)
    {
        if (srcs.Length == 0) return [];

        for (var i = 0; i < srcs.Length; ++i)
        {
            var src = srcs[i];
            if (src.Empty())
                throw new ArgumentException($"src[{i}] size should not be 0, wrong input picture provided?");
        }

        var modelHeight = config.Shape.Height;
        var maxWidth = (int)Math.Ceiling(srcs.Max(src =>
        {
            var size = src.Size();
            return 1.0 * size.Width / size.Height * modelHeight;
        }));


        return srcs
            .AsParallel()
            .Select(src =>
            {
                using var channel3 = src.Channels() switch
                {
                    4 => src.CvtColor(ColorConversionCodes.BGRA2BGR),
                    1 => src.CvtColor(ColorConversionCodes.GRAY2BGR),
                    3 => null,
                    var x => throw new Exception($"Unexpect src channel: {x}, allow: (1/3/4)")
                };
                var inputTensor = OcrUtils.ResizeNormImg(channel3 ?? src, new OcrShape(3, maxWidth, modelHeight),
                    out var owner);
                try
                {
                    using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run([
                        NamedOnnxValue.CreateFromTensor(_session.InputNames[0], inputTensor)
                    ]);
                    var output = results[0];
                    if (output.ElementType is not TensorElementType.Float)
                        throw new Exception($"Unexpected output tensor type: {output.ElementType}");

                    if (output.ValueType is not OnnxValueType.ONNX_TYPE_TENSOR)
                        throw new Exception($"Unexpected output tensor value type: {output.ValueType}");
                    var tensor = output.AsTensor<float>();
                    // 因为一个已知bug,tensor中内存在dml下使用完后会被释放掉,锁之外的代码会报错
                    return (tensor.Dimensions.ToArray(), tensor.ToArray());
                }
                finally
                {
                    owner.Dispose();
                }
            }).Select(resultTensor =>
            {
                var resultArray = resultTensor.Item2;
                var resultShape = resultTensor.Item1;

                var labelCount = resultShape[2];
                var charCount = resultShape[1];
                var chars = new (int, float)[charCount * resultShape[0]];
                Parallel.For(0, charCount * resultShape[0], n =>
                {
                    // 0是无效，labels+1 是blank 所以labelCount比labels的长度多2
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
                            var maxIdx2 = new int[2];
                            row.MinMaxIdx(out _, out var maxVal1, [], maxIdx2);
                            mat.MinMaxIdx(out _, out maxVal, [], maxIdx);
                            if (maxVal1 != maxVal)
                            {
                                var a = 1;
                                
                            }
                        }
                    }

                    chars[n] = (maxIdx[1], (float)maxVal);
                });
                return chars;
            }).Select(chars =>
            {
                float score = 0;
                var result = new StringBuilder();
                foreach (var (index, charScore) in chars)
                {
                    if (index != 0 && charScore > scoreThreshold)
                    {
                        //todo 防止叠词词
                        score += charScore;
                        result.Append(OcrUtils.GetLabelByIndex(index, labels));
                    }
                }

                return new OcrRecognizerResult(result.ToString(),
                    score / chars.Length);
            }).ToArray();
    }
}