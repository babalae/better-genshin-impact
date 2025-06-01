using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Win32;
using Vanara;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiOnnxFactory
{
    private readonly ILogger logger;

    /// <summary>
    ///     缓存模型路径。如果一开始使用缓存就一直使用缓存文件，如果没有使用缓存就一直使用原始模型路径。
    ///     <br />
    ///     这样能避免并发加载模型问题。比如使用了未完全构建好的缓存文件，导致模型加载失败。
    /// </summary>
    private readonly ConcurrentDictionary<BgiOnnxModel, string?> _cachedModelPaths = new();

    /// <summary>
    /// 请勿直接实例化此类
    /// </summary>
    /// <param name="hardwareAccelerationConfig"></param>
    /// <param name="logger"></param>
    public BgiOnnxFactory(HardwareAccelerationConfig hardwareAccelerationConfig, ILogger<BgiOnnxFactory> logger)
    {
        var config = hardwareAccelerationConfig;
        this.logger = logger;
        if (config.AutoAppendCudaPath) AppendCudaPath();

        if (string.IsNullOrWhiteSpace(config.AdditionalPath))
            AppendPath(config.AdditionalPath.Split(Path.PathSeparator));

        ProviderTypes = GetProviderType(config.InferenceDevice, CudaDeviceId, DmlDeviceId);
        OptimizedModel = config.OptimizedModel;
        CudaDeviceId = config.CudaDevice;
        DmlDeviceId = config.GpuDevice;
        TrtUseEmbedMode = config.EmbedTensorRtCache;
        EnableCache = config.EnableTensorRtCache;
        CpuOcr = config.CpuOcr;
        this.logger.LogDebug(
            "[ONNX]启用的provider:{Device},初始化参数: InferenceDevice={InferenceDevice}, OptimizedModel={OptimizedModel}, CudaDeviceId={CudaDeviceId}, DmlDeviceId={DmlDeviceId}, EmbedTensorRtCache={EmbedTensorRtCache}, EnableTensorRtCache={EnableTensorRtCache}, CpuOcr={CpuOcr}",
            string.Join(",", ProviderTypes.Select<ProviderType, string>(Enum.GetName)),
            config.InferenceDevice,
            OptimizedModel,
            CudaDeviceId,
            DmlDeviceId,
            TrtUseEmbedMode,
            EnableCache,
            CpuOcr);
    }

    public ProviderType[] ProviderTypes { get; }
    public int DmlDeviceId { get; }
    public int CudaDeviceId { get; }
    public bool OptimizedModel { get; }
    public bool TrtUseEmbedMode { get; }
    public bool EnableCache { get; }
    public bool CpuOcr { get; }


    /// <summary>
    ///     根据InferenceDeviceType选择Provider
    /// </summary>
    /// <param name="inferenceDeviceType">InferenceDeviceType</param>
    /// <param name="cudaDeviceId">cuda设备id</param>
    /// <param name="dmlDeviceId">dml设备id</param>
    /// <returns></returns>
    /// <exception cref="InvalidEnumArgumentException"></exception>
    private ProviderType[] GetProviderType(InferenceDeviceType inferenceDeviceType, int cudaDeviceId,
        int dmlDeviceId)
    {
        switch (inferenceDeviceType)
        {
            case InferenceDeviceType.Cpu:
                return [ProviderType.Cpu];
            case InferenceDeviceType.GpuDirectMl:
                //只用dml不加cpu的话在很多场景下性能很差。
                return [ProviderType.Dml, ProviderType.Cpu];
            case InferenceDeviceType.Gpu:
                List<ProviderType> list = [];
                SessionOptions? testSession = null;
                var hasGpu = false;
                if (!hasGpu && cudaDeviceId >= 0)
                    // tensorrt本身包含cuda，设备id也是cuda的id，且比纯cuda效果好很多。
                    try
                    {
                        testSession = SessionOptions.MakeSessionOptionWithTensorrtProvider(cudaDeviceId);
                        list.Add(ProviderType.TensorRt);
                        hasGpu = true;
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug("[init]无法加载TensorRt。可能不支持，跳过。({Err})", e.Message);
                    }
                    finally
                    {
                        testSession?.Dispose();
                    }

                if (!hasGpu && dmlDeviceId >= 0)
                    // dml效果不如tensorrt，但是比纯cuda稳定性强
                    try
                    {
                        testSession = new SessionOptions();
                        testSession.AppendExecutionProvider_DML(dmlDeviceId);
                        list.Add(ProviderType.Dml);
                        hasGpu = true;
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug("[init]无法加载DML。可能不支持，跳过。({Err})", e.Message);
                    }
                    finally
                    {
                        testSession?.Dispose();
                    }

                if (!hasGpu && cudaDeviceId >= 0)
                    // cuda优先级比较低，因为跑起来并不太理想。
                    try
                    {
                        testSession = SessionOptions.MakeSessionOptionWithCudaProvider(cudaDeviceId);
                        list.Add(ProviderType.Cuda);
                        hasGpu = true;
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug("[init]无法加载Cuda。可能不支持，跳过。({Err})", e.Message);
                    }
                    finally
                    {
                        testSession?.Dispose();
                    }

                if (!hasGpu) logger.LogWarning("[init]GPU自动选择失败，回退到CPU处理");

                //无论如何都要加入cpu，一些计算在纯gpu上不被支持或性能很烂
                list.Add(ProviderType.Cpu);
                return list.ToArray();
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
            .Where(Directory.Exists)
            .SelectMany(s =>
                //确定需要的文件是否存在
                filePrefix.SelectMany(se =>
                    Directory.GetFiles(s, $"{se}*.dll").Select(Path.GetDirectoryName).WhereNotNull()))
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
            logger.LogWarning("[GpuAuto]SetCudaPath:No valid paths found.");
            return;
        }

        var updatedPath = string.Join(Path.PathSeparator, pathVariables.Distinct());
        logger.LogDebug("[GpuAuto]修改进程PATH为:{UpdatedPath}", updatedPath);
        Environment.SetEnvironmentVariable("PATH", updatedPath, EnvironmentVariableTarget.Process);
    }

    /// <summary>
    ///     根据模型创建一个YoloPredictor
    /// </summary>
    /// <param name="model">模型</param>
    /// <returns>BgiYoloPredictor</returns>
    public BgiYoloPredictor CreateYoloPredictor(BgiOnnxModel model)
    {
        logger.LogDebug("[Yolo]创建yolo预测器，模型: {ModelName}", model.Name);
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
        logger.LogDebug("[ONNX]创建推理会话，模型: {ModelName}", model.Name);
        ProviderType[]? providerTypes = null;
        if (CpuOcr && ocr) providerTypes = [ProviderType.Cpu];

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

        logger.LogWarning("[ONNX]模型 {Model} 的缓存文件可能已被删除，使用原始模型文件。", model.Name);
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
            logger.LogDebug("[ONNX]模型 {Model} 命中TRT匿名缓存文件: {Path}", model.Name, ctxA);
            return ctxA;
        }

        var ctxB = Path.Combine(model.CachePath, "trt",
            Path.GetFileNameWithoutExtension(model.ModalPath) + "_ctx.onnx");
        if (File.Exists(ctxB))
        {
            logger.LogDebug("[ONNX]模型 {Model} 命中TRT命名缓存文件: {Path}", model.Name, ctxB);
            return ctxB;
        }

        logger.LogDebug("[ONNX]没有找到模型 {Model} 的模型缓存文件。", model.Name);
        return null;
    }


    /// <summary>
    ///     通过模型路径生成SessionOptions <br />
    ///     如果加载的模型文件已经是带有缓存的模型，请将cacheFolder设为null避免重复生成。
    /// </summary>
    /// <param name="path">模型路径</param>
    /// <param name="genCache">是否生成缓存。有几种情况下不生成缓存:1为用户主动关闭，即enableCache为false。2为即将加载的模型文件已经是带有缓存的模型文件。</param>
    /// <param name="forcedProvider">强制使用的Provider,为空或null则不强制</param>
    /// <returns></returns>
    /// <exception cref="InvalidEnumArgumentException"></exception>
    protected SessionOptions CreateSessionOptions(BgiOnnxModel path, bool genCache, ProviderType[]? forcedProvider = null)
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
                        break;
                    case ProviderType.TensorRt:
                        using (var options = new OrtTensorRTProviderOptions())
                        {
                            options.UpdateOptions(GetTrtProviderConfig(genCache ? path.CachePath : null));
                            sessionOptions.AppendExecutionProvider_Tensorrt(options);
                        }

                        break;
                    case ProviderType.Cuda:
                        using (var options = new OrtCUDAProviderOptions())
                        {
                            options.UpdateOptions(GetCudaProviderConfig());
                            sessionOptions.AppendExecutionProvider_CUDA();
                        }

                        break;
                    default:
                        throw new InvalidEnumArgumentException("无效的推理设备");
                }
            }
            catch (Exception e)
            {
                logger.LogError("无法加载指定的 ONNX provider {Provider}，跳过。请检查推理设备配置是否正确。({Err})", Enum.GetName(type),
                    e.Message);
            }

        if (!OptimizedModel) return sessionOptions;
        if (!genCache) return sessionOptions;
        var optPath = Path.Combine(path.CachePath, "optimized");
        if (!Directory.Exists(optPath)) Directory.CreateDirectory(optPath);
        sessionOptions.OptimizedModelFilePath = Path.Combine(optPath, Path.GetFileName(path.ModalPath));
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
            ["trt_ep_context_file_path"] = Global.Absolute(Path.Combine(cacheFolder, "trt")),
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
            Directory.CreateDirectory(result["trt_ep_context_file_path"]);

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
}