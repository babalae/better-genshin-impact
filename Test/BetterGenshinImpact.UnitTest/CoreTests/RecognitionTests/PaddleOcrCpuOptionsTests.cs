using System.Text.Json;
using System.Text.Json.Nodes;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class PaddleOcrCpuOptionsTests
{
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(12, 6, 3)]
    [InlineData(16, 8, 4)]
    [InlineData(32, 16, 4)]
    public void GameplayPriority_UsesBalancedFormulaWithoutExceedingAvailableProcessors(int processors, int det, int rec)
    {
        var options = new PaddleOcrCpuOptions { Mode = PaddleOcrCpuMode.GameplayPriority };
        Assert.Equal(det, options.Resolve(true, processors)!.IntraOpThreads);
        Assert.Equal(rec, options.Resolve(false, processors)!.IntraOpThreads);
        Assert.False(options.Resolve(true, processors)!.ParallelExecution);
        Assert.Equal(1, options.Resolve(false, processors)!.InterOpThreads);
    }

    [Fact]
    public void Snapshot_FreezesSettingsAndDefaultModePreservesExistingSignature()
    {
        var config = new HardwareAccelerationConfig { FormatVersion = 1 };
        var baseline = InferenceRuntimeSnapshot.CpuDefault();
        var inherited = InferenceRuntimeSnapshot.Create(config, baseline.Device, "");
        Assert.Equal(baseline.Signature, inherited.Signature);

        config.PaddleOcrCpu.Mode = PaddleOcrCpuMode.Custom;
        config.PaddleOcrCpu.DetectionThreads = 0;
        var frozen = InferenceRuntimeSnapshot.Create(config, baseline.Device, "");
        var signature = frozen.Signature;
        config.PaddleOcrCpu.DetectionThreads = 1;
        Assert.Equal(0, frozen.PaddleOcrCpu.DetectionThreads);
        Assert.Equal(signature, frozen.Signature);
        Assert.NotEqual(signature, InferenceRuntimeSnapshot.Create(config, baseline.Device, "").Signature);
    }

    [Fact]
    public void Configuration_RoundTripsAndMissingOptionsKeepExistingHardwareSettings()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var config = new HardwareAccelerationConfig
        {
            FormatVersion = 1, InferenceDevice = InferenceDeviceType.CUDA,
            Cuda = new() { DeviceId = "saved-gpu" },
            PaddleOcrCpu = new() { Mode = PaddleOcrCpuMode.Custom, DetectionThreads = 1, RecognitionThreads = 0,
                InterOpThreads = 1, ParallelExecution = true, AllowSpinning = false }
        };
        var json = JsonSerializer.Serialize(config, options);
        var parsed = JsonSerializer.Deserialize<HardwareAccelerationConfig>(json, options)!;
        Assert.Equal(config.PaddleOcrCpu.ToOptions(), parsed.PaddleOcrCpu.ToOptions());

        var legacy = JsonNode.Parse(json)!.AsObject();
        legacy.Remove("paddleOcrCpu");
        var oldConfig = JsonSerializer.Deserialize<HardwareAccelerationConfig>(legacy.ToJsonString(), options)!;
        InferenceRuntimeService.PrepareStartup(oldConfig);
        Assert.Equal(InferenceDeviceType.CUDA, oldConfig.InferenceDevice);
        Assert.Equal("saved-gpu", oldConfig.Cuda.DeviceId);
        Assert.Equal(PaddleOcrCpuMode.FollowGlobal, oldConfig.PaddleOcrCpu.Mode);
    }

    [Fact]
    public void InvalidOcrOptions_AreNormalizedWithoutResettingOtherEngineSettings()
    {
        var config = new HardwareAccelerationConfig
        {
            FormatVersion = 1, InferenceDevice = InferenceDeviceType.CUDA, Cuda = new() { DeviceId = "saved-gpu" },
            PaddleOcrCpu = new() { Mode = (PaddleOcrCpuMode)99, DetectionThreads = int.MaxValue,
                RecognitionThreads = -2, InterOpThreads = -1 }
        };
        InferenceRuntimeService.PrepareStartup(config);
        Assert.True(config.PaddleOcrCpu.ToOptions().IsValid);
        Assert.Equal(InferenceDeviceType.CUDA, config.InferenceDevice);
        Assert.Equal("saved-gpu", config.Cuda.DeviceId);
        Assert.Equal(PaddleOcrCpuMode.FollowGlobal, config.PaddleOcrCpu.Mode);
    }

    [Theory]
    [InlineData(false, ExecutionMode.ORT_SEQUENTIAL, 1)]
    [InlineData(true, ExecutionMode.ORT_PARALLEL, 0)]
    public void Factory_CustomOptionsReachNativeSessionOptions(bool parallel, ExecutionMode mode, int interOp)
    {
        var factory = new BgiOnnxFactory(NullLogger<BgiOnnxFactory>.Instance, InferenceRuntimeSnapshot.CpuDefault() with
        {
            PaddleOcrCpu = new() { Mode = PaddleOcrCpuMode.Custom, DetectionThreads = 1,
                RecognitionThreads = 0, InterOpThreads = 0, ParallelExecution = parallel, AllowSpinning = false }
        });
        // 所有已注册 PPOCR 版本及语言变体都应使用同一套 CPU 参数。
        foreach (var model in BgiOnnxModel.GetAll().Where(model => model.Name.StartsWith("PpOcr", StringComparison.Ordinal)))
        {
            using var options = new SessionOptions();
            factory.ConfigurePaddleOcrCpu(options, model, true);
            Assert.Equal(model.Name.StartsWith("PpOcrDet", StringComparison.Ordinal) ? 1 : 0, options.IntraOpNumThreads);
            Assert.Equal(interOp, options.InterOpNumThreads);
            Assert.Equal(mode, options.ExecutionMode);
        }
    }

    [Theory]
    [InlineData(InferenceDeviceType.Cpu)]
    [InlineData(InferenceDeviceType.DirectML)]
    public void Factory_CreatesPaddleSessionsWithCpuParametersIncludingForcedCpu(InferenceDeviceType engine)
    {
        // DirectML 加强制 CPU OCR 时也应能使用并行 CPU 设置，不触发 DirectML 的并行限制。
        var snapshot = InferenceRuntimeSnapshot.CpuDefault() with
        {
            Engine = engine,
            PaddleOcrCpu = new() { Mode = PaddleOcrCpuMode.Custom, DetectionThreads = 1,
                RecognitionThreads = 1, InterOpThreads = 1, ParallelExecution = true, AllowSpinning = false }
        };
        var factory = new BgiOnnxFactory(NullLogger<BgiOnnxFactory>.Instance, snapshot);
        using var det = factory.CreateInferenceSession(BgiOnnxModel.PaddleOcrDetV4, true);
        using var rec = factory.CreateInferenceSession(BgiOnnxModel.PaddleOcrRecV4, true);
        Assert.NotEmpty(det.InputMetadata);
        Assert.NotEmpty(rec.InputMetadata);
    }

    [Fact]
    public void Factory_InheritedGpuAndNonPaddleModelsKeepExistingOptions()
    {
        var baseline = InferenceRuntimeSnapshot.CpuDefault();
        var configured = baseline with { PaddleOcrCpu = new() { Mode = PaddleOcrCpuMode.GameplayPriority } };
        var cases = new[]
        {
            (baseline, BgiOnnxModel.PaddleOcrDetV4, true),
            (configured, BgiOnnxModel.PaddleOcrDetV4, false),
            (configured, BgiOnnxModel.BgiTree, true),
            (configured, BgiOnnxModel.YapModelTraining, true)
        };
        foreach (var (snapshot, model, cpuOnly) in cases)
        {
            var factory = new BgiOnnxFactory(NullLogger<BgiOnnxFactory>.Instance, snapshot);
            using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 0 };
            factory.ConfigurePaddleOcrCpu(options, model, cpuOnly);
            Assert.Equal(1, options.IntraOpNumThreads);
            Assert.Equal(0, options.InterOpNumThreads);
        }
    }

    [Fact]
    public void Factory_RuntimeAutoOverridesGeneralCpuThreadsAndKeepsOrtDefaults()
    {
        var factory = new BgiOnnxFactory(NullLogger<BgiOnnxFactory>.Instance, InferenceRuntimeSnapshot.CpuDefault(6) with
        {
            PaddleOcrCpu = new() { Mode = PaddleOcrCpuMode.RuntimeAuto }
        });
        using var options = new SessionOptions();

        factory.ConfigureCpuSessionOptions(options, BgiOnnxModel.PaddleOcrDetV4, true);

        Assert.Equal(0, options.IntraOpNumThreads);
        Assert.Equal(0, options.InterOpNumThreads);
        Assert.Equal(ExecutionMode.ORT_SEQUENTIAL, options.ExecutionMode);
    }
}
