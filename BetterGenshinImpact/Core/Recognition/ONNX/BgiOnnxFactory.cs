using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Win32;
using Vanara;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiOnnxFactory
{
    private readonly ILogger _logger;

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
    {
        _logger = logger;

        var config = GetConfig();
        if (config.AutoAppendCudaPath) AppendCudaPath();

        if (!string.IsNullOrWhiteSpace(config.AdditionalPath))
            AppendPath(config.AdditionalPath.Split(Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));


        OptimizedModel = config.OptimizedModel;
        CudaDeviceId = config.CudaDevice;
        DmlDeviceId = config.GpuDevice;
        TrtUseEmbedMode = config.EmbedTensorRtCache;
        CpuOcr = config.CpuOcr;
        OpenVinoDevice = config.OpenVinoDevice;
        OpenVinoCache = config.EnableOpenVinoCache;
        ProviderTypes = GetProviderType(config.InferenceDevice);
        _logger.LogDebug(
            "[ONNX]启用的provider:{Device},初始化参数: InferenceDevice={InferenceDevice}, OptimizedModel={OptimizedModel}, CudaDeviceId={CudaDeviceId}, DmlDeviceId={DmlDeviceId}, EmbedTensorRtCache={EmbedTensorRtCache}, CpuOcr={CpuOcr}",
            string.Join(",", ProviderTypes.Select<ProviderType, string>(Enum.GetName!)),
            config.InferenceDevice,
            OptimizedModel,
            CudaDeviceId,
            DmlDeviceId,
            TrtUseEmbedMode,
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
    public int CudaDeviceId { get; }
    public bool OptimizedModel { get; }
    public bool TrtUseEmbedMode { get; }
    public string OpenVinoDevice { get; }
    public bool CpuOcr { get; }
    public bool OpenVinoCache { get; }


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
                return [ProviderType.Cpu];
            case InferenceDeviceType.GpuDirectMl:
                //只用dml不加cpu的话在很多场景下性能很差。
                return [ProviderType.Dml, ProviderType.Cpu];
            case InferenceDeviceType.Gpu:
            {
                List<ProviderType> list = [];
                SessionOptions? testSession = null;
                var hasGpu = false;
                if (!hasGpu && CudaDeviceId >= 0)
                    try
                    {
                        testSession = SessionOptions.MakeSessionOptionWithTensorrtProvider(CudaDeviceId);
                        list.Add(ProviderType.TensorRt);
                        hasGpu = true;
                    }
                    catch (Exception e)
                    {
                        _logger.LogDebug("[init]无法加载TensorRT。可能不支持，跳过。({Err})", e.Message);
                    }
                    finally
                    {
                        testSession?.Dispose();
                    }

                // TensorRT 可能不支援全部的 ONNX 操作，保留 CUDA 作爲其下的層
                if (hasGpu && list.Contains(ProviderType.TensorRt) && CudaDeviceId >= 0)
                    try
                    {
                        testSession = SessionOptions.MakeSessionOptionWithCudaProvider(CudaDeviceId);
                        list.Add(ProviderType.Cuda);
                    }
                    catch (Exception e)
                    {
                        _logger.LogDebug("[init]无法加载CUDA作为TensorRT的后备provider，跳过。({Err})", e.Message);
                    }
                    finally
                    {
                        testSession?.Dispose();
                    }

                if (!hasGpu && CudaDeviceId >= 0)
                    // 僅 CUDA 优先级较低，因为跑起来并不太理想。
                    try
                    {
                        testSession = SessionOptions.MakeSessionOptionWithCudaProvider(CudaDeviceId);
                        list.Add(ProviderType.Cuda);
                        hasGpu = true;
                    }
                    catch (Exception e)
                    {
                        _logger.LogDebug("[init]无法加载CUDA。可能不支持，跳过。({Err})", e.Message);
                    }
                    finally
                    {
                        testSession?.Dispose();
                    }

                if (!hasGpu) _logger.LogWarning("[init]GPU自动选择失败，回退到CPU处理");

                //无论如何都要加入cpu，一些计算在纯gpu上不被支持或性能很烂
                list.Add(ProviderType.Cpu);
                return list.ToArray();
            }

            case InferenceDeviceType.OpenVino:
            {
                List<ProviderType> list = [];
                SessionOptions? testSession = null;
                // OpenVino是英特尔的OpenVINO执行提供程序
                // 目前来看比Dml强
                try
                {
                    testSession = new SessionOptions();
                    testSession.AppendExecutionProvider("OpenVINO", GetOpenVinoProviderConfig(null));
                    testSession.GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL;
                    list.Add(ProviderType.OpenVino);
                }
                catch (Exception e)
                {
                    _logger.LogDebug("[init]无法加载OpenVino。可能不支持，跳过。({Err})", e.Message);
                }
                finally
                {
                    testSession?.Dispose();
                }

                list.Add(ProviderType.Cpu);
                return list.ToArray();
            }
            default:
                throw new InvalidEnumArgumentException("无效的推理设备");
        }
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
        foreach (var cudapath in validPaths)
            _logger.LogDebug("[CUDA_PATH_DEBUG]PATH: {Path}", cudapath);
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
        // logger.LogDebug("[Yolo]创建yolo预测器，模型: {ModelName}", model.Name);

        if (!ProviderTypes.Contains(ProviderType.TensorRt))
            return new BgiYoloPredictor(model, model.ModelPath, CreateSessionOptions(model));

        var cached = GetTrtEmbedCache(model);
        return cached == null
            ? new BgiYoloPredictor(model, model.ModelPath, CreateSessionOptions(model))
            : new BgiYoloPredictor(model, cached, CreateSessionOptions(model));
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

        // 使用 List 类型，方便对无法使用的 EP 捕获异常的同时删改 EP 列表
        List<ProviderType> providerTypes = [.. ProviderTypes];

        // TensorRT 目前暂时无法很好支持 OCR，因为 OCR 的输入画布大小多变，每种画布尺寸都要对应编译缓存
        if ((ocr && CpuOcr) ||
            (ocr && ProviderTypes.Contains(ProviderType.TensorRt)))
            providerTypes = [ProviderType.Cpu];

        // 当前只有 TensorRT Embed 缓存模式下需要不同的模型路径，因此不包含 TensorRT EP 时直接调用原模型
        if (!providerTypes.Contains(ProviderType.TensorRt))
            return new InferenceSession(model.ModelPath, CreateSessionOptions(model, providerTypes));

        // 用于检测是否存在 TensorRT Embed 缓存，并尝试使用
        var trtEmbedCache = GetTrtEmbedCache(model);
        if ((trtEmbedCache != null) && TrtUseEmbedMode)
            try
            {
                return new InferenceSession(trtEmbedCache, CreateSessionOptions(model, providerTypes));
            }
            catch (Exception e)
            {
                // 目前假设这是在游戏内加载模型时发生的，目前暂时没有实现检测 Engine 是否被生成的代码，假设 Engine 也不可用时，从零加载 TensorRT 所要花费的时间是不可接受的，所以不为其使用 TensorRT EP
                // 这种情况通常会被缓存控制相关的逻辑避免，但以防万一和 Embed 缓存损坏的情况发生，使用相关代码尝试规避并提升用户体验
                _logger.LogWarning("[ONNX] 模型 {Model} TensorRT Embed 模式初始化失败，其将不使用 TensorRT EP 进行推理 ({ErrorMsg})", model.Name, e);
                providerTypes.Remove(ProviderType.TensorRt);
            }

        // 不使用 TensorRT Embed 缓存时调用原模型，同时若 TensorRT 作为 EP 的选择之一时保持不变，具体是否生成 Embed 缓存会由变量 TrtUseEmbedMode 决定，若这个变量为真，接下来将会生成 Embed 缓存并在今后的使用中被加载
        return new InferenceSession(model.ModelPath, CreateSessionOptions(model, providerTypes));
    }

    /// <summary>
    ///     获取模型對應的 TensorRT 緩存
    /// </summary>
    /// <param name="model">模型</param>
    /// <returns>带有缓存的模型绝对路径，null表示尚未创建缓存</returns>
    private string? GetTrtEmbedCache(BgiOnnxModel model)
    {
        var result = _cachedModelPaths.GetOrAdd(model, FindTrtContextCache);
        if (result is null || File.Exists(result)) return result;

        _logger.LogWarning("[ONNX]模型 {Model} 的 TensorRT Context 缓存文件可能已被删除，使用原始模型文件。",
            model.Name);
        return null;
    }

    private string? FindTrtContextCache(BgiOnnxModel model)
    {
        var cachePath = GetTrtCachePath(model);
        if (cachePath is null) return null;

        var tensorrtCacheFile = Path.Combine(cachePath, $"{model.CacheIdentifier}_ctx.onnx");
        if (File.Exists(tensorrtCacheFile))
        {
            _logger.LogDebug("[ONNX]模型 {Model} 命中 TensorRT Context 缓存: {Path}", model.Name,
                tensorrtCacheFile);
            return tensorrtCacheFile;
        }
        else
        {
            _logger.LogWarning("[ONNX]模型 {Model} 未命中 TensorRT Context 缓存。正编译并生成缓存，请等待。",
                model.Name);
            return null;
        }
    }

    private string? GetTrtCachePath(BgiOnnxModel model)
    {
        try
        {
            return model.CachePathDeploy(EPTensorRt.TrtCacheDirectoryGet(CudaDeviceId));
        }
        catch (Exception e)
        {
            _logger.LogError("[ONNX]无法确定模型 {Model} 的 TensorRT 缓存目录，TensorRT 无法使用。({Err})",
                model.Name, e.Message);
            return null;
        }
    }

    /// <summary>
    ///     通过模型生成 SessionOptions
    /// </summary>
    /// <param name="model">模型</param>
    /// <param name="forcedProvider">强制使用的Provider,为空或null则不强制</param>
    /// <param name="generateTrtContextModel">是否生成 TensorRT Context 模型</param>
    /// <returns></returns>
    /// <exception cref="InvalidEnumArgumentException"></exception>
    private SessionOptions CreateSessionOptions(BgiOnnxModel model, List<ProviderType>? forcedProvider = null,
        bool generateTrtContextModel = true)
    {
        var sessionOptions = new SessionOptions();
        List<ProviderType> providerTypes = forcedProvider is { Count: > 0 }
            ? forcedProvider
            : [.. ProviderTypes];
        foreach (var type in providerTypes)
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
                    case ProviderType.OpenVino:
                        sessionOptions.AppendExecutionProvider("OpenVINO",
                            GetOpenVinoProviderConfig(OpenVinoCache ? model.CachePath : null));
                        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL;
                        break;
                    case ProviderType.TensorRt:
                        using (var options = new OrtTensorRTProviderOptions())
                        {
                            var trtConfig = GetTrtProviderConfig(model, generateTrtContextModel);
                            if (trtConfig != null)
                                options.UpdateOptions(trtConfig);
                            else
                            {
                                _logger.LogError("[ONNX] TensorRT 運行時配置創建失敗，無法使用 TensorRT。");
                                break;
                            }
                            sessionOptions.AppendExecutionProvider_Tensorrt(options);
                        }

                        break;
                    case ProviderType.Cuda:
                        using (var options = new OrtCUDAProviderOptions())
                        {
                            options.UpdateOptions(GetCudaProviderConfig());
                            sessionOptions.AppendExecutionProvider_CUDA(options);
                        }

                        break;
                    default:
                        throw new InvalidEnumArgumentException("无效的推理设备");
                }
            }
            catch (Exception e)
            {
                _logger.LogError("无法加载 ONNX EP {Provider}，跳过。请检查推理设备配置是否正确。({Err})", Enum.GetName(type),
                    e.Message);
            }

        if (!OptimizedModel) return sessionOptions;
        var optPath = Path.Combine(model.CachePath, "optimized");
        Directory.CreateDirectory(optPath);
        sessionOptions.OptimizedModelFilePath = Path.Combine(optPath, Path.GetFileName(model.ModelPath));
        return sessionOptions;
    }


    /// <summary>
    ///     生成 TensorRT EP 配置
    /// </summary>
    /// <param name="model">BgiOnnxModel 類的模型</param>
    /// <param name="generateContextModel">是否生成 TensorRT Context 模型</param>
    /// <returns>TensorRT EP 配置</returns>
    private Dictionary<string, string>? GetTrtProviderConfig(BgiOnnxModel model, bool generateContextModel)
    {
        var contextDirectory = GetTrtCachePath(model);
        if (contextDirectory is null) return null;

        if (!Directory.Exists(contextDirectory))
        {
            // 如果不存在就创建目录
            _logger.LogDebug("[ONNX] TensorRT 上下文文件目录不存在，创建目录: {Path}", contextDirectory);
        }

        try
        {
            Directory.CreateDirectory(contextDirectory);
        }
        catch (Exception e)
        {
            _logger.LogError("[ONNX] 无法创建 TensorRT 上下文文件目录: {Path}，请检查权限。TensorRT 无法使用。({Err})",
                contextDirectory, e.Message);
            return null;
        }

        var result = new Dictionary<string, string>
        {
            ["device_id"] = CudaDeviceId.ToString(),

            // TensorRT Timing cache
            ["trt_timing_cache_enable"] = "1",
            ["trt_timing_cache_path"] = contextDirectory,
            // ["trt_force_timing_cache"] = "1",

            // TensorRT Engine cache
            ["trt_engine_cache_enable"] = "1",
            ["trt_engine_cache_path"] = contextDirectory,
            ["trt_engine_cache_prefix"] = model.CacheIdentifier
        };

        if (!generateContextModel) return result;

        result["trt_dump_ep_context_model"] = "1";
        result["trt_ep_context_file_path"] =
            Path.Combine(contextDirectory, $"{model.CacheIdentifier}_ctx.onnx");
        if (TrtUseEmbedMode)
        {
            result["trt_ep_context_embed_mode"] = "1";
        }
        else
        {
            result["trt_ep_context_embed_mode"] = "0";
        }

        return result;
    }

    /// <summary>
    ///     生成 CUDA EP 配置
    /// </summary>
    /// <returns>CUDA EP 配置</returns>
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
            result["deice_type"] = OpenVinoDevice;
        }

        if (!string.IsNullOrWhiteSpace(cacheFolder))
        {
            // OpenVINO缓存目录
            result["cache_dir"] = Path.Combine(cacheFolder, "openvino");
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
