using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Compunet.YoloSharp;
using Microsoft.Extensions.Logging;
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

/// <summary>将面向用户的失败原因与供日志排查的原始技术信息分开。</summary>
internal sealed class InferenceValidationException(
    string scene, string userReason, string technicalDetails, Exception? innerException = null)
    : InvalidOperationException(technicalDetails, innerException)
{
    public string Scene { get; } = scene;
    public string UserReason { get; } = userReason;
}

public interface IInferenceModelDiagnostics
{
    bool HasDomainTreeImage { get; }
    Task ValidateTreeAsync(BgiOnnxSessionFactory factory, CancellationToken token);
    Task ValidateOcrAsync(BgiOnnxSessionFactory factory, CancellationToken token);
    Task PreheatOcrAsync();
    Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxSessionFactory factory,
        IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token);
}

public sealed class InferenceModelDiagnostics(
    OcrFactory ocr, IInferenceBenchmarkLeaseProvider leases, ILogger<InferenceModelDiagnostics> logger)
    : IInferenceModelDiagnostics, IDisposable
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

    public async Task ValidateTreeAsync(BgiOnnxSessionFactory factory, CancellationToken token)
    {
        if (!HasDomainTreeImage) throw new FileNotFoundException("古树测试图片待提供。", InferenceTestImages.DomainTreePath);
        await ValidateAsync(factory, "古树检测", validationToken =>
        {
            using var image = Image.Load<Rgb24>(InferenceTestImages.DomainTreePath);
            using var predictor = factory.CreateYoloPredictor(BgiOnnxModel.BgiTree);
            _ = predictor.Predictor.Detect(image);
            logger.LogInformation("[ONNX]古树检测完整推理已执行");
        }, token);
    }

    public Task ValidateOcrAsync(BgiOnnxSessionFactory factory, CancellationToken token)
        => ValidateAsync(factory, "OCR", validationToken =>
        {
            var model = ocr.ActiveModelType;
            using var image = new Mat(model.PreHeatImagePath);
            if (image.Empty()) throw new InvalidDataException("OCR 验证图片无法读取。");
            using var service = new PaddleOcrService(factory.ForOcrValidation(), model);
            validationToken.ThrowIfCancellationRequested();
            _ = service.Ocr(image);
            logger.LogInformation("[ONNX]OCR 验证完整推理已执行");
        }, token);

    /// <summary>验证所选 provider 能创建会话并完成一次真实推理；不再解析性能记录判断单个算子的执行位置。</summary>
    private async Task ValidateAsync(BgiOnnxSessionFactory factory, string scene,
        Action<CancellationToken> inference, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                try
                {
                    inference(linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) { throw; }
                catch (InferenceValidationException) { throw; }
                catch (Exception e)
                {
                    throw CreateRuntimeFailure(scene, factory.Runtime, e);
                }
            }, linked.Token);
        }
        finally { _gate.Release(); }
    }

    internal static InferenceValidationException CreateRuntimeFailure(string scene, ResolvedInferenceRuntime runtime,
        Exception exception)
    {
        var cause = exception.GetBaseException();
        var message = cause.Message;
        var missingDependencies = OnnxRuntimePackageCatalog.MissingDependencyFiles(runtime.Engine, runtime.RuntimeDirectory);
        string reason;
        if (missingDependencies.Length > 0 &&
            (cause is DllNotFoundException || message.Contains("LoadLibrary", StringComparison.OrdinalIgnoreCase)
                                                || message.Contains("找不到指定的模块", StringComparison.OrdinalIgnoreCase)))
            reason = OnnxRuntimePackageCatalog.DescribeMissingDependencies(runtime.Engine, missingDependencies);
        else if (cause is DllNotFoundException || message.Contains("LoadLibrary", StringComparison.OrdinalIgnoreCase)
                                                    || message.Contains("找不到指定的模块", StringComparison.OrdinalIgnoreCase))
            reason = "运行库或配套依赖无法加载，请在硬件加速设置中重新检测并补齐依赖。";
        else if (cause is BadImageFormatException)
            reason = "运行库文件与当前系统不兼容，请重新下载该推理引擎。";
        else if (cause is FileNotFoundException fileNotFound)
            reason = $"验证需要的文件“{Path.GetFileName(fileNotFound.FileName) ?? "未知文件"}”不存在。";
        else if (cause is OutOfMemoryException || message.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
            reason = "运行模型时内存或显存不足。";
        else if (message.Contains("device", StringComparison.OrdinalIgnoreCase)
                 && (message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                     || message.Contains("unavailable", StringComparison.OrdinalIgnoreCase)))
            reason = "保存的推理设备当前不可用，请重新选择设备。";
        else
            reason = $"加载或运行{scene}模型时发生错误，详细错误已记录到硬件加速设置和日志中。";

        var technicalDetails = $"{scene}验证异常：{message}";
        if (missingDependencies.Length > 0)
            technicalDetails += $"\r\n运行库目录：{runtime.RuntimeDirectory}\r\n缺少依赖：{string.Join("、", missingDependencies)}";
        return new InferenceValidationException(scene, reason, technicalDetails, exception);
    }

    public async Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxSessionFactory factory,
        IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        if (scene == InferenceBenchmarkScene.DomainTree && !HasDomainTreeImage) throw new FileNotFoundException("古树测试图片待提供。");
        using var lease = await Task.Run(leases.Acquire, linked.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            // 在主程序后台测量，结束单次原生调用之后才能响应取消并释放会话。
            return await Task.Run(() =>
            {
                if (scene == InferenceBenchmarkScene.Ocr)
                {
                    var type = ocr.ActiveModelType;
                    using var service = new PaddleOcrService(factory, type);
                    using var image = new Mat(ocr.ActiveModelType.PreHeatImagePath);
                    if (image.Empty()) throw new InvalidDataException("OCR 测试图片无法读取。");
                    return Measure("OCR", $"{factory.Runtime.OcrDescription} · {type.DetectionModel.Name}/{type.RecognitionModel.Name}",
                        () => _ = service.Ocr(image), progress, linked.Token);
                }
                using var treeImage = Image.Load<Rgb24>(InferenceTestImages.DomainTreePath);
                using var predictor = factory.CreateYoloPredictor(BgiOnnxModel.BgiTree);
                return Measure("古树检测", factory.Runtime.Description,
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
