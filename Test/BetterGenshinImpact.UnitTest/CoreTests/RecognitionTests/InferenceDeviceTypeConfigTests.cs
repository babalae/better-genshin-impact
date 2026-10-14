using System.Text.Json;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class InferenceDeviceTypeConfigTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Theory]
    [InlineData("0", InferenceDeviceType.Cpu)]
    [InlineData("1", InferenceDeviceType.DirectML)]
    [InlineData("3", InferenceDeviceType.OpenVINO)]
    [InlineData("4", InferenceDeviceType.CUDA)]
    [InlineData("5", InferenceDeviceType.TensorRT)]
    [InlineData("\"CPU\"", InferenceDeviceType.Cpu)]
    [InlineData("\"DirectML\"", InferenceDeviceType.DirectML)]
    [InlineData("\"OpenVINO\"", InferenceDeviceType.OpenVINO)]
    [InlineData("\"CUDA\"", InferenceDeviceType.CUDA)]
    [InlineData("\"TensorRT\"", InferenceDeviceType.TensorRT)]
    public void Deserialize_KnownEnum_WithoutNewConfigurationFallsBack(string jsonValue, InferenceDeviceType expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<InferenceDeviceType>(jsonValue, JsonOptions));
        Assert.Equal(InferenceDeviceType.Cpu, ReadConfig(jsonValue).InferenceDevice);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("1.5")]
    [InlineData("\"Gpu\"")]
    [InlineData("\"GpuDirectMl\"")]
    [InlineData("\"Unknown\"")]
    [InlineData("\"Cpu, DirectML\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{\"legacy\": [1, 2]}")]
    [InlineData("[1, {\"legacy\": 2}]")]
    public void Deserialize_LegacyOrInvalidDevice_FallsBackWithoutResettingOtherSettings(string jsonValue)
    {
        var config = ReadConfig(jsonValue);

        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
        Assert.Equal(0, config.Cpu.CpuThreads);
        Assert.True(config.CpuOcr);
        Assert.Equal("", config.Cuda.DeviceId);
        Assert.True(config.IsExpectedConfiguration);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(99)]
    public void Assign_UnknownDevice_FallsBackToCpu(int value)
    {
        var config = new HardwareAccelerationConfig { InferenceDevice = InferenceDeviceType.DirectML };

        config.InferenceDevice = (InferenceDeviceType)value;

        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
    }

    [Theory]
    [InlineData(InferenceDeviceType.Cpu, "0")]
    [InlineData(InferenceDeviceType.DirectML, "1")]
    [InlineData(InferenceDeviceType.OpenVINO, "3")]
    [InlineData(InferenceDeviceType.CUDA, "4")]
    [InlineData(InferenceDeviceType.TensorRT, "5")]
    public void Serialize_Device_PreservesHistoricalNumber(InferenceDeviceType value, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Serialize(value, JsonOptions));
    }

    [Fact]
    public void DeviceList_UsesRequestedOrderWithoutAutomaticGpu()
    {
        // 枚举顺序与编号是历史配置的兼容约定；界面可选范围见 SelectableEngines_ExcludeRetiredTensorRt。
        Assert.Equal(new[] { "Cpu", "DirectML", "OpenVINO", "CUDA", "TensorRT" },
            Enum.GetNames<InferenceDeviceType>());
    }

    [Fact]
    public void SelectableEngines_ExcludeRetiredTensorRt()
    {
        // 设置页与首页只展示 Selectable 中的引擎；TensorRT 因推理速度过慢下线，枚举与运行库代码仍然保留。
        Assert.Equal(
            new[] { InferenceDeviceType.Cpu, InferenceDeviceType.DirectML, InferenceDeviceType.OpenVINO, InferenceDeviceType.CUDA },
            InferenceDeviceTypeExtensions.Selectable);
        Assert.False(InferenceDeviceType.TensorRT.IsSelectable());
        Assert.True(InferenceDeviceType.CUDA.IsSelectable());
    }

    private static HardwareAccelerationConfig ReadConfig(string jsonValue)
    {
        var config = JsonSerializer.Deserialize<HardwareAccelerationConfig>(
            $"{{\"inferenceDevice\":{jsonValue},\"gpuDevice\":7,\"cpuOcr\":false,\"cudaDevice\":8,\"onnxRuntimePlugin\":\"nvidia\"}}",
            JsonOptions)!;
        InferenceRuntimeService.PrepareStartup(config);
        return config;
    }
}
