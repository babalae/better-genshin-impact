using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class InferenceBenchmarkTests
{
    [Fact]
    public void Profiling_WritesFileToConfiguredDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bettergi-profile-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var prefix = Path.Combine(directory, "tree");
            using (var options = new SessionOptions())
            {
                BgiOnnxFactory.ConfigureProfiling(options, prefix);
                using var session = new InferenceSession(BgiOnnxModel.BgiTree.ModalPath, options);
            }

            var profile = Assert.Single(Directory.EnumerateFiles(directory, "tree*.json"));
            Assert.StartsWith(prefix, profile, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TensorRtLoadFailure_ReportsMissingComponentsAndKeepsInnerException()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bettergi-trt-failure-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var file in OnnxRuntimePackageCatalog.DependencyFiles(InferenceDeviceType.TensorRT)
                         .Where(file => !file.StartsWith("cublas", StringComparison.OrdinalIgnoreCase)
                                        && !file.StartsWith("cudnn", StringComparison.OrdinalIgnoreCase)))
                File.WriteAllText(Path.Combine(directory, file), "installed");
            var snapshot = new InferenceRuntimeSnapshot(InferenceDeviceType.TensorRT,
                new("GPU-test", "测试显卡", 0, "0"), true, 0, true, directory);
            var original = new DllNotFoundException("LoadLibrary failed with error 126");

            var result = InferenceModelDiagnostics.CreateRuntimeFailure("古树检测", snapshot, original);

            Assert.Contains("cuBLAS", result.UserReason);
            Assert.Contains("cuDNN", result.UserReason);
            Assert.Contains("补齐依赖", result.UserReason);
            Assert.Contains("cublas64_12.dll", result.Message);
            Assert.Contains("cudnn64_9.dll", result.Message);
            Assert.Same(original, result.InnerException);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ProviderProfile_DoesNotMistakeCpuFallbackForCuda()
    {
        var file = Path.Combine(Path.GetTempPath(), "bettergi-profile-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, """[{"args":{"provider":"CPUExecutionProvider"}}]""");
            Assert.False(InferenceModelDiagnostics.ProfileUsesProvider(file, "CUDAExecutionProvider"));
            File.WriteAllText(file, """[{"args":{"provider":"CPUExecutionProvider"}},{"args":{"provider":"CUDAExecutionProvider"}}]""");
            Assert.True(InferenceModelDiagnostics.ProfileUsesProvider(file, "CUDAExecutionProvider"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ProviderFallback_SeparatesValidationConclusionFromRuntimeException()
    {
        var snapshot = new InferenceRuntimeSnapshot(InferenceDeviceType.OpenVINO,
            new("NPU-test", "NPU · Intel(R) AI Boost", 0, "NPU"), true, 0, true, "runtime");
        var result = InferenceModelDiagnostics.CreateProviderValidationResult("古树检测", snapshot,
            "OpenVINOExecutionProvider", ["CPUExecutionProvider"]);

        Assert.Equal(InferenceValidationOutcome.NotAccelerated, result.Outcome);
        Assert.Contains("NPU · Intel(R) AI Boost", result.UserReason);
        Assert.Contains("实际全部由 CPU 完成", result.UserReason);
        Assert.DoesNotContain("ExecutionProvider", result.UserReason);
        Assert.Contains("OpenVINOExecutionProvider", result.TechnicalDetails);
        Assert.Contains("没有底层异常堆栈", result.TechnicalDetails);
    }

    [Fact]
    public void MissingProviderEvidence_DoesNotClaimThatAccelerationWasUnused()
    {
        var snapshot = new InferenceRuntimeSnapshot(InferenceDeviceType.DirectML,
            new("GPU-test", "NVIDIA GeForce RTX 5070 Ti", 0, "0"), true, 0, false, "runtime");
        var result = InferenceModelDiagnostics.CreateProviderValidationResult("古树检测", snapshot,
            "DmlExecutionProvider", []);

        Assert.Equal(InferenceValidationOutcome.EvidenceUnavailable, result.Outcome);
        Assert.Contains("完整模型测试已成功", result.UserReason);
        Assert.Contains("不能据此判断", result.TechnicalDetails);
    }

    [Fact]
    public void Benchmark_WarmsOnceAndMeasuresTenTimes()
    {
        var calls = 0;
        var result = InferenceModelDiagnostics.Measure("OCR", "CPU", () => calls++, null, CancellationToken.None);
        Assert.Equal(11, calls);
        Assert.Equal(10, result.Milliseconds.Count);
        Assert.InRange(result.Average, result.Minimum, result.Maximum);
    }

    [Fact]
    public void Cancellation_IsCheckedBetweenCalls()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => InferenceModelDiagnostics.Measure("OCR", "CPU", () =>
        {
            if (++calls == 3) cancellation.Cancel();
        }, null, cancellation.Token));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void ActiveTask_PreventsBenchmark()
    {
        using var semaphore = new SemaphoreSlim(0, 1);
        var coordinator = new InferenceBenchmarkCoordinator(semaphore, () => throw new Exception("不应暂停"));
        Assert.Throws<InvalidOperationException>(() => coordinator.Acquire());
    }

    [Fact]
    public void Lease_RestoresTriggersAndSemaphoreOnce()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        var resumed = 0;
        var coordinator = new InferenceBenchmarkCoordinator(semaphore, () => new ActionLease(() => resumed++));
        var lease = coordinator.Acquire();
        Assert.Equal(0, semaphore.CurrentCount);
        lease.Dispose(); lease.Dispose();
        Assert.Equal(1, semaphore.CurrentCount);
        Assert.Equal(1, resumed);
    }

    [Fact]
    public void SuspensionFailure_DoesNotLeakTaskLock()
    {
        using var semaphore = new SemaphoreSlim(1, 1);
        var coordinator = new InferenceBenchmarkCoordinator(semaphore, () => throw new IOException("暂停失败"));
        Assert.Throws<IOException>(() => coordinator.Acquire());
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public void Cache_IsolatesModelDeviceAndEngine()
    {
        var root = Path.Combine(Path.GetTempPath(), "bettergi-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var model = Path.Combine(root, "model.onnx");
            File.WriteAllText(model, "model-v1");
            var snapshot = new InferenceRuntimeSnapshot(InferenceDeviceType.TensorRT, new("GPU-1", "GPU", 0, "0"), true, 0, true, "runtime");
            var first = snapshot.GetCachePathForModel(model, root);
            Assert.Equal(first, (snapshot with { CpuOcr = false, CpuThreads = 2 }).GetCachePathForModel(model, root));
            Assert.NotEqual(first, (snapshot with { Device = new("GPU-2", "GPU", 1, "1") }).GetCachePathForModel(model, root));
            Assert.NotEqual(first, (snapshot with { Engine = InferenceDeviceType.OpenVINO }).GetCachePathForModel(model, root));
            File.WriteAllText(model, "model-v2");
            Assert.NotEqual(first, snapshot.GetCachePathForModel(model, root));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ActionLease(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
