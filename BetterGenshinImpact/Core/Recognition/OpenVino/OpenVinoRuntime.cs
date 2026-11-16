using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using OpenVinoSharp;
using OvCore = OpenVinoSharp.Core;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

/// <summary>
///     OpenVINO（C# API）后端的运行时承载：运行库目录约定、安装检测与进程级 <see cref="Core" /> 单例。
///     运行库由 NuGet 包 OpenVINO.runtime.win 按需下载安装到 Plugins/OpenVINO（见 OpenVinoRuntimeInstaller），
///     经 <c>Ov.Initialize</c> 以显式路径加载，不污染进程 PATH（单 onnxruntime.dll 约束同样适用于本目录隔离）。
/// </summary>
public static class OpenVinoRuntime
{
    /// <summary>运行库目录（相对启动目录）。</summary>
    public static readonly string RuntimeDirectory = Global.Absolute("Plugins/OpenVINO");

    /// <summary>编译缓存根目录（模型编译缓存按模型文件哈希分目录）。</summary>
    public static string CacheDirectory => Path.Combine(ONNX.ResolvedInferenceRuntime.ModelCacheDirectory, "OpenVino");

    /// <summary>推理可用的最小 DLL 集合（与旧 OpenVINO 执行提供程序的必需清单对齐，不含 ORT 侧文件）。</summary>
    public static readonly string[] RequiredFiles =
    [
        "openvino.dll",
        "openvino_c.dll",
        "openvino_ir_frontend.dll",
        "openvino_onnx_frontend.dll",
        "openvino_intel_cpu_plugin.dll",
        "tbb12.dll"
    ];

    private static readonly object InitLock = new();
    private static OvCore? _sharedCore;
    private static string? _sharedCoreLoadError;

    /// <summary>运行库是否已安装齐备（只查文件，不加载原生库）。</summary>
    public static bool IsInstalled()
    {
        foreach (var file in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(RuntimeDirectory, file))) return false;
        }

        return true;
    }

    /// <summary>首个缺失的必需文件名；已齐备时返回 null。</summary>
    public static string? FirstMissingFile()
    {
        foreach (var file in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(RuntimeDirectory, file))) return file;
        }

        return null;
    }

    /// <summary>是否支持指定设备（按 OpenVINO 设备名前缀：CPU / GPU / NPU / AUTO 族）。</summary>
    public static bool IsSupportedDevice(string deviceId)
        => deviceId.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)
           || deviceId.StartsWith("GPU", StringComparison.OrdinalIgnoreCase)
           || deviceId.StartsWith("NPU", StringComparison.OrdinalIgnoreCase)
           || deviceId.StartsWith("AUTO", StringComparison.OrdinalIgnoreCase)
           || deviceId.StartsWith("HETERO", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     获取进程共享的 OpenVINO Core（首次访问时加载运行库并创建，失败时抛出可读异常）。
    ///     OpenVINO 官方推荐单 Core 复用；编译缓存等属性在 compile_model 调用时按需传入。
    /// </summary>
    public static OvCore GetSharedCore()
    {
        if (_sharedCore != null) return _sharedCore;
        lock (InitLock)
        {
            if (_sharedCore != null) return _sharedCore;
            if (_sharedCoreLoadError != null) throw new OpenVinoUnavailableException(_sharedCoreLoadError);
            try
            {
                var libraryPath = Path.Combine(RuntimeDirectory, "openvino_c.dll");
                if (!File.Exists(libraryPath))
                    throw new OpenVinoUnavailableException(
                        $"OpenVINO 运行库缺失：{libraryPath}。请在硬件加速设置的 OpenVINO 卡片中下载运行库，或关闭 OpenVINO 后端。");
                // 显式路径初始化：运行库与全部插件都在 Plugins/OpenVINO 内，不依赖也不修改进程 PATH。
                Ov.Initialize(libraryPath);
                _sharedCore = new OvCore();
                return _sharedCore;
            }
            catch (OpenVinoUnavailableException e)
            {
                _sharedCoreLoadError = e.Message;
                throw;
            }
            catch (Exception e)
            {
                _sharedCoreLoadError =
                    $"OpenVINO 运行库加载失败：{e.Message}。请在硬件加速设置中重新下载 OpenVINO 运行库，或关闭 OpenVINO 后端。";
                throw new OpenVinoUnavailableException(_sharedCoreLoadError, e);
            }
        }
    }
}

/// <summary>OpenVINO 后端不可用（运行库缺失、加载失败、设备不可用或推理异常）。</summary>
public class OpenVinoUnavailableException : Exception
{
    public OpenVinoUnavailableException(string message) : base(message) { }
    public OpenVinoUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>OpenVINO Core 的懒加载缓存容器：同一 (model, device, cache) 组合复用已编译模型。</summary>
internal static class OpenVinoCompiledModelCache
{
    private static readonly ConcurrentDictionary<string, CachedModel> Cache = new();

    public static CompiledModel GetOrCompile(string modelPath, string deviceId, bool enableCache,
        string? cacheDirectory = null)
    {
        var key = $"{modelPath.Length}:{modelPath}|{deviceId}|{enableCache}";
        return Cache.GetOrAdd(key, _ => new CachedModel(modelPath, deviceId, enableCache, cacheDirectory)).Model;
    }

    /// <summary>缓存条目：懒创建，编译失败不缓存异常（下次重试）。</summary>
    private sealed class CachedModel
    {
        private readonly string _modelPath;
        private readonly string _deviceId;
        private readonly bool _enableCache;
        private readonly string? _cacheDirectory;
        private readonly object _lock = new();
        private CompiledModel? _model;

        public CachedModel(string modelPath, string deviceId, bool enableCache, string? cacheDirectory)
        {
            _modelPath = modelPath;
            _deviceId = deviceId;
            _enableCache = enableCache;
            _cacheDirectory = cacheDirectory;
        }

        public CompiledModel Model
        {
            get
            {
                if (_model != null) return _model;
                lock (_lock)
                {
                    if (_model != null) return _model;
                    var core = OpenVinoRuntime.GetSharedCore();
                    var properties = new Dictionary<string, string>();
                    if (_enableCache && !string.IsNullOrEmpty(_cacheDirectory))
                    {
                        try
                        {
                            Directory.CreateDirectory(_cacheDirectory);
                            properties["CACHE_DIR"] = _cacheDirectory;
                        }
                        catch
                        {
                            // 缓存目录创建失败只影响编译速度，不阻断推理。
                        }
                    }

                    _model = properties.Count > 0
                        ? core.compile_model_unicode(_modelPath, _deviceId, properties)
                        : core.compile_model_unicode(_modelPath, _deviceId);
                    return _model;
                }
            }
        }
    }
}
