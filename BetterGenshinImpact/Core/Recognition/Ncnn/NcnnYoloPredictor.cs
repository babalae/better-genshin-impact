using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using Compunet.YoloSharp.Data;
using Compunet.YoloSharp.Metadata;
using SixLabors.ImageSharp;using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的 YOLO 检测器（实验）：替代 YoloSharp 的完整 Detect 路径。
///     预处理/解码/NMS 逐条复刻 YoloSharp 6.0.3（Yolo11RawBoundingBoxParser / NonMaxSuppressionService /
///     ImageAdjustmentService / SessionRunnerService），保证与被替换路线行为一致：
///     - 最近邻缩放到模型输入的内接矩形（ImageSharp ResizeMode.Max），内容区写入清零张量，四周补 0；
///     - RGB/255 归一化，CHW 平面布局；
///     - 输出 [1, 4+nc, N]：cx=T[i], cy=T[N+i], w=T[2N+i], h=T[3N+i]，类别 c 置信度=T[(4+c)N+i]；
///     - 贪心 NMS：按置信度降序，仅同类且 IoU &gt; 阈值时抑制；无最大框数限制；
///     - 坐标按 1/缩放比映射回原图并截断取整（与 YoloSharp 的 (int) 转换一致）。
///     模型为 pnnx 固定形状转换产物，层名 in0/out0。
/// </summary>
public sealed class NcnnYoloPredictor : IDisposable
{
    /// <summary>与 YoloConfiguration.Default 一致（生产调用 Detect 时不传配置）。</summary>
    public const float DefaultConfidence = 0.3f;
    public const float DefaultIou = 0.45f;

    private readonly NcnnShim _shim;
    private readonly NcnnSessionPool _pool;
    private readonly NcnnTuningSettings _tuning;
    private readonly YoloName[] _names;
    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly float _confidence;
    private readonly float _iou;
    private readonly float[] _inputBuffer;
    private readonly object _inferLock = new();
    private string _inputName = "in0";
    private string _outputName = "out0";
    private bool _blobsResolved;
    private bool _disposed;

    /// <summary>是否为分类模型（输出 [1,nc] 分数）；否则为检测模型（输出 [1,4+nc,N]）。</summary>
    public bool IsClassifier { get; }

    public string ConfigName { get; }

    public NcnnYoloPredictor(string shimDllPath, string paramPath, string modelPath,
        YoloName[] names, int width, int height, int channels = 3,
        float confidence = DefaultConfidence, float iou = DefaultIou,
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
        _shim = new NcnnShim(shimDllPath);
        try
        {
            // 检测单次只跑一路，会话池固定 1 个会话即可。
            _pool = new NcnnSessionPool(_shim,
                _tuning.ToNetOptions(paramPath, modelPath, gpuIndex), 1);
            _inputBuffer = new float[channels * height * width];
            // 预热：首次 extract 会编译 Vulkan 管线，挪到构造时避免计入首次检测
            _ = _pool.Use(net => RunInfer(net, _inputBuffer, _width, _height, _channels, Array.Empty<byte>()));
        }
        catch
        {
            _shim.Dispose();
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
                var (anchorCount, attributeCount) = ResolveOutputLayout(info, outCount, _names.Length);
                var rawBoxes = DecodeAndNmsCore(output, anchorCount, attributeCount, _names.Length,
                    _confidence, _iou);
                var (adjPadX, adjPadY, ratio) = ComputeAdjustment(image.Size, _width, _height);
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
                        $"ncnn 分类输出维度异常: {count} < 类别数 {_names.Length}（{DescribeLayout(info)}）");
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
        using var resized = ResizeLetterbox(image, out var padX, out var padY);
        byte[] pixels = [];
        if (_tuning.UsePixelInput)
        {
            pixels = BuildRgbCanvas(resized, padX, padY);
        }
        else
        {
            Array.Clear(_inputBuffer);
            WriteToTensor(resized, _inputBuffer, padX, padY);
        }

        var preMs = preSw.Elapsed.TotalMilliseconds;
        var inferSw = Stopwatch.StartNew();
        var (data, count, info) = _pool.Use(net => RunInfer(net, _inputBuffer, _width, _height, _channels, pixels));
        var inferMs = inferSw.Elapsed.TotalMilliseconds;

        var output = ArrayPool<float>.Shared.Rent(Math.Max(count, 1));
        data.AsSpan(0, count).CopyTo(output);
        return (output, count, info, preMs, inferMs);
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

    /// <summary>单次推理：返回带元信息的紧凑 fp32；有像素缓冲时直接按 RGB888 喂入。</summary>
    private (float[] Data, int Count, NcnnTensorInfo Info) RunInfer(nint net, float[] chw, int width, int height,
        int channels, byte[] pixels)
    {
        if (!_blobsResolved) ResolveBlobs(net);
        var input = pixels.Length > 0
            ? NcnnInput.FromPixels(_inputName, pixels, width, height, NcnnPixelFormat.Rgb888)
            : NcnnInput.FromFloat(_inputName, chw, width, height, channels);
        var outputs = _shim.InferEx(net, [input], [_outputName]);
        if (outputs.Count == 0) throw new InvalidOperationException("ncnn YOLO 未返回输出");
        return (outputs[0].Data, outputs[0].Data.Length, outputs[0].Info);
    }

    private void ResolveBlobs(nint net)
    {
        var inputs = _shim.QueryInputBlobs(net);
        var outputs = _shim.QueryOutputBlobs(net);
        if (inputs.Length > 0 && !string.IsNullOrWhiteSpace(inputs[0])) _inputName = inputs[0];
        if (outputs.Length > 0 && !string.IsNullOrWhiteSpace(outputs[0])) _outputName = outputs[0];
        _blobsResolved = true;
    }

    /// <summary>
    ///     解析检测输出布局：优先用 shim 返回的 w/h/c（[1,4+nc,N] 通常落成 w=N、h=4+nc、c=1），
    ///     无法与类别数对齐时回退为「元素数 / (4+nc)」。
    /// </summary>
    private (int AnchorCount, int AttributeCount) ResolveOutputLayout(NcnnTensorInfo? info, int count, int classCount)
    {
        var expectedAttributes = 4 + classCount;
        if (info != null)
        {
            (int Anchors, int Attributes)[] candidates =
            [
                (info.Width, info.Height * Math.Max(info.Channels, 1)),
                (info.Width, info.Height),
                (info.Width * Math.Max(info.Height, 1), info.Channels)
            ];
            foreach (var (anchors, attributes) in candidates)
            {
                if (anchors > 0 && attributes == expectedAttributes && (long)anchors * attributes == count)
                    return (anchors, attributes);
            }
        }

        if (count > 0 && count % expectedAttributes == 0) return (count / expectedAttributes, expectedAttributes);
        throw new InvalidOperationException(
            $"YOLO 输出维度异常: {count} 不能被 {expectedAttributes} 整除（{DescribeLayout(info)}）");
    }

    private static string DescribeLayout(NcnnTensorInfo? info) => info == null
        ? "无张量元信息"
        : $"dims={info.Dims} {info.ShapeText} elempack={info.ElementPack}";

    /// <summary>与 YoloSharp SessionRunnerService.ResizeImage 一致：Max 内接 + 最近邻。</summary>
    private Image<Rgb24> ResizeLetterbox(Image<Rgb24> image, out int padX, out int padY)
    {
        var resized = image.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(_width, _height),
            Mode = ResizeMode.Max,
            Sampler = KnownResamplers.NearestNeighbor,
        }));
        padX = (_width - resized.Width) / 2;
        padY = (_height - resized.Height) / 2;
        return resized;
    }

    /// <summary>RGB/255 写入 CHW 张量的内容区（四周保持 0，与 YoloSharp 清零后的张量一致）。</summary>
    private void WriteToTensor(Image<Rgb24> image, float[] tensor, int padX, int padY)
    {
        var plane = _width * _height;
        var width = image.Width;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var rowBase = (y + padY) * _width + padX;
                for (var x = 0; x < width; x++)
                {
                    tensor[rowBase + x] = row[x].R / 255f;
                    tensor[plane + rowBase + x] = row[x].G / 255f;
                    tensor[2 * plane + rowBase + x] = row[x].B / 255f;
                }
            }
        });
    }

    /// <summary>解码 [1, 4+nc, N] 输出并做同类贪心 NMS（复刻 Yolo11RawBoundingBoxParser + NonMaxSuppressionService）。</summary>
    internal static List<RawBox> DecodeAndNms(float[] output, int outCount, int classCount,
        float confidenceThreshold, float iouThreshold)
    {
        var attributeCount = 4 + classCount;
        if (outCount <= 0 || outCount % attributeCount != 0)
            throw new InvalidOperationException($"YOLO 输出维度异常: {outCount} 不能被 {attributeCount} 整除");
        return DecodeAndNmsCore(output, outCount / attributeCount, attributeCount, classCount,
            confidenceThreshold, iouThreshold);
    }

    /// <summary>按显式 anchor / 属性数解码（元信息路径），避免用元素总数反推布局。</summary>
    internal static List<RawBox> DecodeAndNmsCore(float[] output, int anchorCount, int attributeCount,
        int classCount, float confidenceThreshold, float iouThreshold)
    {
        if (anchorCount <= 0 || attributeCount < 4 + classCount)
            throw new InvalidOperationException(
                $"YOLO 输出布局异常: anchors={anchorCount} attributes={attributeCount} 类别={classCount}");

        var candidates = new List<RawBox>();
        for (var i = 0; i < anchorCount; i++)
        {
            var cx = output[i];
            var cy = output[anchorCount + i];
            var w = output[2 * anchorCount + i];
            var h = output[3 * anchorCount + i];
            if (w == 0 || h == 0) continue;
            for (var c = 0; c < classCount; c++)
            {
                var confidence = output[(4 + c) * anchorCount + i];
                // 与 YoloSharp 一致：小于等于阈值直接排除
                if (confidence <= confidenceThreshold) continue;
                candidates.Add(new RawBox(c, confidence, cx - w / 2, cy - h / 2, w, h));
            }
        }

        // 按置信度降序，贪心保留：仅同类且 IoU 大于阈值时抑制
        candidates.Sort(static (a, b) => b.Confidence.CompareTo(a.Confidence));
        var kept = new List<RawBox>();
        foreach (var box in candidates)
        {
            var suppressed = false;
            foreach (var other in kept)
            {
                if (other.ClassIndex != box.ClassIndex) continue;
                if (IoU(box, other) > iouThreshold)
                {
                    suppressed = true;
                    break;
                }
            }

            if (!suppressed) kept.Add(box);
        }

        return kept;
    }

    /// <summary>交并比（与 YoloSharp 一致：面积非正时返回 0，交集面积/并集面积）。</summary>
    private static float IoU(RawBox a, RawBox b)
    {
        if (a.W <= 0 || a.H <= 0 || b.W <= 0 || b.H <= 0) return 0;
        var interX = Math.Max(0, Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X));
        var interY = Math.Max(0, Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y));
        var intersection = interX * interY;
        return intersection / (a.W * a.H + b.W * b.H - intersection);
    }

    /// <summary>
    ///     模型坐标 → 原图坐标的映射参数（复刻 ImageAdjustmentService）：
    ///     内接缩放比 reduction = min(modelW/w, modelH/h)，填充 = (model - 原图*reduction)/2，
    ///     回映射比 = max(w/modelW, h/modelH)。
    /// </summary>
    internal static (int PadX, int PadY, float Ratio) ComputeAdjustment(Size imageSize, int modelWidth, int modelHeight)
    {
        var reductionRatio = Math.Min(modelWidth / (float)imageSize.Width, modelHeight / (float)imageSize.Height);
        var padX = (int)((modelWidth - imageSize.Width * reductionRatio) / 2);
        var padY = (int)((modelHeight - imageSize.Height * reductionRatio) / 2);
        var ratio = Math.Max(imageSize.Width / (float)modelWidth, imageSize.Height / (float)modelHeight);
        return (padX, padY, ratio);
    }

    public void Dispose()
    {
        lock (_inferLock)
        {
            if (_disposed) return;
            _disposed = true;
            _pool.Dispose();
            _shim.Dispose();
        }
    }
}

/// <summary>模型输入坐标系下的候选框（解码与 NMS 的中间表示）。</summary>
internal readonly record struct RawBox(int ClassIndex, float Confidence, float X, float Y, float W, float H);
