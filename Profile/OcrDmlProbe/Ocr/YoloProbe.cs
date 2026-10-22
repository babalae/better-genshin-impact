using System.Diagnostics;
using Compunet.YoloSharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OcrDmlProbe.Ocr;

/// <summary>
/// BgiWorld（YOLOv8，1280×1280）基线探针。两条计时路径：
/// <list type="bullet">
///   <item><see cref="TimeRawRun"/>：零张量纯 session.Run，口径与 ncnn / 纯 CPU 的"纯推理"一致；</item>
///   <item><see cref="DetectOnce"/>：走 YoloSharp 的 Detect(Mat)，与生产的 BgiYoloPredictor 同一条路径
///         （letterbox 预处理 + decode + NMS + 坐标反变换）。</item>
/// </list>
/// 两者相减即为 YoloSharp 后处理与预处理的开销。
/// </summary>
public sealed class YoloProbe : IDisposable
{
    private readonly InferenceSession _rawSession;
    private readonly YoloPredictor _predictor;
    private readonly SessionOptions _predictorOptions;
    private readonly int _channels;
    private readonly int _inputHeight;
    private readonly int _inputWidth;
    // 复用输入张量：1280×1280×3 float = 19.7MB，每次新建会直接进 LOH，GC 停顿会污染计时
    private readonly DenseTensor<float> _rawInput;
    private bool _disposed;

    public YoloProbe(string modelPath, int dmlDeviceId, int cpuThreads, Action<string> log,
        string providers = "both", bool enableProfiling = false)
    {
        _rawSession = DmlSessionFactory.CreateSession(modelPath, dmlDeviceId, cpuThreads,
            Path.GetFileNameWithoutExtension(modelPath), providers, enableProfiling);

        var dims = _rawSession.InputMetadata[_rawSession.InputNames[0]].Dimensions;
        if (dims.Length != 4 || dims.Any(d => d <= 0))
            throw new InvalidOperationException(
                $"YOLO 模型输入必须是静态 NCHW，实际 [{string.Join(",", dims)}]");
        _channels = dims[1];
        _inputHeight = dims[2];
        _inputWidth = dims[3];
        _rawInput = new DenseTensor<float>(new[] { 1, _channels, _inputHeight, _inputWidth });

        // YoloSharp 不接管 SessionOptions 所有权（生产 BgiYoloPredictor 也在构造后立即释放）。
        _predictorOptions = DmlSessionFactory.CreateSessionOptions(dmlDeviceId, cpuThreads, providers);
        try
        {
            _predictor = new YoloPredictor(modelPath, new YoloPredictorOptions { SessionOptions = _predictorOptions });
        }
        catch
        {
            _predictorOptions.Dispose();
            _rawSession.Dispose();
            throw;
        }

        DmlSessionFactory.Describe(_rawSession, Path.GetFileNameWithoutExtension(modelPath), log);
        log?.Invoke($"[YOLO] 任务 {_predictor.Metadata.Task} | 输入 {_channels}x{_inputHeight}x{_inputWidth} | " +
                    $"类别 {_predictor.Metadata.Names.Length}：" +
                    string.Join(", ", _predictor.Metadata.Names.Select(n => $"{n.Id}:{n.Name}")));
    }

    /// <summary>纯 session.Run：零张量（复用同一块缓冲），不含预处理/后处理与托管分配。</summary>
    public double TimeRawRun()
    {
        var start = Stopwatch.GetTimestamp();
        using var results = _rawSession.Run([
            NamedOnnxValue.CreateFromTensor(_rawSession.InputNames[0], _rawInput)
        ]);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>完整 Detect：YoloSharp 预处理 + 推理 + decode + NMS。</summary>
    public (double Ms, int Boxes) DetectOnce(Image<Rgb24> image)
    {
        var start = Stopwatch.GetTimestamp();
        var result = _predictor.Detect(image);
        var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return (ms, result.Count);
    }

    /// <summary>结束 ORT 内置 profiler，返回 chrome trace JSON 路径（需创建会话时开启 --profile）。</summary>
    public string EndProfiling() => _rawSession.EndProfiling();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _predictor.Dispose();
        _predictorOptions.Dispose();
        _rawSession.Dispose();
    }
}
