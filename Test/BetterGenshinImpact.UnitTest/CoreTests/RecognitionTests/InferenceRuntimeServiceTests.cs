using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class InferenceRuntimeServiceTests
{
    [Fact]
    public void InvalidHardwareField_DoesNotResetOtherApplicationSettings()
    {
        var config = JsonSerializer.Deserialize<AllConfig>("""
            {"captureMode":"preserved","hardwareAccelerationConfig":{"formatVersion":"bad","inferenceDevice":4}}
            """, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        InferenceRuntimeService.PrepareStartup(config.HardwareAccelerationConfig);
        Assert.Equal("preserved", config.CaptureMode);
        Assert.Equal(InferenceDeviceType.Cpu, config.HardwareAccelerationConfig.InferenceDevice);
    }

    [Fact]
    public void NewConfiguration_RoundTripsWithoutRemovedParameters()
    {
        var configuration = Configuration();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(configuration, options);
        var parsed = JsonSerializer.Deserialize<HardwareAccelerationConfig>(json, options)!;
        Assert.True(parsed.IsExpectedConfiguration);
        Assert.Equal(configuration.InferenceDevice, parsed.InferenceDevice);
        Assert.Equal("GPU-test", parsed.Cuda.DeviceId);
        Assert.DoesNotContain("gpuDevice", json);
        Assert.DoesNotContain("optimizedModel", json);
        Assert.DoesNotContain("additionalPath", json);
    }

    private static HardwareAccelerationConfig Configuration(InferenceDeviceType engine = InferenceDeviceType.CUDA) => new()
    {
        FormatVersion = 1, InferenceDevice = engine,
        Cuda = new() { DeviceId = "GPU-test" },
        ValidationState = InferenceValidationState.PendingRestart
    };

    [Fact]
    public void LegacyConfiguration_IsNotMigrated()
    {
        var config = new HardwareAccelerationConfig { InferenceDevice = InferenceDeviceType.CUDA, CpuOcr = false };
        InferenceRuntimeService.PrepareStartup(config);
        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
        Assert.True(config.CpuOcr);
        Assert.Equal(1, config.FormatVersion);
        Assert.Equal(InferenceValidationState.None, config.ValidationState);
    }

    [Fact]
    public void InterruptedValidation_FallsBackBeforeLoadingNativeLibrary()
    {
        var config = Configuration();
        config.ValidationState = InferenceValidationState.Validating;
        InferenceRuntimeService.PrepareStartup(config);
        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
        Assert.Equal(InferenceDeviceType.CUDA, config.LastFailedEngine);
        Assert.Contains("未正常完成", config.LastValidationError);
    }

    [Fact]
    public async Task PendingValidation_PublishesSnapshotOnlyAfterSuccess()
    {
        var config = Configuration();
        InferenceRuntimeService.PrepareStartup(config);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic = new Diagnostics { Validation = (_, _) => completion.Task };
        var service = Create(config, diagnostic);
        var initialization = service.InitializeAsync();
        await diagnostic.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(service.IsReady);
        Assert.Same(initialization, service.InitializeAsync());
        completion.SetResult();
        await initialization;
        Assert.Equal(InferenceDeviceType.CUDA, service.Current.Engine);
        Assert.Equal(InferenceValidationState.None, config.ValidationState);
        Assert.Equal(service.Current.Signature, config.VerifiedSignature);
    }

    [Fact]
    public async Task NativeValidationFailure_UsesCpuInSameProcess()
    {
        var config = Configuration();
        InferenceRuntimeService.PrepareStartup(config);
        var diagnostic = new Diagnostics { Validation = (_, _) => Task.FromException(new InvalidOperationException("provider 失败")) };
        var service = Create(config, diagnostic);
        await service.InitializeAsync();
        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
        Assert.Equal(InferenceDeviceType.CUDA, config.LastFailedEngine);
        Assert.Contains("provider 失败", service.StartupMessage);
    }

    [Fact]
    public async Task MissingDevice_DoesNotSubstituteAnotherGpu()
    {
        var config = Configuration();
        config.Cuda.DeviceId = "GPU-removed";
        InferenceRuntimeService.PrepareStartup(config);
        var diagnostic = new Diagnostics();
        var service = Create(config, diagnostic);
        await service.InitializeAsync();
        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
        Assert.Equal(0, diagnostic.Calls);
        Assert.Contains("设备不可用", config.LastValidationError);
    }

    [Fact]
    public async Task MissingTreeImage_ValidatesOcrAndKeepsSelectedEngine()
    {
        var config = Configuration();
        InferenceRuntimeService.PrepareStartup(config);
        var diagnostic = new Diagnostics { HasDomainTreeImage = false };
        var service = Create(config, diagnostic);
        await service.InitializeAsync();
        Assert.Equal(InferenceDeviceType.CUDA, service.Current.Engine);
        Assert.Null(config.LastFailedEngine);
        Assert.Equal(0, diagnostic.Calls);
        Assert.Equal(1, diagnostic.OcrCalls);
        Assert.Equal(InferenceValidationState.None, config.ValidationState);
        Assert.Equal(service.Current.Signature, config.VerifiedSignature);
        Assert.True(service.Current.CpuOcr);
    }

    [Fact]
    public async Task MissingTreeImage_OcrValidationFailureStillFallsBack()
    {
        var config = Configuration();
        InferenceRuntimeService.PrepareStartup(config);
        var diagnostic = new Diagnostics { HasDomainTreeImage = false,
            Validation = (_, _) => Task.FromException(new InvalidOperationException("OCR provider 失败")) };
        var service = Create(config, diagnostic);
        await service.InitializeAsync();
        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
        Assert.Equal(InferenceDeviceType.CUDA, config.LastFailedEngine);
        Assert.Contains("OCR provider 失败", config.LastValidationError);
    }

    [Theory]
    [InlineData(InferenceDeviceType.OpenVINO)]
    [InlineData(InferenceDeviceType.CUDA)]
    public async Task Apply_WithoutTreeImageCanSaveAndAwaitRestart(InferenceDeviceType engine)
    {
        var config = Configuration(InferenceDeviceType.Cpu);
        config.ValidationState = InferenceValidationState.None;
        var service = Create(config, new Diagnostics { HasDomainTreeImage = false }, InferenceDeviceType.Cpu);
        await service.InitializeAsync();
        service.Apply(engine, new() { DeviceId = "GPU-test" }, true, 0);
        Assert.Equal(engine, config.InferenceDevice);
        Assert.Equal(InferenceValidationState.PendingRestart, config.ValidationState);
        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
    }

    [Fact]
    public async Task OrdinaryStartup_DoesNotRunTreeWarmup()
    {
        var config = Configuration(InferenceDeviceType.Cpu);
        config.ValidationState = InferenceValidationState.None;
        var diagnostic = new Diagnostics();
        var service = Create(config, diagnostic, InferenceDeviceType.Cpu);
        await service.InitializeAsync();
        Assert.Equal(0, diagnostic.Calls);
    }

    [Fact]
    public async Task ValidatedGpuStartup_DoesNotRunTreeWarmupOrRequireImage()
    {
        var config = Configuration();
        config.ValidationState = InferenceValidationState.None;
        config.VerifiedSignature = InferenceRuntimeSnapshot.Create(config, new("GPU-test", "测试显卡", 0, "0"), "test-runtime").Signature;
        var diagnostic = new Diagnostics { HasDomainTreeImage = false };
        var service = Create(config, diagnostic);
        await service.InitializeAsync();
        Assert.Equal(InferenceDeviceType.CUDA, service.Current.Engine);
        Assert.Equal(0, diagnostic.Calls);
    }

    [Fact]
    public async Task Apply_OnlyChangesSavedConfiguration()
    {
        var config = Configuration(InferenceDeviceType.Cpu);
        config.ValidationState = InferenceValidationState.None;
        var service = Create(config, new Diagnostics(), InferenceDeviceType.Cpu);
        await service.InitializeAsync();
        var current = service.Current;
        service.Apply(InferenceDeviceType.CUDA, new() { DeviceId = "GPU-test" }, false, 0);
        Assert.Same(current, service.Current);
        Assert.True(service.Current.CpuOcr);
        Assert.False(config.CpuOcr);
        Assert.Equal(InferenceValidationState.PendingRestart, config.ValidationState);
        Assert.Equal(InferenceDeviceType.CUDA, config.InferenceDevice);
    }

    private static InferenceRuntimeService Create(HardwareAccelerationConfig config, Diagnostics diagnostic,
        InferenceDeviceType engine = InferenceDeviceType.CUDA) => new(new ConfigurationService(config),
        new Runtime(engine), new Devices(), diagnostic, NullLogger<BgiOnnxFactory>.Instance);

    private sealed class Runtime(InferenceDeviceType engine) : IOnnxRuntimeSelection
    {
        public InferenceDeviceType InferenceDevice => engine;
        public string RuntimeDirectory => "test-runtime";
        public string StatusText => "test";
    }
    private sealed class Devices : IInferenceDeviceDiscovery
    {
        public IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string directory) =>
            engine == InferenceDeviceType.Cpu ? [InferenceRuntimeSnapshot.CpuDefault().Device] : [new("GPU-test", "测试显卡", 0, "0")];
    }
    private sealed class Diagnostics : IInferenceModelDiagnostics
    {
        public bool HasDomainTreeImage { get; set; } = true;
        public int Calls { get; private set; }
        public int OcrCalls { get; private set; }
        public Func<BgiOnnxFactory, CancellationToken, Task> Validation { get; set; } = (_, _) => Task.CompletedTask;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ValidateTreeAsync(BgiOnnxFactory factory, CancellationToken token)
        {
            Calls++; Started.TrySetResult(); return Validation(factory, token);
        }
        public Task ValidateOcrAsync(BgiOnnxFactory factory, CancellationToken token)
        {
            OcrCalls++; Started.TrySetResult(); return Validation(factory, token);
        }
        public Task PreheatOcrAsync() => Task.CompletedTask;
        public Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxFactory factory,
            IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ConfigurationService(HardwareAccelerationConfig config) : IConfigService
    {
        private readonly AllConfig _all = new() { HardwareAccelerationConfig = config };
        public AllConfig Get() => _all;
        public AllConfig Read() => _all;
        public void Save() { }
        public void Flush() { }
        public void Write(AllConfig config) { }
    }
}
