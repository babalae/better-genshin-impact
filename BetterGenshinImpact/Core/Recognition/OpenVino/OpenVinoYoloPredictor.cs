using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Compunet.YoloSharp.Data;
using Compunet.YoloSharp.Metadata;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Size = SixLabors.ImageSharp.Size;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

/// <summary>
///     OpenVINO 后端的 YOLO 检测器：替代 YoloSharp 的完整 Detect / Classify 路径。
///     预处理 / 解码 / NMS 与 ncnn 后端共用 <see cref="YoloBackendShared" />（逐条复刻 YoloSharp 6.0.3），
///     推理直接使用现有 ONNX 模型文件，本类只负责 OpenVINO 会话与推理。
///     实现者须自行串行化 Detect / Classify（调用方可能从多线程进入）。
/// </summary>
internal sealed class OpenVinoYoloPredictor : IYoloPredictorBackend
{
    private readonly OpenVinoSession _session;
    private readonly YoloName[] _names;
    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly float _confidence;
    private readonly float _iou;
    private readonly object _inferLock = new();
    private bool _disposed;

    /// <summary>是否为分类模型（输出 [1,nc] 分数）；否则为检测模型（输出 [1,4+nc,N]）。</summary>
    public bool IsClassifier { get; }

    public string ConfigName { get; }

    public OpenVinoYoloPredictor(string modelPath, string deviceId, bool enableCache,
        YoloName[] names, int width, int height, int channels = 3,
        float confidence = YoloBackendShared.DefaultConfidence, float iou = YoloBackendShared.DefaultIou,
        bool isClassifier = false)
    {
        if (names.Length == 0) throw new ArgumentException("类别名不能为空", nameof(names));
        ConfigName = $"YOLO OpenVINO ({Path.GetFileName(modelPath)})";
        _names = names;
        _width = width;
        _height = height;
        _channels = channels;
        _confidence = confidence;
        _iou = iou;
        IsClassifier = isClassifier;
        _session = new OpenVinoSession(modelPath, deviceId, enableCache);
    }

    /// <summary>检测：letterbox 预处理 + OpenVINO 推理 + 解码 + NMS，返回与 YoloSharp 相同的结果类型。</summary>
    public YoloResult<Detection> Detect(Image<Rgb24> image)
    {
        lock (_inferLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var sw = Stopwatch.StartNew();
            var (output, preMs, inferMs) = InferPreprocessed(image);
            try
            {
                var (anchorCount, attributeCount) = ResolveLayout(output);
                var rawBoxes = YoloBackendShared.DecodeAndNmsCore(output.Data, anchorCount, attributeCount,
                    _names.Length, _confidence, _iou);
                var (adjPadX, adjPadY, ratio) = YoloBackendShared.ComputeAdjustment(image.Size, _width, _height);
                var detections = new Detection[rawBoxes.Count];
                for (var i = 0; i < rawBoxes.Count; i++)
                {
                    var b = rawBoxes[i];
                    detections[i] = new Detection
                    {
                        Name = _names[b.ClassIndex],
                        Bounds = new Rectangle(
                            (int)((b.X - adjPadX) * ratio),
                            (int)((b.Y - adjPadY) * ratio),
                            (int)(b.W * ratio),
                            (int)(b.H * ratio)),
                        Confidence = b.Confidence,
                    };
                }

                var postMs = sw.Elapsed.TotalMilliseconds - preMs - inferMs;
                return new YoloResult<Detection>(detections)
                {
                    ImageSize = new Size(image.Width, image.Height),
                    Speed = new SpeedResult(
                        TimeSpan.FromMilliseconds(preMs),
                        TimeSpan.FromMilliseconds(inferMs),
                        TimeSpan.FromMilliseconds(postMs)),
                };
            }
            finally
            {
                ArrayPool<float>.Shared.Return(output.Data);
            }
        }
    }

    /// <summary>
    ///     分类：与 YoloSharp ClassificationParser 同语义 —— 输出 [1,nc] 直接读分数（模型自带 softmax），
    ///     全部类别按置信度降序返回，不做阈值筛选。
    /// </summary>
    public YoloResult<Classification> Classify(Image<Rgb24> image)
    {
        lock (_inferLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var sw = Stopwatch.StartNew();
            var (output, preMs, inferMs) = InferPreprocessed(image);
            try
            {
                if (output.Data.Length < _names.Length)
                    throw new InvalidOperationException(
                        $"OpenVINO 分类输出维度异常: {output.Data.Length} < 类别数 {_names.Length}");
                var classifications = new Classification[_names.Length];
                for (var i = 0; i < _names.Length; i++)
                {
                    classifications[i] = new Classification
                    {
                        Name = _names[i],
                        Confidence = output.Data[i],
                    };
                }

                Array.Sort(classifications, static (x, y) => y.Confidence.CompareTo(x.Confidence));
                var postMs = sw.Elapsed.TotalMilliseconds - preMs - inferMs;
                return new YoloResult<Classification>(classifications)
                {
                    ImageSize = new Size(image.Width, image.Height),
                    Speed = new SpeedResult(
                        TimeSpan.FromMilliseconds(preMs),
                        TimeSpan.FromMilliseconds(inferMs),
                        TimeSpan.FromMilliseconds(postMs)),
                };
            }
            finally
            {
                ArrayPool<float>.Shared.Return(output.Data);
            }
        }
    }

    /// <summary>letterbox 预处理与推理；返回拷出数据的首个输出（调用方负责归还缓冲）。</summary>
    private (PooledOutput Output, double PreMs, double InferMs) InferPreprocessed(
        Image<Rgb24> image)
    {
        var preSw = Stopwatch.StartNew();
        var tensor = new float[_channels * _height * _width];
        // 检测与分类共用同一条 letterbox 预处理（与 ncnn 后端行为一致）。
        using var resized = YoloBackendShared.ResizeLetterbox(image, _width, _height, out var padX, out var padY);
        YoloBackendShared.WriteToTensor(resized, tensor, _width, padX, padY);

        var preMs = preSw.Elapsed.TotalMilliseconds;
        var inferSw = Stopwatch.StartNew();
        var inputTensor = new DenseTensor<float>(tensor, [1, _channels, _height, _width]);
        var outputs = _session.Run(inputTensor);
        var inferMs = inferSw.Elapsed.TotalMilliseconds;

        var output = outputs[0];
        var pooled = ArrayPool<float>.Shared.Rent(Math.Max(output.Data.Length, 1));
        output.Data.AsSpan(0, output.Data.Length).CopyTo(pooled);
        return (new PooledOutput(pooled, output.Data.Length, output.Shape), preMs, inferMs);
    }

    /// <summary>解析检测输出布局（OpenVINO 输出 shape 直接来自模型元数据）。</summary>
    private (int AnchorCount, int AttributeCount) ResolveLayout(PooledOutput output)
    {
        int? w = output.Shape.Length == 3 ? output.Shape[2] : null;
        int? h = output.Shape.Length >= 2 ? output.Shape[1] : null;
        return YoloBackendShared.ResolveOutputLayout(w, h, null, output.Length, _names.Length);
    }

    /// <summary>拷出到池化缓冲的输出张量；缓冲由调用方在 finally 中归还（DecodeAndNmsCore 之外不持有引用）。</summary>
    private readonly struct PooledOutput(float[] data, int length, int[] shape)
    {
        public float[] Data { get; } = data;
        public int Length { get; } = length;
        public int[] Shape { get; } = shape;
    }

    public void Dispose()
    {
        lock (_inferLock)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }
}
