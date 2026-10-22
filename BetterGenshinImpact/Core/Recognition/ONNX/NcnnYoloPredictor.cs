using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Compunet.YoloSharp.Data;
using Compunet.YoloSharp.Metadata;
using SixLabors.ImageSharp;using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using BetterGenshinImpact.Core.Recognition.OCR.Ncnn;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

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
    private readonly nint _net;
    private readonly YoloName[] _names;
    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly float _confidence;
    private readonly float _iou;
    private readonly float[] _inputBuffer;
    private readonly object _inferLock = new();
    private bool _disposed;

    /// <summary>是否为分类模型（输出 [1,nc] 分数）；否则为检测模型（输出 [1,4+nc,N]）。</summary>
    public bool IsClassifier { get; }

    public string ConfigName { get; }

    public NcnnYoloPredictor(string shimDllPath, string paramPath, string modelPath,
        YoloName[] names, int width, int height, int channels = 3,
        float confidence = DefaultConfidence, float iou = DefaultIou,
        int gpuIndex = 0, int numThreads = 4, bool isClassifier = false)
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
        _shim = new NcnnShim(shimDllPath);
        try
        {
            _net = _shim.Create(paramPath, modelPath, gpuIndex, numThreads);
            if (_net == nint.Zero)
                throw new InvalidOperationException($"ncnn YOLO 会话创建失败: {paramPath}");
            // 预热：首次 extract 会编译 Vulkan 管线，挪到构造时避免计入首次检测
            var rc = _shim.Infer(_net, "in0", "out0", _inputBuffer = new float[channels * height * width],
                width, height, channels, out var warmPtr, out _);
            if (rc != 0)
                throw new InvalidOperationException($"ncnn YOLO 预热推理失败: {rc}");
            _shim.FreeBuffer(warmPtr);
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
            var output = InferPreprocessed(image, out var preMs, out var inferMs, out var outCount);
            try
            {
                var rawBoxes = DecodeAndNms(output, outCount, _names.Length, _confidence, _iou);
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
            var output = InferPreprocessed(image, out var preMs, out var inferMs, out var _);
            try
            {
                if (output.Length < _names.Length)
                    throw new InvalidOperationException(
                        $"ncnn 分类输出维度异常: {output.Length} < 类别数 {_names.Length}");
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

    /// <summary>预处理（内接缩放 + 清零张量写入）与推理；返回池化输出缓冲与真实元素数，调用方负责 Return。</summary>
    private float[] InferPreprocessed(Image<Rgb24> image, out double preMs, out double inferMs, out int outCount)
    {
        var sw = Stopwatch.StartNew();
        using var resized = ResizeLetterbox(image, out var padX, out var padY);
        var input = _inputBuffer;
        Array.Clear(input);
        WriteToTensor(resized, input, padX, padY);
        preMs = sw.Elapsed.TotalMilliseconds;

        var rc = _shim.Infer(_net, "in0", "out0", input, _width, _height, _channels,
            out var outPtr, out outCount);
        if (rc != 0)
            throw new InvalidOperationException($"ncnn YOLO 推理失败: {rc}");
        try
        {
            var output = ArrayPool<float>.Shared.Rent(outCount);
            Marshal.Copy(outPtr, output, 0, outCount);
            inferMs = sw.Elapsed.TotalMilliseconds - preMs;
            return output;
        }
        finally
        {
            _shim.FreeBuffer(outPtr);
        }
    }

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
        var anchorCount = outCount / (4 + classCount);
        if (outCount % (4 + classCount) != 0)
            throw new InvalidOperationException($"YOLO 输出维度异常: {outCount} 不能被 {4 + classCount} 整除");

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
            if (_net != nint.Zero) _shim.Destroy(_net);
            _shim.Dispose();
        }
    }
}

/// <summary>模型输入坐标系下的候选框（解码与 NMS 的中间表示）。</summary>
internal readonly record struct RawBox(int ClassIndex, float Confidence, float X, float Y, float W, float H);
