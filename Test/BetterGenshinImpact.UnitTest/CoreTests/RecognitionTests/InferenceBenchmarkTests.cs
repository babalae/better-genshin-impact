using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class InferenceBenchmarkTests
{
    [Fact]
    public void CudaVisibility_MapsRuntimeOrdinalsBeforeHardwareFiltering()
    {
        var devices = new InferenceDeviceInfo[] { new("GPU-old", "旧显卡", 0, "0"), new("GPU-new", "新显卡", 1, "1") };
        var visible = InferenceDeviceDiscovery.MapCudaVisibility(devices, "0,1");
        Assert.Equal(1, visible.Single(d => d.Id == "GPU-new").Index);
        var reordered = InferenceDeviceDiscovery.MapCudaVisibility(devices, "1,0");
        Assert.Equal(0, reordered.Single(d => d.Id == "GPU-new").Index);
        Assert.Empty(InferenceDeviceDiscovery.MapCudaVisibility(devices, "-1"));
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
