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

public enum InferenceValidationOutcome { Passed, EvidenceUnavailable, NotAccelerated }
public sealed record InferenceValidationResult(
    string Scene, InferenceValidationOutcome Outcome, string UserReason = "", string TechnicalDetails = "")
{
    public static InferenceValidationResult Passed(string scene) => new(scene, InferenceValidationOutcome.Passed);
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
    Task<InferenceValidationResult> ValidateTreeAsync(BgiOnnxFactory factory, CancellationToken token);
    Task<InferenceValidationResult> ValidateOcrAsync(BgiOnnxFactory factory, CancellationToken token);
    Task PreheatOcrAsync();
    Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxFactory factory,
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

    public async Task<InferenceValidationResult> ValidateTreeAsync(BgiOnnxFactory factory, CancellationToken token)
    {
        if (!HasDomainTreeImage) throw new FileNotFoundException("古树测试图片待提供。", InferenceTestImages.DomainTreePath);
        return await ValidateWithProfileAsync(factory, "古树检测", (directory, validationToken) =>
        {
            using var image = Image.Load<Rgb24>(InferenceTestImages.DomainTreePath);
            using var predictor = factory.CreateYoloPredictor(BgiOnnxModel.BgiTree, Path.Combine(directory, "tree"));
            _ = predictor.Predictor.Detect(image);
            logger.LogInformation("[ONNX]古树检测完整推理已执行");
        }, token);
    }

    public Task<InferenceValidationResult> ValidateOcrAsync(BgiOnnxFactory factory, CancellationToken token)
        => ValidateWithProfileAsync(factory, "OCR", (directory, validationToken) =>
        {
            var model = ocr.ActiveModelType;
            using var image = new Mat(model.PreHeatImagePath);
            if (image.Empty()) throw new InvalidDataException("OCR 验证图片无法读取。");
            using var service = new PaddleOcrService(factory.ForOcrValidation(directory), model);
            validationToken.ThrowIfCancellationRequested();
            _ = service.Ocr(image);
            logger.LogInformation("[ONNX]OCR 验证完整推理已执行");
        }, token);

    private async Task<InferenceValidationResult> ValidateWithProfileAsync(BgiOnnxFactory factory, string scene,
        Action<string, CancellationToken> inference, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            return await Task.Run(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                var profileDirectory = Path.Combine(Path.GetTempPath(), "bettergi-inference-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(profileDirectory);
                try
                {
                    inference(profileDirectory, linked.Token);
                    linked.Token.ThrowIfCancellationRequested();
                    if (factory.Snapshot.Engine == InferenceDeviceType.Cpu) return InferenceValidationResult.Passed(scene);
                    var expected = factory.Snapshot.Engine switch
                    {
                        InferenceDeviceType.DirectML => "DmlExecutionProvider",
                        InferenceDeviceType.OpenVINO => "OpenVINOExecutionProvider",
                        InferenceDeviceType.CUDA => "CUDAExecutionProvider",
                        InferenceDeviceType.TensorRT => "TensorrtExecutionProvider",
                        _ => throw new InvalidOperationException("未知执行提供程序")
                    };
                    var providers = Directory.EnumerateFiles(profileDirectory, "*.json")
                        .SelectMany(ReadProfileProviders)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    return CreateProviderValidationResult(scene, factory.Snapshot, expected, providers);
                }
                catch (OperationCanceledException) { throw; }
                catch (InferenceValidationException) { throw; }
                catch (Exception e)
                {
                    throw CreateRuntimeFailure(scene, factory.Snapshot, e);
                }
                finally
                {
                    // 只清理本次诊断创建的临时目录，会话已释放，原生运行库由进程持有。
                    try
                    {
                        if (Directory.Exists(profileDirectory)) Directory.Delete(profileDirectory, true);
                    }
                    catch (Exception cleanupError)
                    {
                        // 临时诊断文件清理失败不能被误判为推理引擎不可用。
                        Debug.WriteLine("清理推理诊断目录失败：" + cleanupError);
                    }
                }
            }, linked.Token);
        }
        finally { _gate.Release(); }
    }

    public static bool ProfileUsesProvider(string path, string provider)
        => ReadProfileProviders(path).Contains(provider, StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyCollection<string> ReadProfileProviders(string path)
    {
        var profile = JArray.Parse(File.ReadAllText(path));
        return profile.OfType<JObject>()
            .Select(item => item["args"] is JObject args ? args.Value<string>("provider") : null)
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Select(provider => provider!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static InferenceValidationResult CreateProviderValidationResult(string scene,
        InferenceRuntimeSnapshot snapshot, string expectedProvider, IReadOnlyCollection<string> actualProviders)
    {
        if (actualProviders.Contains(expectedProvider, StringComparer.OrdinalIgnoreCase))
            return InferenceValidationResult.Passed(scene);

        if (actualProviders.Count == 0)
            return new(scene, InferenceValidationOutcome.EvidenceUnavailable,
                "完整模型测试已成功，但没有生成可用于判断计算设备的性能记录。",
                $"{scene}推理成功；未读取到任何执行设备记录，不能据此判断 {expectedProvider} 未参与计算。");

        var actual = actualProviders.Count == 0 ? "无执行记录" : string.Join(", ", actualProviders);
        var cpuOnly = actualProviders.Count > 0
                      && actualProviders.All(provider => string.Equals(provider, "CPUExecutionProvider", StringComparison.OrdinalIgnoreCase));
        var userReason = cpuOnly
            ? $"性能记录显示{scene}模型实际全部由 CPU 完成，{snapshot.Engine} 没有在“{snapshot.Device.Name}”上参与计算。"
            : $"性能记录显示{snapshot.Engine} 没有在“{snapshot.Device.Name}”上参与{scene}模型的计算。";
        return new(scene, InferenceValidationOutcome.NotAccelerated, userReason,
            $"{scene}未使用 {expectedProvider} 执行；实际执行记录：{actual}。这是一项验证结论，不是运行库异常，因此没有底层异常堆栈。");
    }

    internal static InferenceValidationException CreateRuntimeFailure(string scene, InferenceRuntimeSnapshot snapshot,
        Exception exception)
    {
        var cause = exception.GetBaseException();
        var message = cause.Message;
        var missingDependencies = OnnxRuntimePackageCatalog.MissingDependencyFiles(snapshot.Engine, snapshot.RuntimeDirectory);
        string reason;
        if (missingDependencies.Length > 0 &&
            (cause is DllNotFoundException || message.Contains("LoadLibrary", StringComparison.OrdinalIgnoreCase)
                                                || message.Contains("找不到指定的模块", StringComparison.OrdinalIgnoreCase)))
            reason = OnnxRuntimePackageCatalog.DescribeMissingDependencies(snapshot.Engine, missingDependencies);
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
            technicalDetails += $"\r\n运行库目录：{snapshot.RuntimeDirectory}\r\n缺少依赖：{string.Join("、", missingDependencies)}";
        return new InferenceValidationException(scene, reason, technicalDetails, exception);
    }

    public async Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxFactory factory,
        IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token)
    {
        factory = factory.CaptureSnapshot();
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
