using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Win32;
using Vanara;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiOnnxFactory
{
    private readonly ILogger _logger;
    private readonly IOnnxRuntimePluginManager? _pluginManager;
    private readonly IOnnxRuntimePluginRegistry? _pluginRegistry;
    private IReadOnlyList<OrtEpDevice> _pluginDevices = [];
    private readonly string _cudaDeviceUuid = "";

    /// <summary>
    ///     缓存模型路径。如果一开始使用缓存就一直使用缓存文件，如果没有使用缓存就一直使用原始模型路径。
    ///     <br />
    ///     这样能避免并发加载模型问题。比如使用了未完全构建好的缓存文件，导致模型加载失败。
    /// </summary>
    private readonly ConcurrentDictionary<BgiOnnxModel, string?> _cachedModelPaths = new();


    /// <summary>
    /// 请勿直接实例化此类
    /// </summary>
    /// <param name="logger"></param>
    public BgiOnnxFactory(ILogger<BgiOnnxFactory> logger)
        : this(logger, null, null, null)
    {
        // 保留单元测试和旧调用方使用的构造入口；生产环境由依赖注入选择下面的完整构造函数。
    }

    public BgiOnnxFactory(
        ILogger<BgiOnnxFactory> logger,
        IConfigService? configService,
        IOnnxRuntimePluginManager? pluginManager,
        IOnnxRuntimePluginRegistry? pluginRegistry)
    {
        _logger = logger;
        _pluginManager = pluginManager;
        _pluginRegistry = pluginRegistry;

        var config = configService?.Get().HardwareAccelerationConfig ?? GetConfig();
        OnnxRuntimePathHelper.Prepare(config, logger);

        OptimizedModel = config.OptimizedModel;
        CudaDeviceId = config.CudaDevice;
        _cudaDeviceUuid = config.CudaDeviceUuid;
        DmlDeviceId = Service.DxgiAdapterEnumerator.ResolveDeviceId(
            config.DirectMlAdapterLuid, config.GpuDevice, logger);
        TrtUseEmbedMode = config.EmbedTensorRtCache;
        EnableCache = config.EnableTensorRtCache;
        CpuOcr = config.CpuOcr;
        OpenVinoDevice = config.OpenVinoDevice;
        OpenVinoCache = config.EnableOpenVinoCache;
        ConfiguredProvider = config.InferenceDevice;
        CudaRuntime = config.CudaRuntime;
        ProviderTypes = GetProviderType(config.InferenceDevice);
        _logger.LogDebug(
            "[ONNX]启用的provider:{Device},实际provider:{EffectiveProvider},设备:{EffectiveDevice},初始化参数: InferenceDevice={InferenceDevice}, OptimizedModel={OptimizedModel}, CudaDeviceId={CudaDeviceId}, DmlDeviceId={DmlDeviceId}, CpuOcr={CpuOcr}",
            string.Join(",", ProviderTypes.Select<ProviderType, string>(Enum.GetName!)),
            EffectiveProvider,
            EffectiveDevice,
            config.InferenceDevice,
            OptimizedModel,
            CudaDeviceId,
            DmlDeviceId,
            CpuOcr);
    }

    /// <summary>
    /// 获取 硬件加速配置
    /// 为了单元测试
    /// </summary>
    /// <returns></returns>
    private HardwareAccelerationConfig GetConfig()
    {
        try
        {
            // 直接使用配置
            return TaskContext.Instance().Config.HardwareAccelerationConfig;
        }
        catch (Exception e)
        {
            // 如果配置获取失败，使用默认配置
            _logger.LogWarning(e, "获取硬件加速配置失败，使用默认配置");
            return new HardwareAccelerationConfig();
        }
    }

    public ProviderType[] ProviderTypes { get; }
    public int DmlDeviceId { get; }
    public int CudaDeviceId { get; private set; }
    public bool OptimizedModel { get; }
    public bool TrtUseEmbedMode { get; }
    public string OpenVinoDevice { get; private set; }
    public bool EnableCache { get; }
    public bool CpuOcr { get; }
    public bool OpenVinoCache { get; }
    public HardwareAccelerationConfig.CudaRuntimeMajor CudaRuntime { get; }
    public InferenceDeviceType ConfiguredProvider { get; }
    public ProviderType EffectiveProvider { get; private set; } = ProviderType.Cpu;
    public string EffectiveDevice { get; private set; } = "CPU";
    public string StartupDiagnostic { get; private set; } = "";


    /// <summary>
    ///     根据InferenceDeviceType选择Provider
    /// </summary>
    /// <param name="inferenceDeviceType">InferenceDeviceType</param>
    /// <returns></returns>
    /// <exception cref="InvalidEnumArgumentException"></exception>
    private ProviderType[] GetProviderType(InferenceDeviceType inferenceDeviceType)
    {
        switch (inferenceDeviceType)
        {
            case InferenceDeviceType.Cpu:
                EffectiveProvider = ProviderType.Cpu;
                EffectiveDevice = "CPU";
                return [ProviderType.Cpu];
            case InferenceDeviceType.GpuDirectMl:
                //只用dml不加cpu的话在很多场景下性能很差。
                EffectiveProvider = ProviderType.Dml;
                EffectiveDevice = $"DirectML {DmlDeviceId}";
                return [ProviderType.Dml, ProviderType.Cpu];
            case InferenceDeviceType.LegacyAutoGpu:
                // 历史值只作为兼容兜底；ConfigService 会在正常读取流程中迁移为 DirectML。
                EffectiveProvider = ProviderType.Dml;
                EffectiveDevice = $"DirectML {DmlDeviceId}";
                return [ProviderType.Dml, ProviderType.Cpu];
            case InferenceDeviceType.Cuda:
                return TryActivatePlugin(InferenceDeviceType.Cuda, ProviderType.Cuda);
            case InferenceDeviceType.OpenVino:
                return TryActivatePlugin(InferenceDeviceType.OpenVino, ProviderType.OpenVino);
            default:
                StartupDiagnostic = "无效的推理设备配置，已回退到 CPU。";
                return [ProviderType.Cpu];
        }
    }

    private ProviderType[] TryActivatePlugin(InferenceDeviceType provider, ProviderType providerType)
    {
        if (_pluginManager is null || _pluginRegistry is null)
        {
            StartupDiagnostic = "当前调用方未提供 Plugin EP 服务，已回退到 CPU。";
            EffectiveProvider = ProviderType.Cpu;
            EffectiveDevice = "CPU（回退）";
            return [ProviderType.Cpu];
        }

        var failures = new List<string>();
        var resolutions = _pluginManager.ResolveForStartup(provider, CudaRuntime);
        foreach (var resolution in resolutions)
        {
            OnnxRuntimePathHelper.AppendPluginDirectory(resolution.LibraryPath, _logger);
            if (!_pluginRegistry.TryRegister(resolution, out var devices, out var error))
            {
                failures.Add($"{resolution.Descriptor.DisplayName} {resolution.Version}: {error}");
                continue;
            }

            var selectedDevices = SelectPluginDevices(provider, devices);
            if (selectedDevices.Count == 0)
            {
                failures.Add($"{resolution.Descriptor.DisplayName}: 配置的推理设备不存在");
                continue;
            }

            _pluginDevices = selectedDevices;
            _pluginManager.MarkActivationSucceeded(resolution);
            EffectiveProvider = providerType;
            EffectiveDevice = BuildDeviceDescription(selectedDevices);
            StartupDiagnostic = "";
            return [providerType, ProviderType.Cpu];
        }

        var localPackageDiagnostic = _pluginManager.GetCurrentPackages()
            .FirstOrDefault(package => package.Descriptor.Provider == provider &&
                                       !string.IsNullOrWhiteSpace(package.InstalledVersion))
            ?.StatusMessage;
        StartupDiagnostic = failures.Count == 0
            ? string.IsNullOrWhiteSpace(localPackageDiagnostic)
                ? "未安装所选 Plugin EP，已回退到 CPU。"
                : $"所选 Plugin EP 不可用，已回退到 CPU：{localPackageDiagnostic}"
            : $"Plugin EP 加载失败，已回退到 CPU：{string.Join("；", failures)}";
        _logger.LogWarning("[ONNX] {Diagnostic}", StartupDiagnostic);
        EffectiveProvider = ProviderType.Cpu;
        EffectiveDevice = "CPU（回退）";
        return [ProviderType.Cpu];
    }

    private IReadOnlyList<OrtEpDevice> SelectPluginDevices(
        InferenceDeviceType provider, IReadOnlyList<OrtEpDevice> devices)
    {
        if (provider == InferenceDeviceType.Cuda)
        {
            if (!string.IsNullOrWhiteSpace(_cudaDeviceUuid))
            {
                var uuidMatch = devices.Where(device =>
                    device.HardwareDevice.Metadata.Entries.Any(pair =>
                        (pair.Key.Equals("uuid", StringComparison.OrdinalIgnoreCase) ||
                         pair.Key.Equals("device_uuid", StringComparison.OrdinalIgnoreCase)) &&
                        pair.Value.Equals(_cudaDeviceUuid, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (uuidMatch.Length > 0)
                {
                    CudaDeviceId = checked((int)uuidMatch[0].HardwareDevice.DeviceId);
                    return uuidMatch;
                }

                var nvidiaGpu = Service.NvidiaSmiDeviceEnumerator.Enumerate()
                    .FirstOrDefault(gpu => gpu.Uuid.Equals(_cudaDeviceUuid,
                        StringComparison.OrdinalIgnoreCase));
                if (nvidiaGpu is not null)
                {
                    CudaDeviceId = checked((int)nvidiaGpu.DeviceId);
                    var indexMatch = devices.Where(device =>
                        device.HardwareDevice.DeviceId == nvidiaGpu.DeviceId).ToArray();
                    if (indexMatch.Length > 0)
                    {
                        return indexMatch;
                    }
                }
            }

            return devices.Where(device => device.HardwareDevice.DeviceId == CudaDeviceId).ToArray();
        }

        if (string.IsNullOrWhiteSpace(OpenVinoDevice) ||
            OpenVinoDevice.StartsWith("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            var supportedDevices = devices.Where(device =>
                device.HardwareDevice.Type != OrtHardwareDeviceType.NPU ||
                device.HardwareDevice.VendorId == 0x8086 ||
                device.HardwareDevice.Vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase)).ToArray();
            OpenVinoDevice = BuildOpenVinoAutoOption(supportedDevices);
            return supportedDevices;
        }

        if (OpenVinoDevice.Equals("NPU", StringComparison.OrdinalIgnoreCase))
        {
            return devices.Where(device => device.HardwareDevice.Type == OrtHardwareDeviceType.NPU &&
                                           (device.HardwareDevice.VendorId == 0x8086 ||
                                            device.HardwareDevice.Vendor.Contains("Intel",
                                                StringComparison.OrdinalIgnoreCase))).ToArray();
        }

        if (OpenVinoDevice.Equals("CPU", StringComparison.OrdinalIgnoreCase))
        {
            return devices.Where(device => device.HardwareDevice.Type == OrtHardwareDeviceType.CPU).ToArray();
        }

        if (OpenVinoDevice.StartsWith("GPU", StringComparison.OrdinalIgnoreCase))
        {
            var gpuDevices = devices.Where(device => device.HardwareDevice.Type == OrtHardwareDeviceType.GPU);
            var separator = OpenVinoDevice.IndexOf('.');
            if (separator >= 0 && uint.TryParse(OpenVinoDevice[(separator + 1)..], out var deviceId))
            {
                gpuDevices = gpuDevices.Where(device => device.HardwareDevice.DeviceId == deviceId);
            }

            return gpuDevices.ToArray();
        }

        // HETERO/MULTI 和其他 OpenVINO 高级配置由插件自行解析，设备集合保持完整。
        return devices;
    }

    private static string BuildDeviceDescription(IReadOnlyList<OrtEpDevice> devices)
    {
        return string.Join(", ", devices.Select(device =>
            $"{device.HardwareDevice.Vendor} {device.HardwareDevice.Type} {device.HardwareDevice.DeviceId}"));
    }

    private static string BuildOpenVinoAutoOption(IReadOnlyList<OrtEpDevice> devices)
    {
        var availableTypes = devices.Select(device => device.HardwareDevice.Type).ToHashSet();
        var options = new List<string>();
        if (devices.Any(device => device.HardwareDevice.Type == OrtHardwareDeviceType.NPU &&
                                 (device.HardwareDevice.VendorId == 0x8086 ||
                                  device.HardwareDevice.Vendor.Contains("Intel",
                                      StringComparison.OrdinalIgnoreCase)))) options.Add("NPU");
        if (availableTypes.Contains(OrtHardwareDeviceType.GPU)) options.Add("GPU");
        if (availableTypes.Contains(OrtHardwareDeviceType.CPU)) options.Add("CPU");
        return options.Count == 0 ? "AUTO" : $"AUTO:{string.Join(",", options)}";
    }

    /// <summary>
    ///     自动嗅探并修改path以加载cuda
    /// </summary>
    private void AppendCudaPath()
    {
        var cudaVersion =
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\NVIDIA Corporation\GPU Computing Toolkit\CUDA",
                "FirstVersionInstalled", null)?.ToString() ?? "v12.8";
        string[] filePrefix = ["cudnn", "nvrtc", "cudart", "nvinfer", "cublas", "onnx"];
        string[] environmentVariableNames = ["PATH", "CUDA_PATH", "CUDNN_PATH", "LD_LIBRARY_PATH"];

        // 例如: CUDNN\v9.8\lib\12.8\x64
        var validPaths = environmentVariableNames.SelectMany(s => Environment
                // 获取所有可能包含CUDA/cuDNN路径的环境变量
                .GetEnvironmentVariable(s, EnvironmentVariableTarget.Process)?
                .Split(Path.PathSeparator) ?? []).Distinct()
            // 环境变量下层文件夹
            .SelectMany<string, string>(s =>
                // lib路径
                [s, Path.Combine(s, cudaVersion), Path.Combine(s, "bin"), Path.Combine(s, "lib")])
            .SelectMany<string, string>(
                // cuda的版本
                s => cudaVersion.StartsWith("v", StringComparison.InvariantCultureIgnoreCase)
                    ? [s, Path.Combine(s, cudaVersion), Path.Combine(s, cudaVersion[1..])]
                    : [s, Path.Combine(s, cudaVersion)])
            .SelectMany<string, string>(s =>
            {
                // 体系架构 
                var architecture = Enum.GetName(RuntimeInformation.ProcessArchitecture);
                if (architecture is null) return [s];

                return
                [
                    s, Path.Combine(s, architecture), Path.Combine(s, architecture.ToLowerInvariant()),
                    Path.Combine(s, architecture.ToUpperInvariant())
                ];
            })
            .Where(basePath => !string.IsNullOrWhiteSpace(basePath))
            //构建完了需要搜索的路径，去重。
            .Distinct()

            //确定路径是否真的存在
            .Where(d =>
            {
                try
                {
                    return Directory.Exists(d);
                }
                catch (Exception)
                {
                    return false;
                }
            })
            .SelectMany(s =>
                //确定需要的文件是否存在
                filePrefix.SelectMany(se =>
                {
                    try
                    {
                        return Directory.GetFiles(s, $"{se}*.dll").Select(Path.GetDirectoryName).WhereNotNull();
                    }
                    catch (Exception)
                    {
                        return [];
                    }
                }))
            //去重
            .Distinct();
        AppendPath(validPaths.ToArray());
    }

    /// <summary>
    ///     将附加的path应用进来
    /// </summary>
    /// <param name="extraPath">附加的path字符串</param>
    private void AppendPath(string[] extraPath)
    {
        if (extraPath.Length <= 0) return;

        var pathVariables = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process)
            ?.Split(Path.PathSeparator).ToList() ?? new List<string>();
        pathVariables.AddRange(extraPath);
        if (pathVariables.Count <= 0)
        {
            _logger.LogWarning("[GpuAuto]SetCudaPath:No valid paths found.");
            return;
        }

        var updatedPath = string.Join(Path.PathSeparator, pathVariables.Distinct());
        _logger.LogDebug("[GpuAuto]修改进程PATH为:{UpdatedPath}", updatedPath);
        Environment.SetEnvironmentVariable("PATH", updatedPath, EnvironmentVariableTarget.Process);
    }

    /// <summary>
    ///     根据模型创建一个YoloPredictor
    /// </summary>
    /// <param name="model">模型</param>
    /// <returns>BgiYoloPredictor</returns>
    public BgiYoloPredictor CreateYoloPredictor(BgiOnnxModel model)
    {
        try
        {
            return CreateYoloPredictorCore(model);
        }
        catch (Exception exception) when (EffectiveProvider is ProviderType.OpenVino or ProviderType.Cuda)
        {
            _logger.LogError(exception,
                "[ONNX] {Provider} 创建模型 {Model} 失败，本次会话回退到 CPU",
                EffectiveProvider, model.Name);
            return new BgiYoloPredictor(model, model.ModalPath,
                CreateSessionOptions(model, false, [ProviderType.Cpu]));
        }
    }

    private BgiYoloPredictor CreateYoloPredictorCore(BgiOnnxModel model)
    {
        // logger.LogDebug("[Yolo]创建yolo预测器，模型: {ModelName}", model.Name);
        if (!EnableCache) return new BgiYoloPredictor(model, model.ModalPath, CreateSessionOptions(model, false));

        var cached = GetCached(model);
        return cached == null
            ? new BgiYoloPredictor(model, model.ModalPath, CreateSessionOptions(model, true))
            : new BgiYoloPredictor(model, cached, CreateSessionOptions(model, false));
    }

    /// <summary>
    ///     根据模型创建一个onnx运行时的InferenceSession
    /// </summary>
    /// <param name="model">模型</param>
    /// <param name="ocr">是否是用于ocr的模型，默认false</param>
    /// <returns>InferenceSession</returns>
    public InferenceSession CreateInferenceSession(BgiOnnxModel model, bool ocr = false)
    {
        _logger.LogDebug("[ONNX]创建推理会话，模型: {ModelName}", model.Name);
        ProviderType[]? providerTypes = null;
        if (CpuOcr && ocr) providerTypes = [ProviderType.Cpu];

        try
        {
            return CreateInferenceSessionCore(model, providerTypes);
        }
        catch (Exception exception) when (providerTypes is null &&
                                          (EffectiveProvider is ProviderType.OpenVino or ProviderType.Cuda))
        {
            _logger.LogError(exception,
                "[ONNX] {Provider} 创建模型 {Model} 失败，本次会话回退到 CPU",
                EffectiveProvider, model.Name);
            return new InferenceSession(model.ModalPath,
                CreateSessionOptions(model, false, [ProviderType.Cpu]));
        }
    }

    private InferenceSession CreateInferenceSessionCore(BgiOnnxModel model, ProviderType[]? providerTypes)
    {
        if (!EnableCache)
            return new InferenceSession(model.ModalPath, CreateSessionOptions(model, false, providerTypes));

        var cached = GetCached(model, providerTypes);
        return cached == null
            ? new InferenceSession(model.ModalPath, CreateSessionOptions(model, true, providerTypes))
            : new InferenceSession(cached, CreateSessionOptions(model, false, providerTypes));
    }

    /// <summary>
    ///     获取带有缓存的模型(目前只支持TensorRT)
    /// </summary>
    /// <param name="model">模型</param>
    /// <param name="forcedProvider">强制使用的 providerTypes</param>
    /// <returns>带有缓存的模型绝对路径，null表示尚未创建缓存</returns>
    private string? GetCached(BgiOnnxModel model, ProviderType[]? forcedProvider = null)
    {
        var providerTypes = forcedProvider ?? ProviderTypes;
        // 目前只支持TensorRT
        if (!providerTypes.Contains(ProviderType.TensorRt)) return null;
        var result = _cachedModelPaths.GetOrAdd(model, _GetCached);
        if (result is null) return result;

        // 判断文件是否存在
        if (File.Exists(result)) return result;

        _logger.LogWarning("[ONNX]模型 {Model} 的缓存文件可能已被删除，使用原始模型文件。", model.Name);
        return null;
    }

    private string? _GetCached(BgiOnnxModel model)
    {
        if (model.ModelRelativePath.StartsWith(BgiOnnxModel.ModelCacheRelativePath) &&
            model.ModelRelativePath.EndsWith("_ctx.onnx"))
            // 这已经是带有缓存的文件路径了
            return model.ModalPath;

        var ctxA = Path.Combine(model.CachePath, "trt", "_ctx.onnx");
        if (File.Exists(ctxA))
        {
            _logger.LogDebug("[ONNX]模型 {Model} 命中TRT匿名缓存文件: {Path}", model.Name, ctxA);
            return ctxA;
        }

        var ctxB = Path.Combine(model.CachePath, "trt",
            Path.GetFileNameWithoutExtension(model.ModalPath) + "_ctx.onnx");
        if (File.Exists(ctxB))
        {
            _logger.LogDebug("[ONNX]模型 {Model} 命中TRT命名缓存文件: {Path}", model.Name, ctxB);
            return ctxB;
        }

        _logger.LogDebug("[ONNX]没有找到模型 {Model} 的模型缓存文件。", model.Name);
        return null;
    }


    /// <summary>
    ///     通过模型路径生成SessionOptions <br />
    ///     如果加载的模型文件已经是带有缓存的模型，请将cacheFolder设为null避免重复生成。
    /// </summary>
    /// <param name="model">模型路径</param>
    /// <param name="genCache">是否生成缓存。有几种情况下不生成缓存:1为用户主动关闭，即enableCache为false。2为即将加载的模型文件已经是带有缓存的模型文件。</param>
    /// <param name="forcedProvider">强制使用的Provider,为空或null则不强制</param>
    /// <returns></returns>
    /// <exception cref="InvalidEnumArgumentException"></exception>
    private SessionOptions CreateSessionOptions(BgiOnnxModel model, bool genCache,
        ProviderType[]? forcedProvider = null)
    {
        var sessionOptions = new SessionOptions();
        foreach (var type in
                 forcedProvider is null || forcedProvider.Length == 0 ? ProviderTypes : forcedProvider)
            try
            {
                switch (type)
                {
                    case ProviderType.Dml:
                        // DirectML 执行提供程序不支持在 onnxruntime 中使用内存模式优化或并行执行。在创建 InferenceSession 期间提供会话选项时，必须禁用这些选项，否则将返回错误。
                        sessionOptions.AppendExecutionProvider_DML(DmlDeviceId);
                        sessionOptions.EnableMemoryPattern = false;
                        sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                        break;
                    case ProviderType.Cpu:
                        sessionOptions.AppendExecutionProvider_CPU();
                        // if (model.Name.Contains("PpOcr") || model.Name.Contains("Yap"))
                        // {
                        //     sessionOptions.IntraOpNumThreads = 2;  // 限制算子内部并行线程数
                        //     sessionOptions.InterOpNumThreads = 1;  // 限制算子间并行线程数（顺序执行）  
                        // }
                        break;
                    case ProviderType.Dnnl:
                        sessionOptions.AppendExecutionProvider_Dnnl();
                        break;
                    case ProviderType.OpenVino:
                        sessionOptions.AppendExecutionProvider(OrtEnv.Instance(), _pluginDevices,
                            GetOpenVinoProviderConfig(OpenVinoCache ? model.Name : null));
                        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL;
                        break;
                    case ProviderType.TensorRt:
                        using (var options = new OrtTensorRTProviderOptions())
                        {
                            options.UpdateOptions(GetTrtProviderConfig(genCache ? model.CachePath : null));
                            sessionOptions.AppendExecutionProvider_Tensorrt(options);
                        }

                        break;
                    case ProviderType.Cuda:
                        sessionOptions.AppendExecutionProvider(OrtEnv.Instance(), _pluginDevices,
                            GetCudaProviderConfig());
                        break;
                    default:
                        throw new InvalidEnumArgumentException("无效的推理设备");
                }
            }
            catch (Exception e)
            {
                _logger.LogError("无法加载指定的 ONNX provider {Provider}，跳过。请检查推理设备配置是否正确。({Err})", Enum.GetName(type),
                    e.Message);
                if (type is ProviderType.OpenVino or ProviderType.Cuda)
                {
                    // Plugin EP 会话初始化失败时交给上层统一创建纯 CPU 会话，避免返回半配置状态。
                    sessionOptions.Dispose();
                    throw;
                }
            }

        if (!OptimizedModel) return sessionOptions;
        if (!genCache) return sessionOptions;
        var optPath = Path.Combine(model.CachePath, "optimized");
        if (!Directory.Exists(optPath)) Directory.CreateDirectory(optPath);
        sessionOptions.OptimizedModelFilePath = Path.Combine(optPath, Path.GetFileName(model.ModalPath));
        return sessionOptions;
    }


    /// <summary>
    ///     获取TensorRT的配置
    /// </summary>
    /// <param name="cacheFolder">缓存生成的目录</param>
    /// <returns>trt配置</returns>
    private Dictionary<string, string> GetTrtProviderConfig(string? cacheFolder)
    {
        if (cacheFolder is null)
        {
            // 不使用缓存目录
            var r = new Dictionary<string, string>
            {
                ["device_id"] = CudaDeviceId.ToString()
            };
            return r;
        }

        var result = new Dictionary<string, string>
        {
            ["trt_engine_cache_enable"] = "1",
            ["trt_dump_ep_context_model"] = "1",
            ["trt_ep_context_file_path"] = Path.Combine(cacheFolder, "trt"),
            // ["trt_ep_context_embed_mode"] = "1", // 因为yoloSharp是把模型转为嵌入式运行，不这样会爆炸
            // ["trt_engine_cache_path"] = ".\\" // 没必要了
            ["trt_timing_cache_enable"] = "1",
            ["trt_timing_cache_path"] =
                Global.Absolute(Path.Combine(BgiOnnxModel.ModelCacheRelativePath, "trt_timing")),
            // ["trt_force_timing_cache"] = "1",
            ["device_id"] = CudaDeviceId.ToString()
        };
        if (TrtUseEmbedMode)
        {
            result["trt_ep_context_embed_mode"] = "1";
        }
        else
        {
            result["trt_ep_context_embed_mode"] = "0";
            result["trt_engine_cache_path"] = ".\\";
        }

        if (!Directory.Exists(result["trt_ep_context_file_path"]))
        {
            // 如果不存在就创建目录
            _logger.LogDebug("[ONNX]TensorRT上下文文件路径不存在，创建目录: {Path}", result["trt_ep_context_file_path"]);
            try
            {
                Directory.CreateDirectory(result["trt_ep_context_file_path"]);
            }
            catch (Exception e)
            {
                _logger.LogError("无法创建TensorRT上下文文件路径: {Path}，请检查权限。({Err})",
                    result["trt_ep_context_file_path"], e.Message);
                // 如果无法创建目录，就不使用缓存
                result.Remove("trt_ep_context_file_path");
            }
        }

        if (!Directory.Exists(result["trt_timing_cache_path"]))
        {
            // 如果不存在就创建目录
            _logger.LogDebug("[ONNX]TensorRT计时缓存路径不存在，创建目录: {Path}", result["trt_timing_cache_path"]);
            try
            {
                Directory.CreateDirectory(result["trt_timing_cache_path"]);
            }
            catch (Exception e)
            {
                _logger.LogError("无法创建TensorRT计时缓存路径: {Path}，请检查权限。({Err})",
                    result["trt_timing_cache_path"], e.Message);
                // 如果无法创建目录，就不使用缓存
                result.Remove("trt_timing_cache_path");
            }
        }

        return result;
    }

    /// <summary>
    ///     获取cuda provider的配置
    /// </summary>
    /// <returns>cuda配置</returns>
    private Dictionary<string, string> GetCudaProviderConfig()
    {
        var result = new Dictionary<string, string>
        {
            ["device_id"] = CudaDeviceId.ToString()
        };
        return result;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cacheFolder"></param>
    /// <returns></returns>
    private Dictionary<string, string> GetOpenVinoProviderConfig(string? cacheFolder)
    {
        var result = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(OpenVinoDevice))
        {
            result["device_type"] = OpenVinoDevice;
        }

        if (!string.IsNullOrWhiteSpace(cacheFolder))
        {
            // OpenVINO缓存目录
            var storageRoot = _pluginManager?.StorageRoot ?? Path.Combine(AppContext.BaseDirectory, "ort");
            result["cache_dir"] = Path.Combine(storageRoot, "cache", "openvino", cacheFolder);
            if (!Directory.Exists(result["cache_dir"]))
            {
                try
                {
                    Directory.CreateDirectory(result["cache_dir"]);
                }
                catch (Exception e)
                {
                    _logger.LogError("无法创建OpenVINO缓存目录: {Path}，请检查权限。({Err})", result["cache_dir"],
                        e.Message);
                    // 如果无法创建目录，就不使用缓存
                    result.Remove("cache_dir");
                }
            }
        }

        result["enable_opencl_throttling"] = "true";
        return result;
    }
}
