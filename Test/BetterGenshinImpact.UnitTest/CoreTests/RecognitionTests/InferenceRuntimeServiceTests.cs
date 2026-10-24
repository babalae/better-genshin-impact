using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class InferenceRuntimeServiceTests
{
    [Fact]
    public async Task DeferredValidation_DoesNotGateSnapshotAndRunsOnceWhenRequested()
    {
        var config = Configuration();
        InferenceRuntimeService.PrepareStartup(config);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic = new Diagnostics { Validation = (_, _) => completion.Task };
        using var service = Create(config, diagnostic);

        await service.InitializeAsync();
        Assert.True(service.IsReady);
        Assert.False(service.IsCurrentValidated);
        Assert.Equal(0, diagnostic.Calls);

        var validation = service.ValidatePendingAsync();
        await diagnostic.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(validation, service.ValidatePendingAsync());
        Assert.Equal(InferenceValidationState.Validating, config.ValidationState);

        completion.SetResult();
        await validation;
        Assert.True(service.IsCurrentValidated);
        Assert.Equal(InferenceDeviceType.CUDA, service.Current.Engine);
        Assert.Equal(InferenceValidationState.None, config.ValidationState);
    }

    [Fact]
    public async Task NativeValidationFailure_UsesCpuInSameProcess()
    {
        var config = Configuration();
        InferenceRuntimeService.PrepareStartup(config);
        var diagnostic = new Diagnostics
        {
            Validation = (_, _) => Task.FromException(new InvalidOperationException("provider 失败"))
        };
        using var service = Create(config, diagnostic);

        await service.InitializeAsync();
        await service.ValidatePendingAsync();

        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
        Assert.Equal(InferenceDeviceType.CUDA, config.LastFailedEngine);
        Assert.Contains("provider 失败", config.LastValidationError);
    }

    [Fact]
    public async Task SecondaryInstance_RuntimeLoadFailureDoesNotChangeSharedConfiguration()
    {
        var config = Configuration();
        var startupStatus = InferenceRuntimeService.PrepareStartup(config, isMainInstance: false);
        var store = new ConfigurationService(config);
        using var service = new InferenceRuntimeService(store, new Runtime(InferenceDeviceType.Cpu), new Devices(),
            new Diagnostics(), NullLogger<BgiOnnxFactory>.Instance, startupStatus);

        await service.InitializeAsync();

        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
        Assert.Equal(InferenceDeviceType.CUDA, config.InferenceDevice);
        Assert.Equal(InferenceValidationState.PendingRestart, config.ValidationState);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ValidationFailure_DoesNotOverwriteSettingsAppliedWhileValidationWasRunning()
    {
        var config = Configuration();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic = new Diagnostics
        {
            Validation = async (_, _) =>
            {
                await completion.Task;
                throw new InvalidOperationException("旧引擎验证失败");
            }
        };
        using var service = Create(config, diagnostic);
        await service.InitializeAsync();

        var validation = service.ValidatePendingAsync();
        await diagnostic.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Apply(InferenceDeviceType.DirectML, new() { DeviceId = "GPU-test" }, false, 0);
        completion.SetResult();
        await validation;

        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
        Assert.Equal(InferenceDeviceType.DirectML, config.InferenceDevice);
        Assert.Equal("GPU-test", config.DirectML.DeviceId);
        Assert.Equal(InferenceValidationState.PendingRestart, config.ValidationState);
    }

    private static HardwareAccelerationConfig Configuration() => new()
    {
        FormatVersion = 1,
        InferenceDevice = InferenceDeviceType.CUDA,
        Cuda = new() { DeviceId = "GPU-test" },
        ValidationState = InferenceValidationState.PendingRestart
    };

    private static InferenceRuntimeService Create(HardwareAccelerationConfig config, Diagnostics diagnostic) =>
        new(new ConfigurationService(config), new Runtime(InferenceDeviceType.CUDA), new Devices(), diagnostic,
            NullLogger<BgiOnnxFactory>.Instance);

    private sealed class Runtime(InferenceDeviceType engine) : IOnnxRuntimeSelection
    {
        public InferenceDeviceType InferenceDevice => engine;
        public string RuntimeDirectory => "test-runtime";
        public string StatusText => "test";
        public string FailureReason => "";
    }

    private sealed class Devices : IInferenceDeviceDiscovery
    {
        public IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string directory) =>
            engine == InferenceDeviceType.Cpu
                ? [ResolvedInferenceRuntime.CpuDevice]
                : [new("GPU-test", "测试显卡", 0, "0")];
    }

    private sealed class Diagnostics : IInferenceModelDiagnostics
    {
        public bool HasDomainTreeImage => true;
        public int Calls { get; private set; }
        public Func<BgiOnnxSessionFactory, CancellationToken, Task> Validation { get; set; } = (_, _) => Task.CompletedTask;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ValidateTreeAsync(BgiOnnxSessionFactory factory, CancellationToken token)
        {
            Calls++;
            Started.TrySetResult();
            await Validation(factory, token);
        }

        public Task ValidateOcrAsync(BgiOnnxSessionFactory factory, CancellationToken token) => Task.CompletedTask;

        public Task PreheatOcrAsync() => Task.CompletedTask;

        public Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxSessionFactory factory,
            IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class ConfigurationService(HardwareAccelerationConfig config) : IConfigService
    {
        private readonly AllConfig _all = new() { HardwareAccelerationConfig = config };
        public int SaveCount { get; private set; }
        public AllConfig Get() => _all;
        public AllConfig Read() => _all;
        public void Save() => SaveCount++;
        public void Flush() { }
        public void Write(AllConfig value) { }
    }
}
