using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

public class Rec
{
    private readonly OcrVersionConfig _config;
    private readonly IReadOnlyList<string> _labels;
    private readonly InferenceSession _session;

    public Rec(BgiOnnxModel model, string labelFilePath, OcrVersionConfig config, BgiOnnxFactory bgiOnnxFactory)
    {
        _config = config;
        _session = bgiOnnxFactory.CreateInferenceSession(model, true);


        _labels = File.ReadAllLines(labelFilePath);
    }

    ~Rec()
    {
        lock (_session)
        {
            _session.Dispose();
        }
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

        var modelHeight = _config.Shape.Height;
        var maxWidth = (int)Math.Ceiling(srcs.Max(src =>
        {
            var size = src.Size();
            return 1.0 * size.Width / size.Height * modelHeight;
        }));
        List<IMemoryOwner<float>> owners = [];
        (int[], float[])[] resultTensors;
        try
        {
            resultTensors = srcs
                // .AsParallel()
                .Select(src =>
                {
                    using var channel3 = src.Channels() switch
                    {
                        4 => src.CvtColor(ColorConversionCodes.BGRA2BGR),
                        1 => src.CvtColor(ColorConversionCodes.GRAY2BGR),
                        3 => src,
                        var x => throw new Exception($"Unexpect src channel: {x}, allow: (1/3/4)")
                    };
                    var result = OcrUtils.ResizeNormImg(channel3, new OcrShape(3, maxWidth, modelHeight),
                        out var owner);
                    lock (owners)
                    {
                        owners.Add(owner);
                    }

                    return result;
                })
                .Select(inputTensor =>
                    {
                        lock (_session)
                        {
                            // 多线程推理会出现问题，加锁解决。
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
                    }
                ).ToArray();
        }
        finally
        {
            owners.ForEach(x => { x.Dispose(); });
        }

        return resultTensors.SelectMany(resultTensor =>
        {
            var resultArray = resultTensor.Item2;
            var resultShape = resultTensor.Item1;
            GCHandle dataHandle = default;
            try
            {
                dataHandle = GCHandle.Alloc(resultArray, GCHandleType.Pinned);
                var dataPtr = dataHandle.AddrOfPinnedObject();
                var labelCount = resultShape[2];
                var charCount = resultShape[1];

                return Enumerable.Range(0, resultShape[0])
                    .Select(i =>
                    {
                        StringBuilder sb = new();
                        var lastIndex = 0;
                        float score = 0;
                        for (var n = 0; n < charCount; ++n)
                        {
                            using var mat = Mat.FromPixelData(1, labelCount, MatType.CV_32FC1,
                                dataPtr + (n + i * charCount) * labelCount * sizeof(float));
                            var maxIdx = new int[2];
                            mat.MinMaxIdx(out _, out var maxVal, [], maxIdx);

                            if (maxIdx[1] > 0 && !(n > 0 && maxIdx[1] == lastIndex))
                            {
                                score += (float)maxVal;
                                sb.Append(OcrUtils.GetLabelByIndex(maxIdx[1], _labels));
                            }

                            lastIndex = maxIdx[1];
                        }

                        return new OcrRecognizerResult(sb.ToString(), score / sb.Length);
                    })
                    .ToArray();
            }
            finally
            {
                dataHandle.Free();
            }
        }).ToArray();
    }
}