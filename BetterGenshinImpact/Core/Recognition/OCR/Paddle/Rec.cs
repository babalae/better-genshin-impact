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

public class Rec
    : IDisposable
{
    protected BgiOnnxModel Model;
    protected readonly IReadOnlyList<string> Labels;
    protected readonly OcrVersionConfig Config;
    protected readonly BgiOnnxFactory BgiOnnxFactory;
    protected readonly float ScoreThreshold;
    protected readonly Dictionary<string, float>? ExtraWeights;
    protected readonly bool AllowDuplicateChar;
    private readonly InferenceSession _session;
    private readonly float[]? _weights;

    public Rec(BgiOnnxModel model,
        IReadOnlyList<string> labels,
        OcrVersionConfig config,
        BgiOnnxFactory bgiOnnxFactory,
        float scoreThreshold = 0f, // 识别结果分数阈值，低于该值的结果将被过滤掉
        Dictionary<string, float>? extraWeights = null, // 额外的权重参数
        bool allowDuplicateChar = false // 是否允许重复字符
    )
    {
        Model = model;
        Labels = labels;
        Config = config;
        BgiOnnxFactory = bgiOnnxFactory;
        ScoreThreshold = scoreThreshold;
        ExtraWeights = extraWeights;
        AllowDuplicateChar = allowDuplicateChar;
        _session = BgiOnnxFactory.CreateInferenceSession(Model, true);
        _weights = ExtraWeights is null || ExtraWeights.Count <= 0
            ? null
            : OcrUtils.CreateWeights(ExtraWeights, Labels);

    }


    // _labels = File.ReadAllLines(labelFilePath);
    protected bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed) return;
        lock (_session)
        {
            _session.Dispose();
        }

        GC.SuppressFinalize(this);
        IsDisposed = true;
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
        if (IsDisposed)
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
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(Det));
        }

        return RunMulti([src]).Single();
    }

    protected void CheckInputMats(Mat[] srcs)
    {
        for (var i = 0; i < srcs.Length; ++i)
        {
            var src = srcs[i];
            if (src.Empty())
                throw new ArgumentException($"src[{i}] size should not be 0, wrong input picture provided?");
        }
    }

    private OcrRecognizerResult[] RunMulti(Mat[] srcs)
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
                // 后处理
                var resultArray = resultTensor.Item2;
                var resultShape = resultTensor.Item1;

                var labelCount = resultShape[2];
                var charCount = resultShape[1];
                var dim = resultShape[0];
                var chars = new (int, float)[charCount * dim];
                Parallel.For(0, charCount * dim, n =>
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
                            mat.MinMaxIdx(out _, out maxVal, [], maxIdx);
                        }
                    }

                    chars[n] = (maxIdx[1], (float)maxVal);
                });
                return UncodeChars(chars, charCount);
            }).ToArray();
    }

    /// <summary>
    /// 获取最大宽度，按比例缩放后，保持长边不超过 config.Shape.Height 的宽度。
    /// </summary>
    protected int GetMaxWidth(Mat[] srcs)
    {
        return (int)Math.Ceiling(srcs.Max(src =>
        {
            var size = src.Size();
            return 1.0 * size.Width / size.Height * Config.Shape.Height;
        }));
    }

    /// <summary>
    /// 对单个Mat保持线程安全。前处理以及推理部分。
    /// </summary>
    protected (int[], float[]) RunSingle(Mat src, int maxWidth, int modelHeight)
    {
        // 推理部分
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
    }

    /// <summary>
    /// 把识别结果解码为OcrRecognizerResult
    /// </summary>
    /// <param name="chars">未解码的字符</param>
    /// <param name="charCount">单一维度下的字符数</param>
    /// <returns></returns>
    private OcrRecognizerResult UncodeChars(
        (int, float)[] chars,
        int? charCount = null)
    {
        // 解码部分
        float score = 0;
        var result = new StringBuilder();
        var lastIndex = 0;
        for (int i = 0; i < chars.Length; i++)
        {
            var (index, charScore) = chars[i];
            if (index != 0 && charScore > ScoreThreshold)
            {
                if (AllowDuplicateChar || i % (charCount ?? chars.Length) == 0 || index != lastIndex)
                {
                    score += charScore;
                    result.Append(OcrUtils.GetLabelByIndex(index, Labels));
                }
            }

            lastIndex = index;
        }

        return new OcrRecognizerResult(result.ToString(),
            score / chars.Length);
    }
}