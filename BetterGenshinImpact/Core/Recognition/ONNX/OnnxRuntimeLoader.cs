using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using Microsoft.ML.OnnxRuntime;
using Serilog;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
/// 主程序按推理类型选定原生运行库，在任何 ONNX 原生调用之前创建一次。
/// 运行中的进程不切换、不卸载已选中的运行库。
/// </summary>
public interface IOnnxRuntimeSelection
{
    InferenceDeviceType InferenceDevice { get; }
    string RuntimeDirectory { get; }
    string StatusText { get; }
    string FailureReason { get; }
}

public sealed class OnnxRuntimeLoader : IOnnxRuntimeSelection
{
    // 原生库必须与编译时引用的托管 ONNX Runtime 主次修订版一致，不再另外维护版本号。
    public static string RequiredVersion { get; } = GetRequiredVersion();
    private IntPtr _libraryHandle;
    private readonly Func<string, IntPtr> _loadLibrary;
    private readonly Func<IntPtr, string> _readRuntimeVersion;
    private readonly Action<IntPtr> _freeLibrary;

    public string PluginsDirectory { get; }
    public string RuntimeDirectory { get; private set; }
    public InferenceDeviceType InferenceDevice { get; private set; }
    public string StatusText { get; private set; } = "内置 DirectML / CPU";
    public string FailureReason { get; private set; } = "";

    public OnnxRuntimeLoader(HardwareAccelerationConfig config, string baseDirectory)
        : this(config, baseDirectory,
            path => LoadLibraryEx(path, IntPtr.Zero, 0x00000008), // LOAD_WITH_ALTERED_SEARCH_PATH
            ReadRuntimeVersion, NativeLibrary.Free)
    {
    }

    internal OnnxRuntimeLoader(HardwareAccelerationConfig config, string baseDirectory,
        Func<string, IntPtr> loadLibrary, Func<IntPtr, string> readRuntimeVersion, Action<IntPtr> freeLibrary)
    {
        _loadLibrary = loadLibrary;
        _readRuntimeVersion = readRuntimeVersion;
        _freeLibrary = freeLibrary;
        PluginsDirectory = Path.Combine(Path.GetFullPath(baseDirectory), "Plugins", "OnnxRuntime");
        RuntimeDirectory = Path.GetFullPath(baseDirectory);
        InferenceDevice = config.InferenceDevice;
        var directoryName = InferenceDevice switch
        {
            InferenceDeviceType.DirectML or InferenceDeviceType.OpenVINO or
                InferenceDeviceType.CUDA or InferenceDeviceType.TensorRT => InferenceDevice.ToString(),
            _ => null
        };
        if (directoryName is null)
        {
            InferenceDevice = InferenceDeviceType.Cpu;
            return;
        }

        var directory = OnnxRuntimePackageCatalog.RuntimeDirectory(PluginsDirectory, InferenceDevice);
        var libraryPath = Path.Combine(directory, "onnxruntime.dll");
        // DirectML 已随主程序提供，固定目录中的运行库仅用于可选替换。
        if (InferenceDevice == InferenceDeviceType.DirectML && !File.Exists(libraryPath)) return;

        var previousPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
        string version = "";
        var versionMismatch = false;
        var deleteInvalidPlugin = false;
        try
        {
            var missingRuntimeFiles = OnnxRuntimePackageCatalog.ProviderFiles(InferenceDevice)
                .Where(file => !File.Exists(Path.Combine(directory, file)))
                .ToArray();
            if (missingRuntimeFiles.Length > 0)
            {
                deleteInvalidPlugin = Directory.Exists(directory);
                FailureReason = $"{directoryName} 运行库不完整，缺少 {string.Join("、", missingRuntimeFiles)}。请在硬件加速设置中重新下载运行库。";
                throw new FileNotFoundException($"运行库目录中缺少 {string.Join("、", missingRuntimeFiles)}。",
                    Path.Combine(directory, missingRuntimeFiles[0]));
            }

            // 只加入选中运行库的目录，不扫描加载其他目录中的 DLL。PATH 仅修改当前进程，
            // 也供 ONNX 原生代码随后加载 provider 的间接依赖使用。
            // 不从其他引擎安装目录借用同名依赖。CUDA 和 TensorRT 保持独立。
            var searchPaths = new[] { directory, previousPath ?? "" }
                .SelectMany(path => path.Split(Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(path => !path.StartsWith(PluginsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || path.Equals(directory, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, searchPaths),
                EnvironmentVariableTarget.Process);

            // 与 ONNX Runtime 的 Windows provider 加载方式保持一致，使主 DLL 的同目录依赖优先加载。
            deleteInvalidPlugin = true;
            _libraryHandle = _loadLibrary(libraryPath);
            if (_libraryHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法加载 {libraryPath}，请检查架构及配套依赖。");

            // 先直接读取稳定的 OrtApiBase 版本接口，不触发托管 NativeMethods 的静态初始化；
            // 版本不符时仍能释放失败的模块并回退到内置运行库。
            version = _readRuntimeVersion(_libraryHandle);
            if (version != RequiredVersion)
            {
                versionMismatch = true;
                throw new InvalidDataException($"原生运行库版本为 {version}，必须为 {RequiredVersion}。");
            }

            var missingDependencies = OnnxRuntimePackageCatalog.MissingDependencyFiles(InferenceDevice, directory);
            if (missingDependencies.Length > 0)
            {
                FailureReason = OnnxRuntimePackageCatalog.DescribeMissingDependencies(InferenceDevice, missingDependencies);
                throw new FileNotFoundException($"依赖不完整：缺少 {string.Join("、", missingDependencies)}。",
                    Path.Combine(directory, missingDependencies[0]));
            }

            NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly,
                (name, _, _) => name == "onnxruntime" ? _libraryHandle : IntPtr.Zero);
        }
        catch (Exception e)
        {
            if (_libraryHandle != IntPtr.Zero)
            {
                _freeLibrary(_libraryHandle);
                _libraryHandle = IntPtr.Zero;
            }

            Environment.SetEnvironmentVariable("PATH", previousPath, EnvironmentVariableTarget.Process);
            if (versionMismatch)
            {
                var deleted = TryDeletePlugin(directory);
                FailureReason = $"{directoryName} 运行库版本为 {version}，与当前程序编译使用的 ONNX Runtime {RequiredVersion} 不一致。" +
                                (deleted
                                    ? "已自动删除该插件并恢复默认 CPU 设置，如需继续使用请重新下载。"
                                    : "插件可能正被其他进程占用，无法自动删除；已恢复默认 CPU 设置，请关闭其他实例后手动删除。");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(FailureReason))
                    FailureReason = $"{directoryName} 运行库无法加载，请在硬件加速设置中重新下载。";
                if (deleteInvalidPlugin)
                    FailureReason += TryDeletePlugin(directory)
                        ? "已自动删除不完整或无法使用的插件，并恢复默认 CPU 设置。"
                        : "插件可能正被其他进程占用，无法自动删除；已恢复默认 CPU 设置。";
            }
            InferenceDevice = InferenceDeviceType.Cpu;
            StatusText = $"内置运行库（CPU）；{directoryName} 加载失败：{e.Message}";
            Log.Warning(e, "[ONNX]运行库 {Library} 加载失败，回退到 CPU", libraryPath);
            return;
        }

        // 解析器注册后，日志或状态更新异常也不能再卸载已对托管绑定公开的模块。
        RuntimeDirectory = directory;
        StatusText = $"{directoryName}（ONNX Runtime {version}）";
        Log.Information("[ONNX]已加载运行库 {Library}，版本 {Version}", libraryPath, version);
        // 句柄由本进程持有至退出。推理会话、OrtEnv 和托管静态函数指针仍可能引用它，
        // 不能在服务 Dispose 或设置页关闭时 Free，否则会产生悬空函数指针。
    }

    private static string ReadRuntimeVersion(IntPtr libraryHandle)
    {
        var getApiBase = Marshal.GetDelegateForFunctionPointer<OrtGetApiBase>(
            NativeLibrary.GetExport(libraryHandle, "OrtGetApiBase"));
        var apiBase = getApiBase();
        if (apiBase == IntPtr.Zero) throw new InvalidDataException("原生运行库未提供 OrtApiBase。");

        // OrtApiBase 依次包含 GetApi 和 GetVersionString 两个函数指针。
        var getVersion = Marshal.GetDelegateForFunctionPointer<OrtGetVersionString>(Marshal.ReadIntPtr(apiBase, IntPtr.Size));
        return Marshal.PtrToStringUTF8(getVersion()) ?? throw new InvalidDataException("无法读取原生运行库版本。");
    }

    private static string GetRequiredVersion()
    {
        var version = typeof(InferenceSession).Assembly.GetName().Version
                      ?? throw new InvalidOperationException("无法读取编译时 ONNX Runtime 版本。");
        return version.ToString(3);
    }

    private bool TryDeletePlugin(string directory)
    {
        var expected = Path.GetFullPath(OnnxRuntimePackageCatalog.EngineDirectory(PluginsDirectory, InferenceDevice));
        if (!string.Equals(Path.GetFullPath(directory), expected, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            Directory.Delete(expected, true);
            return !Directory.Exists(expected);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr OrtGetApiBase();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr OrtGetVersionString();

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string libraryPath, IntPtr file, uint flags);
}
