using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using Compunet.YoloSharp;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 使用自动钓鱼模型和固定测试图片验证完整的模型加载、图像预处理与推理链路。
/// </summary>
public sealed class InferenceBenchmarkService(
    ILogger<BgiOnnxFactory> onnxLogger,
    ILogger<InferenceBenchmarkService> logger,
    IOnnxRuntimePluginManager pluginManager,
    IOnnxRuntimePluginRegistry pluginRegistry)
    : IInferenceBenchmarkService
{
    private readonly SemaphoreSlim _benchmarkGate = new(1, 1);

    public async Task<InferenceBenchmarkResult> RunAsync(
        HardwareAccelerationConfig config,
        int iterations,
        IProgress<InferenceBenchmarkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        iterations = Math.Clamp(iterations, 1, 100);
        await _benchmarkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = config.Clone();
            return await Task.Run(() => RunCore(snapshot, iterations, progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _benchmarkGate.Release();
        }
    }

    private InferenceBenchmarkResult RunCore(
        HardwareAccelerationConfig config,
        int iterations,
        IProgress<InferenceBenchmarkProgress>? progress,
        CancellationToken cancellationToken)
    {
        var model = BgiOnnxModel.BgiFish;
        var imagePath = Global.Absolute(@"Assets\Model\Fish\test_fish.jpg");
        if (!File.Exists(model.ModalPath))
        {
            throw new FileNotFoundException("推理测试模型 bgi_fish.onnx 不存在。", model.ModalPath);
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("推理测试图片 test_fish.jpg 不存在。", imagePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new InferenceBenchmarkProgress(0, iterations, "正在加载测试图片和推理会话…"));
        using var image = Image.Load<Rgb24>(imagePath);
        var totalWatch = Stopwatch.StartNew();
        var initializationWatch = Stopwatch.StartNew();
        var factory = new BgiOnnxFactory(onnxLogger, config, pluginManager, pluginRegistry);
        EnsureRequestedProviderIsActive(config.InferenceDevice, factory);
        using var predictor = factory.CreateYoloPredictorForValidation(model);
        _ = predictor.Predictor;
        initializationWatch.Stop();

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new InferenceBenchmarkProgress(0, iterations, "正在预热模型…"));
        var warmupWatch = Stopwatch.StartNew();
        _ = predictor.Predictor.Detect(image).Count();
        warmupWatch.Stop();

        var durations = new double[iterations];
        var detectionCount = 0;
        for (var index = 0; index < iterations; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inferenceWatch = Stopwatch.StartNew();
            detectionCount = predictor.Predictor.Detect(image).Count();
            inferenceWatch.Stop();
            durations[index] = inferenceWatch.Elapsed.TotalMilliseconds;
            progress?.Report(new InferenceBenchmarkProgress(index + 1, iterations,
                $"正在测试：{index + 1} / {iterations}"));
        }

        totalWatch.Stop();
        Array.Sort(durations);
        var average = durations.Average();
        var result = new InferenceBenchmarkResult(
            factory.EffectiveProvider.ToString(),
            factory.EffectiveDevice,
            iterations,
            initializationWatch.Elapsed.TotalMilliseconds,
            warmupWatch.Elapsed.TotalMilliseconds,
            average,
            durations[0],
            durations[^1],
            Percentile(durations, 0.50),
            Percentile(durations, 0.95),
            average <= 0 ? 0 : 1000d / average,
            detectionCount,
            totalWatch.Elapsed);
        logger.LogInformation(
            "[ONNX] 推理测试完成：{Provider} {Device}，{Iterations} 次，平均 {Average:F2} ms，P95 {P95:F2} ms",
            result.Provider, result.Device, result.Iterations, result.AverageMilliseconds,
            result.P95Milliseconds);
        return result;
    }

    private static void EnsureRequestedProviderIsActive(
        InferenceDeviceType requestedProvider, BgiOnnxFactory factory)
    {
        var expectedProvider = requestedProvider switch
        {
            InferenceDeviceType.GpuDirectMl => ProviderType.Dml,
            InferenceDeviceType.Cuda => ProviderType.Cuda,
            InferenceDeviceType.OpenVino => ProviderType.OpenVino,
            _ => ProviderType.Cpu
        };
        if (factory.EffectiveProvider != expectedProvider)
        {
            var diagnostic = string.IsNullOrWhiteSpace(factory.StartupDiagnostic)
                ? "所选 Provider 未能启用。"
                : factory.StartupDiagnostic;
            throw new InvalidOperationException(diagnostic);
        }
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(sortedValues.Length * percentile) - 1,
            0, sortedValues.Length - 1);
        return sortedValues[index];
    }
}
