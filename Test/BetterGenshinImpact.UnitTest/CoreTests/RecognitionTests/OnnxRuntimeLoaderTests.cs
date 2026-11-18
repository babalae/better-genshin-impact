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
    [InlineData(InferenceDeviceType.CUDA, "CUDA")]
    [InlineData(InferenceDeviceType.OpenVINO, "OpenVINO")]
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
    public void OpenVinoPluginDirectory_LoadFailure_FallsBackToCpuReportingOpenVino()
    {
        // 目录映射存在时，加载器才会走到 OpenVINO 目录并尝试加载原生库；
        // 若映射再次丢失，StatusText 会停留在默认值，不含 "OpenVINO"。
        var config = new HardwareAccelerationConfig
        {
            InferenceDevice = InferenceDeviceType.OpenVINO,
            OpenVINO = new() { DeviceId = "GPU.0" }
        };
        var pluginDirectory = OnnxRuntimePackageCatalog.EngineDirectory(
            Path.Combine(_directory, "Plugins", "OnnxRuntime"), InferenceDeviceType.OpenVINO);
        Directory.CreateDirectory(pluginDirectory);
        foreach (var file in OnnxRuntimePackageCatalog.ProviderFiles(InferenceDeviceType.OpenVINO))
            File.WriteAllText(Path.Combine(pluginDirectory, file), string.Empty);

        // 原生库加载失败（返回零句柄），走 catch 分支回退 CPU，不注册 DllImport 解析器。
        var loader = new OnnxRuntimeLoader(config, _directory,
            _ => IntPtr.Zero, _ => OnnxRuntimeLoader.RequiredVersion, _ => { });

        Assert.Equal(InferenceDeviceType.Cpu, loader.InferenceDevice);
        Assert.Contains("OpenVINO", loader.StatusText);
        Assert.Contains("运行库无法加载", loader.FailureReason);
        Assert.Equal(InferenceDeviceType.OpenVINO, config.InferenceDevice);
        Assert.Equal("GPU.0", config.OpenVINO.DeviceId);
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

// 运行库加载器会读取原生库加载状态，与其他使用原生库的测试隔离。
[CollectionDefinition("ONNX Runtime 运行库环境", DisableParallelization = true)]
public sealed class OnnxRuntimeLoaderTestCollection;
