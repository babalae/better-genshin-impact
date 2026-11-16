using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Compunet.YoloSharp.Data;
using Compunet.YoloSharp.Metadata;
using SixLabors.ImageSharp;using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的 YOLO 检测器（实验）：替代 YoloSharp 的完整 Detect 路径。
///     预处理/解码/NMS 与 OpenVINO 后端共用 <see cref="ONNX.YoloBackendShared" />（逐条复刻 YoloSharp 6.0.3），
///     本类只负责 ncnn 会话与推理。
///     模型为 pnnx 固定形状转换产物，层名 in0/out0。
///     shim 由装配方注入并持有生命周期，本类只负责会话与推理。
/// </summary>
internal sealed class NcnnYoloPredictor : IYoloPredictorBackend
{
    /// <summary>与 YoloConfiguration.Default 一致（生产调用 Detect 时不传配置）。</summary>
        private readonly NcnnSession _session;
    private readonly NcnnTuningSettings _tuning;
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

    public NcnnYoloPredictor(INcnnShim shim, string paramPath, string modelPath,
        YoloName[] names, int width, int height, int channels = 3,
        float confidence = YoloBackendShared.DefaultConfidence, float iou = YoloBackendShared.DefaultIou,
        NcnnTuningSettings? tuning = null, int gpuIndex = 0, bool isClassifier = false)
    {
        if (names.Length == 0) throw new ArgumentException("类别名不能为空", nameof(names));
        ConfigName = $"YOLO ncnn ({Path.GetFileNameWithoutExtension(paramPath)})";
        _names = names;
        _width = width;
        _height = height;
        _channels = channels;
        _confidence = confidence;
        _iou = iou;
        IsClassifier = isClassifier;
        _tuning = (tuning ?? NcnnTuningSettings.CreateDefault()).NormalizedCopy();
        // 检测单次只跑一路，会话池固定 1 个会话即可。
        _session = new NcnnSession(shim, _tuning.ToNetOptions(paramPath, modelPath, gpuIndex), 1,
            channels * height * width);
        try
        {
            // 预热：首次 extract 会编译 Vulkan 管线，挪到构造时避免计入首次检测
            _ = _session.Infer(NcnnInput.FromFloat("in0", _session.InputBuffer, _width, _height, _channels));
        }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

    /// <summary>检测：letterbox 预处理 + ncnn 推理 + 解码 + NMS，返回与 YoloSharp 相同的结果类型。</summary>
    public YoloResult<Detection> Detect(Image<Rgb24> image)
    {
        lock (_inferLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var sw = Stopwatch.StartNew();
            var (output, outCount, info, preMs, inferMs) = InferPreprocessed(image);
            try
            {
                var (anchorCount, attributeCount) = YoloBackendShared.ResolveOutputLayout(info?.Width, info?.Height, info?.Channels,
                    outCount, _names.Length);
                var rawBoxes = YoloBackendShared.DecodeAndNmsCore(output, anchorCount, attributeCount, _names.Length,
                    _confidence, _iou);
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
                ArrayPool<float>.Shared.Return(output);
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
            var (output, count, info, preMs, inferMs) = InferPreprocessed(image);
            try
            {
                if (count < _names.Length)
                    throw new InvalidOperationException(
                        $"ncnn 分类输出维度异常: {count} < 类别数 {_names.Length}（{info?.Describe() ?? "无张量元信息"}）");
                var classifications = new Classification[_names.Length];
                for (var i = 0; i < _names.Length; i++)
                {
                    classifications[i] = new Classification
                    {
                        Name = _names[i],
                        Confidence = output[i],
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
                ArrayPool<float>.Shared.Return(output);
            }
        }
    }

    /// <summary>
    ///     预处理（内接缩放 + 清零张量写入）与推理；返回池化输出缓冲、真实元素数与张量元信息，
    ///     调用方负责归还缓冲。
    /// </summary>
    private (float[] Buffer, int Count, NcnnTensorInfo? Info, double PreMs, double InferMs) InferPreprocessed(Image<Rgb24> image)
    {
        var preSw = Stopwatch.StartNew();
        using var resized = YoloBackendShared.ResizeLetterbox(image, _width, _height, out var padX, out var padY);
        byte[] pixels = [];
        if (_tuning.UsePixelInput)
        {
            pixels = BuildRgbCanvas(resized, padX, padY);
        }
        else
        {
            Array.Clear(_session.InputBuffer);
            YoloBackendShared.WriteToTensor(resized, _session.InputBuffer, _width, padX, padY);
        }

        var preMs = preSw.Elapsed.TotalMilliseconds;
        var inferSw = Stopwatch.StartNew();
        var input = pixels.Length > 0
            ? NcnnInput.FromPixels("in0", pixels, _width, _height, NcnnPixelFormat.Rgb888)
            : NcnnInput.FromFloat("in0", _session.InputBuffer, _width, _height, _channels);
        var output = _session.Infer(input);
        var inferMs = inferSw.Elapsed.TotalMilliseconds;

        var pooled = ArrayPool<float>.Shared.Rent(Math.Max(output.Data.Length, 1));
        output.Data.AsSpan(0, output.Data.Length).CopyTo(pooled);
        return (pooled, output.Data.Length, output.Info, preMs, inferMs);
    }

    /// <summary>把 letterbox 后的图像画到模型尺寸的黑底画布，返回 RGB888 像素（供像素直输）。</summary>
    private byte[] BuildRgbCanvas(Image<Rgb24> resized, int padX, int padY)
    {
        using var canvas = new Image<Rgb24>(_width, _height);
        canvas.Mutate(context => context.DrawImage(resized, new Point(padX, padY), 1f));
        var pixels = new byte[_width * _height * 3];
        canvas.CopyPixelDataTo(pixels);
        return pixels;
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

