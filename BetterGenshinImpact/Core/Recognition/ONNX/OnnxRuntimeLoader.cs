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
}

public sealed class OnnxRuntimeLoader : IOnnxRuntimeSelection
{
    public const string RequiredVersion = "1.21.0";
    private IntPtr _libraryHandle;

    public string PluginsDirectory { get; }
    public string RuntimeDirectory { get; private set; }
    public InferenceDeviceType InferenceDevice { get; private set; }
    public string StatusText { get; private set; } = "内置 DirectML / CPU";

    public OnnxRuntimeLoader(HardwareAccelerationConfig config, string baseDirectory)
    {
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
        string version;
        try
        {
            if (!File.Exists(libraryPath)) throw new FileNotFoundException("运行库目录中缺少 onnxruntime.dll。", libraryPath);

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

            // 与 ORT 1.21 的 Windows provider 加载方式保持一致，使主 DLL 的同目录依赖优先加载。
            _libraryHandle = LoadLibraryEx(libraryPath, IntPtr.Zero, 0x00000008); // LOAD_WITH_ALTERED_SEARCH_PATH
            if (_libraryHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法加载 {libraryPath}，请检查架构及配套依赖。");

            // 先直接读取稳定的 OrtApiBase 版本接口，不触发托管 NativeMethods 的静态初始化；
            // 版本不符时仍能释放失败的模块并回退到内置运行库。
            version = ReadRuntimeVersion(_libraryHandle);
            if (version != RequiredVersion)
                throw new InvalidDataException($"原生运行库版本为 {version}，必须为 {RequiredVersion}。");

            NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly,
                (name, _, _) => name == "onnxruntime" ? _libraryHandle : IntPtr.Zero);
        }
        catch (Exception e)
        {
            if (_libraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_libraryHandle);
                _libraryHandle = IntPtr.Zero;
            }

            Environment.SetEnvironmentVariable("PATH", previousPath, EnvironmentVariableTarget.Process);
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr OrtGetApiBase();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr OrtGetVersionString();

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string libraryPath, IntPtr file, uint flags);
}
