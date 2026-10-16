using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

[Collection("ONNX Runtime 运行库环境")]
public sealed class OnnxRuntimeLoaderTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "BetterGI.OnnxRuntimeTests");
    private readonly string _directory;

    public OnnxRuntimeLoaderTests()
    {
        _directory = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData(InferenceDeviceType.OpenVINO, "OpenVINO")]
    [InlineData(InferenceDeviceType.CUDA, "CUDA")]
    public void MissingSelectedRuntime_FallsBackToCpuWithoutChangingSavedSettings(
        InferenceDeviceType device, string directoryName)
    {
        var config = new HardwareAccelerationConfig
        {
            InferenceDevice = device,
            DirectML = new() { DeviceId = "保存的设备" }
        };

        var loader = new OnnxRuntimeLoader(config, _directory);

        Assert.Equal(InferenceDeviceType.Cpu, loader.InferenceDevice);
        Assert.Contains(directoryName, loader.StatusText);
        Assert.Equal(device, config.InferenceDevice);
        Assert.Equal("保存的设备", config.DirectML.DeviceId);
    }

    [Fact]
    public void VersionMismatch_DeletesPluginAndFallsBackToCpu()
    {
        var directory = Directory.CreateDirectory(
            Path.Combine(_directory, "Plugins", "OnnxRuntime", "CUDA")).FullName;
        foreach (var file in OnnxRuntimePackageCatalog.ProviderFiles(InferenceDeviceType.CUDA))
        {
            File.WriteAllText(Path.Combine(directory, file), "仅用于版本不匹配测试");
        }

        var freed = false;
        var loader = new OnnxRuntimeLoader(
            new HardwareAccelerationConfig { InferenceDevice = InferenceDeviceType.CUDA }, _directory,
            _ => new IntPtr(1), _ => "0.0.0", _ => freed = true);

        Assert.True(freed);
        Assert.Equal(InferenceDeviceType.Cpu, loader.InferenceDevice);
        Assert.False(Directory.Exists(directory));
        Assert.Contains(OnnxRuntimeLoader.RequiredVersion, loader.FailureReason);
    }

    public void Dispose()
    {
        // 只清理当前测试生成的临时子目录，不触及程序目录中的实际运行库。
        var path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.GetFullPath(_temporaryRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("测试临时目录不在预期范围内。");
        }

        Directory.Delete(path, true);
    }
}

// 版本校验测试会短暂操作原生库加载状态，与其他使用原生库的测试隔离。
[CollectionDefinition("ONNX Runtime 运行库环境", DisableParallelization = true)]
public sealed class OnnxRuntimeLoaderTestCollection;
