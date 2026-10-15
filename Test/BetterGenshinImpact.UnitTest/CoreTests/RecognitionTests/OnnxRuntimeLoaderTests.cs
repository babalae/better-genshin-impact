using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.ML.OnnxRuntime;

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
    [InlineData(InferenceDeviceType.Cpu)]
    [InlineData(InferenceDeviceType.DirectML)]
    public void BuiltInRuntime_DoesNotRequirePluginDirectory(InferenceDeviceType device)
    {
        var loader = new OnnxRuntimeLoader(new HardwareAccelerationConfig { InferenceDevice = device }, _directory);

        Assert.Equal(device, loader.InferenceDevice);
        Assert.Equal("内置 DirectML / CPU", loader.StatusText);
        Assert.False(Directory.Exists(loader.PluginsDirectory));
    }

    [Theory]
    [InlineData(InferenceDeviceType.OpenVINO, "OpenVINO")]
    [InlineData(InferenceDeviceType.CUDA, "CUDA")]
    [InlineData(InferenceDeviceType.TensorRT, "TensorRT")]
    public void MissingRuntime_FallsBackToCpuWithoutChangingSavedSettings(InferenceDeviceType device, string directoryName)
    {
        var config = new HardwareAccelerationConfig { InferenceDevice = device, DirectML = new() { DeviceId = "保存的设备" } };

        var loader = new OnnxRuntimeLoader(config, _directory);

        Assert.Equal(InferenceDeviceType.Cpu, loader.InferenceDevice);
        Assert.Contains(directoryName, loader.StatusText);
        Assert.Contains("缺少 onnxruntime.dll", loader.StatusText);
        Assert.Equal(device, config.InferenceDevice);
        Assert.Equal("保存的设备", config.DirectML.DeviceId);
    }

    [Theory]
    [InlineData(InferenceDeviceType.DirectML, "DirectML")]
    [InlineData(InferenceDeviceType.OpenVINO, "OpenVINO")]
    [InlineData(InferenceDeviceType.CUDA, "CUDA")]
    [InlineData(InferenceDeviceType.TensorRT, "TensorRT")]
    public void FixedDirectoryWithoutManifest_AttemptsLoadAndRestoresPathOnFailure(InferenceDeviceType device,
        string directoryName)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "Plugins", "OnnxRuntime", directoryName)).FullName;
        // 文件内容并非原生库：完整文件清单存在时，必须尝试加载 DLL，而不是依赖描述文件才能发现它。
        foreach (var file in OnnxRuntimePackageCatalog.ProviderFiles(device))
            File.WriteAllText(Path.Combine(directory, file), "仅用于失败加载测试");
        var previousPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);

        var loader = new OnnxRuntimeLoader(new HardwareAccelerationConfig { InferenceDevice = device }, _directory);

        Assert.Equal(InferenceDeviceType.Cpu, loader.InferenceDevice);
        Assert.Contains("无法加载", loader.StatusText);
        Assert.Contains(directoryName, loader.StatusText);
        Assert.Equal(previousPath, Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process));
    }

    [Fact]
    public void RequiredVersion_ComesFromManagedOnnxRuntimeAssembly()
    {
        var managedVersion = typeof(InferenceSession).Assembly.GetName().Version!;

        Assert.Equal(managedVersion.ToString(3), OnnxRuntimeLoader.RequiredVersion);
    }

    [Fact]
    public void VersionMismatch_DeletesFixedPluginAndFallsBackToCpu()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "Plugins", "OnnxRuntime", "CUDA")).FullName;
        foreach (var file in OnnxRuntimePackageCatalog.ProviderFiles(InferenceDeviceType.CUDA))
            File.WriteAllText(Path.Combine(directory, file), "仅用于版本不匹配测试");
        var freed = false;

        var loader = new OnnxRuntimeLoader(
            new HardwareAccelerationConfig { InferenceDevice = InferenceDeviceType.CUDA }, _directory,
            _ => new IntPtr(1), _ => "0.0.0", _ => freed = true);

        Assert.True(freed);
        Assert.Equal(InferenceDeviceType.Cpu, loader.InferenceDevice);
        Assert.False(Directory.Exists(directory));
        Assert.Contains("已自动删除", loader.FailureReason);
        Assert.Contains(OnnxRuntimeLoader.RequiredVersion, loader.FailureReason);
    }

    [Fact]
    public void TensorRtWithoutCudaBridge_FallsBackBeforeNativeLoadAndReportsMissingFile()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "Plugins", "OnnxRuntime", "TensorRT")).FullName;
        foreach (var file in OnnxRuntimePackageCatalog.ProviderFiles(InferenceDeviceType.TensorRT)
                     .Where(file => !file.Equals("onnxruntime_providers_cuda.dll", StringComparison.OrdinalIgnoreCase)))
            File.WriteAllText(Path.Combine(directory, file), "仅用于文件完整性测试");
        foreach (var file in OnnxRuntimePackageCatalog.DependencyFiles(InferenceDeviceType.TensorRT))
            File.WriteAllText(Path.Combine(directory, file), "仅用于文件完整性测试");

        var loader = new OnnxRuntimeLoader(
            new HardwareAccelerationConfig { InferenceDevice = InferenceDeviceType.TensorRT }, _directory);

        Assert.Equal(InferenceDeviceType.Cpu, loader.InferenceDevice);
        Assert.Contains("onnxruntime_providers_cuda.dll", loader.StatusText);
        Assert.Contains("onnxruntime_providers_cuda.dll", loader.FailureReason);
        Assert.Contains("重新下载运行库", loader.FailureReason);
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("NVIDIA")]
    public void UnknownDirectory_IsNotLoaded(string directoryName)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "Plugins", "OnnxRuntime", directoryName)).FullName;
        File.WriteAllText(Path.Combine(directory, "onnxruntime.dll"), "不会被加载");

        var loader = new OnnxRuntimeLoader(new HardwareAccelerationConfig { InferenceDevice = InferenceDeviceType.CUDA }, _directory);

        Assert.Equal(InferenceDeviceType.Cpu, loader.InferenceDevice);
        Assert.Contains("缺少 onnxruntime.dll", loader.StatusText);
        Assert.DoesNotContain("无法加载", loader.StatusText);
    }

    [Theory]
    [InlineData(InferenceDeviceType.CUDA, "TensorRT")]
    [InlineData(InferenceDeviceType.TensorRT, "CUDA")]
    public void MissingSelectedRuntime_DoesNotUseOtherEngineDirectory(InferenceDeviceType device, string otherDirectory)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_directory, "Plugins", "OnnxRuntime", otherDirectory)).FullName;
        File.WriteAllText(Path.Combine(directory, "onnxruntime.dll"), "不会被另一个引擎加载");

        var loader = new OnnxRuntimeLoader(new HardwareAccelerationConfig { InferenceDevice = device }, _directory);

        Assert.Equal(InferenceDeviceType.Cpu, loader.InferenceDevice);
        Assert.Contains("缺少 onnxruntime.dll", loader.StatusText);
        Assert.DoesNotContain("无法加载", loader.StatusText);
    }

    public void Dispose()
    {
        // 只清理当前测试生成的临时子目录，不触及程序目录中的实际运行库。
        var path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.GetFullPath(_temporaryRoot) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("测试临时目录不在预期范围内。");
        Directory.Delete(path, true);
    }
}

// 失败加载测试会短暂修改进程 PATH，与其他使用原生库的测试隔离。
[CollectionDefinition("ONNX Runtime 运行库环境", DisableParallelization = true)]
public sealed class OnnxRuntimeLoaderTestCollection;
