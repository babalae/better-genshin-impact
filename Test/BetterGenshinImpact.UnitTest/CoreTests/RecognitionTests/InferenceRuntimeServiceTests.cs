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

    [Fact]
    public void PrepareStartup_RecoversLegacySignatureForPreviouslySelectedEngine()
    {
        var config = Configuration();
        config.VerifiedSignature = InferenceRuntimeSnapshot.Create(config,
            new("GPU-test", "测试显卡", 0, "0"), "test-runtime").Signature;
        config.InferenceDevice = InferenceDeviceType.Cpu;
        config.ValidationState = InferenceValidationState.None;

        var reloaded = JsonSerializer.Deserialize<HardwareAccelerationConfig>(JsonSerializer.Serialize(config))!;
        InferenceRuntimeService.PrepareStartup(reloaded);

        Assert.Equal(new[] { InferenceDeviceType.CUDA }, reloaded.ValidatedEngines);
        Assert.Equal(InferenceDeviceType.Cpu, reloaded.InferenceDevice);
    }

    [Fact]
    public void PrepareStartup_DoesNotRecoverLegacySignatureWhenSavedParametersChanged()
    {
        var config = Configuration();
        config.VerifiedSignature = InferenceRuntimeSnapshot.Create(config,
            new("GPU-old", "旧显卡", 0, "0"), "test-runtime").Signature;

        InferenceRuntimeService.PrepareStartup(config);

        Assert.Empty(config.ValidatedEngines);
    }

    [Fact]
    public async Task ValidatedEngines_PersistAcrossSwitchesAndAnotherEngineFailure()
    {
        var config = Configuration();
        using (var cuda = Create(config, new Diagnostics()))
        {
            await cuda.ValidatePendingAsync();
            cuda.Apply(InferenceDeviceType.DirectML, new() { DeviceId = "GPU-test" }, true, 0);
        }
        config = JsonSerializer.Deserialize<HardwareAccelerationConfig>(JsonSerializer.Serialize(config))!;
        using (var dml = Create(config, new Diagnostics(), InferenceDeviceType.DirectML))
        {
            await dml.ValidatePendingAsync();
            dml.Apply(InferenceDeviceType.OpenVINO, new() { DeviceId = "GPU-test" }, true, 0);
        }
        config = JsonSerializer.Deserialize<HardwareAccelerationConfig>(JsonSerializer.Serialize(config))!;
        using var failed = Create(config, new Diagnostics
        {
            Validation = (_, _) => Task.FromException(new InvalidOperationException("OpenVINO 验证失败"))
        }, InferenceDeviceType.OpenVINO);
        await failed.ValidatePendingAsync();

        var restarted = JsonSerializer.Deserialize<HardwareAccelerationConfig>(JsonSerializer.Serialize(config))!;
        Assert.Equal(new[] { InferenceDeviceType.CUDA, InferenceDeviceType.DirectML }, restarted.ValidatedEngines);
        Assert.Equal(InferenceDeviceType.Cpu, restarted.InferenceDevice);
        Assert.Equal(InferenceDeviceType.OpenVINO, restarted.LastFailedEngine);
    }

    [Fact]
    public void RetiredTensorRtSelection_FallsBackToCpuAndKeepsOtherEngineSettings()
    {
        var config = Configuration(InferenceDeviceType.TensorRT);
        config.TensorRT = new() { DeviceId = "GPU-test" };
        config.Cuda = new() { DeviceId = "GPU-kept" };

        var status = InferenceRuntimeService.PrepareStartup(config);

        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
        Assert.Equal("CPU", config.Cpu.DeviceId);
        Assert.Equal("GPU-kept", config.Cuda.DeviceId);
        Assert.Equal(InferenceValidationState.None, config.ValidationState);
        Assert.Null(config.LastFailedEngine);
        Assert.Contains("TensorRT", status.FallbackMessage);
    }

    [Fact]
    public void Apply_RetiredTensorRtEngine_IsRejected()
    {
        var config = Configuration(InferenceDeviceType.Cpu);
        var service = Create(config, new Diagnostics(), InferenceDeviceType.Cpu);

        Assert.Throws<ArgumentException>(() =>
            service.Apply(InferenceDeviceType.TensorRT, new() { DeviceId = "GPU-test" }, true, 0));
        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
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
    public async Task DeferredValidation_DoesNotGateSnapshotAndRunsOnceWhenRequested()
    {
        var config = Configuration();
        InferenceRuntimeService.PrepareStartup(config);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic = new Diagnostics { Validation = (_, _) => completion.Task };
        var service = Create(config, diagnostic);
        await service.InitializeAsync();
        Assert.True(service.IsReady);
        Assert.False(service.IsCurrentValidated);
        Assert.Equal(0, diagnostic.Calls);
        Assert.Equal(InferenceValidationState.PendingRestart, config.ValidationState);
        var validation = service.ValidatePendingAsync();
        await diagnostic.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.IsReady);
        Assert.Same(validation, service.ValidatePendingAsync());
        Assert.Equal(InferenceValidationState.Validating, config.ValidationState);
        completion.SetResult();
        await validation;
        Assert.True(service.IsCurrentValidated);
        Assert.Equal(InferenceDeviceType.CUDA, service.Current.Engine);
        Assert.Equal(InferenceValidationState.None, config.ValidationState);
        Assert.Equal(service.Current.Signature, config.VerifiedSignature);
    }

    [Theory]
    [InlineData(InferenceValidationState.PendingRestart)]
    [InlineData(InferenceValidationState.Validating)]
    public async Task SecondaryInstance_NeverValidatesOrChangesMainValidationMarker(InferenceValidationState state)
    {
        var config = Configuration();
        config.ValidationState = state;
        var status = InferenceRuntimeService.PrepareStartup(config, isMainInstance: false);
        var diagnostic = new Diagnostics { Validation = (_, _) => throw new InvalidOperationException("子实例不应验证") };
        using var service = Create(config, diagnostic, startupStatus: status);
        await service.InitializeAsync();
        await service.ValidatePendingAsync();
        Assert.Equal(InferenceDeviceType.CUDA, service.Current.Engine);
        Assert.Equal(state, config.ValidationState);
        Assert.Null(config.LastFailedEngine);
        Assert.Empty(service.StartupMessage);
        Assert.Equal(0, diagnostic.Calls);
        Assert.Equal(0, diagnostic.OcrCalls);
    }

    [Fact]
    public async Task SecondaryInstance_RuntimeLoadFailure_UsesLocalCpuWithoutChangingSharedConfiguration()
    {
        var config = Configuration();
        config.ValidationState = InferenceValidationState.PendingRestart;
        var status = InferenceRuntimeService.PrepareStartup(config, isMainInstance: false);
        var store = new ConfigurationService(config);
        using var service = new InferenceRuntimeService(store, new Runtime(InferenceDeviceType.Cpu), new Devices(),
            new Diagnostics(), NullLogger<BgiOnnxFactory>.Instance, status);

        await service.InitializeAsync();

        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
        Assert.Equal(InferenceDeviceType.CUDA, config.InferenceDevice);
        Assert.Equal(InferenceValidationState.PendingRestart, config.ValidationState);
        Assert.Null(config.LastFailedEngine);
        Assert.Empty(config.LastValidationError);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task LateFailure_UpdatesFutureFactoriesAndPreservesCapturedSnapshot()
    {
        var config = Configuration();
        InferenceRuntimeService.PrepareStartup(config);
        var diagnostic = new Diagnostics { Validation = (_, _) => Task.FromException(new InvalidOperationException("provider 失败")) };
        using var service = Create(config, diagnostic);
        var liveFactory = new BgiOnnxFactory(NullLogger<BgiOnnxFactory>.Instance, service);
        Assert.False(service.IsReady); // 构造业务工厂不要求验证或设备初始化已经完成。
        await service.InitializeAsync();
        var captured = liveFactory.CaptureSnapshot();
        Assert.Equal(InferenceDeviceType.CUDA, captured.Snapshot.Engine);
        await service.ValidatePendingAsync();
        Assert.Equal(InferenceDeviceType.Cpu, liveFactory.Snapshot.Engine);
        Assert.Equal(new[] { ProviderType.Cpu }, liveFactory.ProviderTypes);
        Assert.Equal(InferenceDeviceType.CUDA, captured.Snapshot.Engine);
        Assert.Contains(ProviderType.Cuda, captured.ProviderTypes);
    }

    [Fact]
    public async Task LateFailure_DoesNotOverwriteSettingsAppliedWhileValidationWasRunning()
    {
        var config = Configuration();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic = new Diagnostics { Validation = async (_, _) =>
        {
            await completion.Task;
            throw new InvalidOperationException("旧引擎验证失败");
        } };
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
        Assert.False(config.CpuOcr);
        Assert.Equal(InferenceValidationState.PendingRestart, config.ValidationState);
        Assert.True(service.HasPendingRestart);
    }

    [Fact]
    public async Task LateSuccess_DoesNotClearNewerPendingSettings()
    {
        var config = Configuration();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic = new Diagnostics { Validation = (_, _) => completion.Task };
        var store = new ConfigurationService(config);
        using var service = new InferenceRuntimeService(store, new Runtime(InferenceDeviceType.CUDA), new Devices(),
            diagnostic, NullLogger<BgiOnnxFactory>.Instance);
        await service.InitializeAsync();
        var validation = service.ValidatePendingAsync();
        await diagnostic.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Apply(InferenceDeviceType.OpenVINO, new() { DeviceId = "GPU-test" }, true, 0);
        completion.SetResult();
        await validation;
        Assert.Equal(InferenceDeviceType.CUDA, service.Current.Engine);
        Assert.True(service.IsCurrentValidated);
        Assert.Equal(InferenceDeviceType.OpenVINO, config.InferenceDevice);
        Assert.Equal(InferenceValidationState.PendingRestart, config.ValidationState);
        Assert.True(service.HasPendingRestart);
        var saved = JsonSerializer.Deserialize<HardwareAccelerationConfig>(store.SavedHardwareJson)!;
        Assert.Equal(new[] { InferenceDeviceType.CUDA }, saved.ValidatedEngines);
        Assert.Equal(InferenceDeviceType.OpenVINO, saved.InferenceDevice);
    }

    [Fact]
    public async Task Shutdown_WaitsForNativeValidationAndKeepsPendingSettingsWithoutFalseFailure()
    {
        var config = Configuration();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic = new Diagnostics { Validation = (_, _) => completion.Task };
        using var service = Create(config, diagnostic);
        await service.InitializeAsync();
        var validation = service.ValidatePendingAsync();
        await diagnostic.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopping = service.StopAsync();
        Assert.True(service.IsStopping);
        Assert.False(stopping.IsCompleted);
        completion.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await validation;
        Assert.Equal(InferenceDeviceType.CUDA, config.InferenceDevice);
        Assert.Equal(InferenceValidationState.PendingRestart, config.ValidationState);
        Assert.Null(config.LastFailedEngine);
        Assert.Empty(service.StartupMessage);
    }

    [Fact]
    public async Task NativeValidationFailure_UsesCpuInSameProcess()
    {
        var config = Configuration();
        InferenceRuntimeService.PrepareStartup(config);
        var diagnostic = new Diagnostics { Validation = (_, _) => Task.FromException(new InvalidOperationException("provider 失败")) };
        var service = Create(config, diagnostic);
        await service.InitializeAsync();
        await service.ValidatePendingAsync();
        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
        Assert.Equal(InferenceDeviceType.CUDA, config.LastFailedEngine);
        Assert.Contains("失败环节：模型验证", service.StartupMessage);
        Assert.DoesNotContain("provider 失败", service.StartupMessage);
        Assert.Contains("provider 失败", config.LastValidationError);
    }

    [Fact]
    public async Task MissingProviderEvidence_DoesNotTurnSuccessfulInferenceIntoFailure()
    {
        var config = Configuration();
        var diagnostic = new Diagnostics
        {
            Result = new("古树检测", InferenceValidationOutcome.EvidenceUnavailable,
                "完整模型测试已成功，但没有性能记录。", "没有读取到执行设备记录。")
        };
        using var service = Create(config, diagnostic);

        await service.InitializeAsync();
        await service.ValidatePendingAsync();

        Assert.Equal(InferenceDeviceType.CUDA, service.Current.Engine);
        Assert.True(service.IsCurrentValidated);
        Assert.Equal(InferenceValidationState.None, config.ValidationState);
        Assert.Empty(service.StartupMessage);
        Assert.Null(config.LastFailedEngine);
    }

    [Fact]
    public async Task ExplicitCpuOnlyEvidence_IsReportedAsValidationConclusionWithoutFakeException()
    {
        var config = Configuration();
        var diagnostic = new Diagnostics
        {
            Result = new("古树检测", InferenceValidationOutcome.NotAccelerated,
                "性能记录显示模型实际全部由 CPU 完成。",
                "只记录到 CPUExecutionProvider。这是验证结论，不是运行库异常，因此没有底层异常堆栈。")
        };
        using var service = Create(config, diagnostic);

        await service.InitializeAsync();
        await service.ValidatePendingAsync();

        Assert.Equal(InferenceDeviceType.Cpu, service.Current.Engine);
        Assert.Contains("加速未生效", service.StartupMessage);
        Assert.DoesNotContain("加速验证失败", service.StartupMessage);
        Assert.Contains("没有底层异常堆栈", config.LastValidationError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedDmlValidation_PersistsCpuAndDoesNotRepeatWarningOrValidationOnRestart(bool hasTreeImage)
    {
        var config = Configuration(InferenceDeviceType.DirectML);
        config.DirectML.DeviceId = "GPU-test";
        var startupStatus = InferenceRuntimeService.PrepareStartup(config);
        var store = new ConfigurationService(config);
        var diagnostic = new Diagnostics { HasDomainTreeImage = hasTreeImage,
            Validation = (_, _) => Task.FromException(new InvalidOperationException("DML provider 失败")) };
        var failedStartup = new InferenceRuntimeService(store, new Runtime(InferenceDeviceType.DirectML), new Devices(),
            diagnostic, NullLogger<BgiOnnxFactory>.Instance, startupStatus);
        await failedStartup.InitializeAsync();
        await failedStartup.ValidatePendingAsync();
        Assert.Equal(InferenceDeviceType.Cpu, failedStartup.Current.Engine);
        Assert.Contains("失败环节：模型验证", failedStartup.StartupMessage);
        Assert.DoesNotContain("DML provider 失败", failedStartup.StartupMessage);
        Assert.Contains("DML provider 失败", config.LastValidationError);

        // 重启从已保存的 JSON 重新构造配置，不能只验证同一个内存对象。
        var reloaded = JsonSerializer.Deserialize<HardwareAccelerationConfig>(store.SavedHardwareJson)!;
        Assert.Equal(InferenceDeviceType.Cpu, reloaded.InferenceDevice);
        Assert.Equal(InferenceValidationState.None, reloaded.ValidationState);
        Assert.Equal(InferenceDeviceType.DirectML, reloaded.LastFailedEngine);
        var nextStartupStatus = InferenceRuntimeService.PrepareStartup(reloaded);
        var nextDiagnostic = new Diagnostics { Validation = (_, _) => throw new InvalidOperationException("不应再次验证 DML") };
        var restarted = Create(reloaded, nextDiagnostic, InferenceDeviceType.Cpu, nextStartupStatus);
        await restarted.InitializeAsync();
        await restarted.ValidatePendingAsync();
        Assert.Equal(InferenceDeviceType.Cpu, restarted.Current.Engine);
        Assert.True(restarted.Current.CpuOcr);
        Assert.Empty(restarted.StartupMessage);
        Assert.Equal(0, nextDiagnostic.Calls);
        Assert.Equal(0, nextDiagnostic.OcrCalls);
        Assert.Contains("DML provider 失败", reloaded.LastValidationError);
    }

    [Fact]
    public async Task InterruptedValidation_NotifiesOnlyTheFirstCpuStartup()
    {
        var config = Configuration(InferenceDeviceType.DirectML);
        config.DirectML.DeviceId = "GPU-test";
        config.ValidationState = InferenceValidationState.Validating;
        var startupStatus = InferenceRuntimeService.PrepareStartup(config);
        var diagnostic = new Diagnostics();
        var firstCpuStartup = Create(config, diagnostic, InferenceDeviceType.Cpu, startupStatus);
        await firstCpuStartup.InitializeAsync();
        await firstCpuStartup.ValidatePendingAsync();
        Assert.Contains("未正常完成", firstCpuStartup.StartupMessage);
        Assert.Equal(0, diagnostic.Calls);
        Assert.Equal(0, diagnostic.OcrCalls);

        var reloaded = JsonSerializer.Deserialize<HardwareAccelerationConfig>(JsonSerializer.Serialize(config))!;
        var nextStartupStatus = InferenceRuntimeService.PrepareStartup(reloaded);
        var nextDiagnostic = new Diagnostics();
        var nextCpuStartup = Create(reloaded, nextDiagnostic, InferenceDeviceType.Cpu, nextStartupStatus);
        await nextCpuStartup.InitializeAsync();
        await nextCpuStartup.ValidatePendingAsync();
        Assert.Empty(nextCpuStartup.StartupMessage);
        Assert.Contains("未正常完成", reloaded.LastValidationError);
        Assert.Equal(0, nextDiagnostic.Calls);
        Assert.Equal(0, nextDiagnostic.OcrCalls);
    }

    [Fact]
    public async Task ExplicitlyReapplyingDml_ValidatesAgainAndClearsHistoricalFailureOnSuccess()
    {
        var config = Configuration(InferenceDeviceType.DirectML);
        config.DirectML.DeviceId = "GPU-test";
        config.LastFailedEngine = InferenceDeviceType.DirectML;
        config.LastValidationError = "上一次 DML 失败";
        var startupStatus = InferenceRuntimeService.PrepareStartup(config);
        var diagnostic = new Diagnostics();
        var startup = Create(config, diagnostic, InferenceDeviceType.DirectML, startupStatus);
        await startup.InitializeAsync();
        await startup.ValidatePendingAsync();
        Assert.Equal(InferenceDeviceType.DirectML, startup.Current.Engine);
        Assert.Equal(1, diagnostic.Calls);
        Assert.Empty(startup.StartupMessage);
        Assert.Null(config.LastFailedEngine);
        Assert.Empty(config.LastValidationError);
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
        await service.ValidatePendingAsync();
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
        await service.ValidatePendingAsync();
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
        await service.ValidatePendingAsync();
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
        await service.ValidatePendingAsync();
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
        await service.ValidatePendingAsync();
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
        InferenceDeviceType engine = InferenceDeviceType.CUDA, InferenceStartupStatus? startupStatus = null) => new(new ConfigurationService(config),
        new Runtime(engine), new Devices(), diagnostic, NullLogger<BgiOnnxFactory>.Instance, startupStatus);

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
            engine == InferenceDeviceType.Cpu ? [InferenceRuntimeSnapshot.CpuDefault().Device] : [new("GPU-test", "测试显卡", 0, "0")];
    }
    private sealed class Diagnostics : IInferenceModelDiagnostics
    {
        public bool HasDomainTreeImage { get; set; } = true;
        public int Calls { get; private set; }
        public int OcrCalls { get; private set; }
        public InferenceValidationResult Result { get; set; } = InferenceValidationResult.Passed("测试");
        public Func<BgiOnnxFactory, CancellationToken, Task> Validation { get; set; } = (_, _) => Task.CompletedTask;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<InferenceValidationResult> ValidateTreeAsync(BgiOnnxFactory factory, CancellationToken token)
        {
            Calls++; Started.TrySetResult(); await Validation(factory, token); return Result with { Scene = "古树检测" };
        }
        public async Task<InferenceValidationResult> ValidateOcrAsync(BgiOnnxFactory factory, CancellationToken token)
        {
            OcrCalls++; Started.TrySetResult(); await Validation(factory, token); return Result with { Scene = "OCR" };
        }
        public Task PreheatOcrAsync() => Task.CompletedTask;
        public Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxFactory factory,
            IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ConfigurationService(HardwareAccelerationConfig config) : IConfigService
    {
        private readonly AllConfig _all = new() { HardwareAccelerationConfig = config };
        public string SavedHardwareJson { get; private set; } = "";
        public int SaveCount { get; private set; }
        public AllConfig Get() => _all;
        public AllConfig Read() => _all;
        public void Save()
        {
            SaveCount++;
            SavedHardwareJson = JsonSerializer.Serialize(_all.HardwareAccelerationConfig);
        }
        public void Flush() { }
        public void Write(AllConfig config) { }
    }
}
