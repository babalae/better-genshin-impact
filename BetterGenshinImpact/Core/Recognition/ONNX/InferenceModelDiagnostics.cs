using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Compunet.YoloSharp;
using OpenCvSharp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public sealed record InferenceBenchmarkProgress(string Scene, int Completed, int Total);
public sealed record InferenceBenchmarkResult(string Scene, string Execution, IReadOnlyList<double> Milliseconds)
{
    public double Average => Milliseconds.Average();
    public double Minimum => Milliseconds.Min();
    public double Maximum => Milliseconds.Max();
    public string SamplesText => "每次耗时（ms）：" + string.Join(" / ", Milliseconds.Select(t => t.ToString("F1")));
}

public interface IInferenceModelDiagnostics
{
    bool HasDomainTreeImage { get; }
    Task ValidateTreeAsync(BgiOnnxFactory factory, CancellationToken token);
    Task ValidateOcrAsync(BgiOnnxFactory factory, CancellationToken token);
    Task PreheatOcrAsync();
    Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxFactory factory,
        IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token);
}

public sealed class InferenceModelDiagnostics(OcrFactory ocr, IInferenceBenchmarkLeaseProvider leases) : IInferenceModelDiagnostics, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _warmupLock = new();
    private Task? _warmupTask;
    public bool HasDomainTreeImage => InferenceTestImages.HasDomainTreeImage;

    public Task PreheatOcrAsync()
    {
        lock (_warmupLock)
            return _warmupTask ??= Task.Run(async () =>
            {
                await _gate.WaitAsync(_lifetime.Token);
                try
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    var service = ocr.PaddleOcr;
                    using var image = new Mat(ocr.ActiveModelType.PreHeatImagePath);
                    if (image.Empty()) throw new InvalidDataException("OCR 预热图片无法读取。");
                    Debug.WriteLine("PaddleOcr预热结果:" + service.Ocr(image));
                }
                finally { _gate.Release(); }
            }, _lifetime.Token);
    }

    public async Task ValidateTreeAsync(BgiOnnxFactory factory, CancellationToken token)
    {
        if (!HasDomainTreeImage) throw new FileNotFoundException("古树测试图片待提供。", InferenceTestImages.DomainTreePath);
        await ValidateWithProfileAsync(factory, "古树检测", (directory, validationToken) =>
        {
            using var image = Image.Load<Rgb24>(InferenceTestImages.DomainTreePath);
            using var predictor = factory.CreateYoloPredictor(BgiOnnxModel.BgiTree, Path.Combine(directory, "tree"));
            _ = predictor.Predictor.Detect(image);
        }, token);
    }

    public Task ValidateOcrAsync(BgiOnnxFactory factory, CancellationToken token)
        => ValidateWithProfileAsync(factory, "OCR", (directory, validationToken) =>
        {
            var model = ocr.ActiveModelType;
            using var image = new Mat(model.PreHeatImagePath);
            if (image.Empty()) throw new InvalidDataException("OCR 验证图片无法读取。");
            using var service = new PaddleOcrService(factory.ForOcrValidation(directory), model);
            validationToken.ThrowIfCancellationRequested();
            _ = service.Ocr(image);
        }, token);

    private async Task ValidateWithProfileAsync(BgiOnnxFactory factory, string scene, Action<string, CancellationToken> inference, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                var profileDirectory = Path.Combine(Path.GetTempPath(), "bettergi-inference-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(profileDirectory);
                try
                {
                    inference(profileDirectory, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    if (factory.Snapshot.Engine == InferenceDeviceType.Cpu) return;
                    var expected = factory.Snapshot.Engine switch
                    {
                        InferenceDeviceType.DirectML => "DmlExecutionProvider",
                        InferenceDeviceType.OpenVINO => "OpenVINOExecutionProvider",
                        InferenceDeviceType.CUDA => "CUDAExecutionProvider",
                        InferenceDeviceType.TensorRT => "TensorrtExecutionProvider",
                        _ => throw new InvalidOperationException("未知执行提供程序")
                    };
                    if (!Directory.EnumerateFiles(profileDirectory, "*.json").Any(path => ProfileUsesProvider(path, expected)))
                        throw new InvalidOperationException($"{scene} 未使用 {expected} 执行，不能将 CPU 兜底算作引擎验证通过。");
                }
                finally
                {
                    // 只清理本次诊断创建的临时目录，会话已释放，原生运行库由进程持有。
                    if (Directory.Exists(profileDirectory)) Directory.Delete(profileDirectory, true);
                }
            }, linked.Token);
        }
        finally { _gate.Release(); }
    }

    public static bool ProfileUsesProvider(string path, string provider)
    {
        var profile = JArray.Parse(File.ReadAllText(path));
        return profile.OfType<JObject>().Any(item => item["args"] is JObject args
            && string.Equals(args.Value<string>("provider"), provider, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxFactory factory,
        IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        if (scene == InferenceBenchmarkScene.DomainTree && !HasDomainTreeImage) throw new FileNotFoundException("古树测试图片待提供。");
        using var lease = await Task.Run(leases.Acquire, linked.Token);
        if (scene == InferenceBenchmarkScene.Ocr) await PreheatOcrAsync();
        await _gate.WaitAsync(linked.Token);
        try
        {
            // 在主程序后台测量，结束单次原生调用之后才能响应取消并释放会话。
            return await Task.Run(() =>
            {
                if (scene == InferenceBenchmarkScene.Ocr)
                {
                    var service = ocr.PaddleOcr;
                    using var image = new Mat(ocr.ActiveModelType.PreHeatImagePath);
                    if (image.Empty()) throw new InvalidDataException("OCR 测试图片无法读取。");
                    var type = ocr.ActiveModelType;
                    return Measure("OCR", $"{factory.Snapshot.OcrDescription} · {type.DetectionModel.Name}/{type.RecognitionModel.Name}",
                        () => _ = service.Ocr(image), progress, linked.Token);
                }
                using var treeImage = Image.Load<Rgb24>(InferenceTestImages.DomainTreePath);
                using var predictor = factory.CreateYoloPredictor(BgiOnnxModel.BgiTree);
                return Measure("古树检测", factory.Snapshot.Description,
                    () => _ = predictor.Predictor.Detect(treeImage), progress, linked.Token);
            }, linked.Token);
        }
        finally { _gate.Release(); }
    }

    public static InferenceBenchmarkResult Measure(string scene, string execution, Action inference,
        IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        progress?.Report(new(scene + "预热", 0, 10));
        inference();
        var times = new double[10];
        for (var i = 0; i < times.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var start = Stopwatch.GetTimestamp();
            inference();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            progress?.Report(new(scene, i + 1, times.Length));
        }
        token.ThrowIfCancellationRequested();
        return new(scene, execution, times);
    }

    public async Task StopAsync()
    {
        _lifetime.Cancel();
        Task? warmup;
        lock (_warmupLock) warmup = _warmupTask;
        if (warmup != null)
        {
            try { await warmup; }
            catch (OperationCanceledException) { }
            catch (Exception) { /* 预热错误已经由调用方报告；退出仍须等待资源释放。 */ }
        }
        await _gate.WaitAsync();
        _gate.Release();
    }

    public void Dispose() { _lifetime.Dispose(); _gate.Dispose(); }
}
