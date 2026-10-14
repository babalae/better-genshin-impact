using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiOnnxFactory
{
    private readonly ILogger<BgiOnnxFactory> _logger;
    private readonly ProviderType[] _providerTypes;
    private readonly string? _profilingDirectory;
    private readonly InferenceRuntimeService? _runtime;
    private readonly InferenceRuntimeSnapshot? _snapshot;

    /// <summary>
    ///     缓存模型路径对应的目录；会话始终加载原始模型，不再使用嵌入缓存文件。
    ///     <br />
    ///     这样能避免并发加载模型问题。普通缓存目录按模型内容、设备与设置隔离。
    /// </summary>
    private readonly ConcurrentDictionary<BgiOnnxModel, string?> _cachedModelPaths = new();


    /// <summary>
    /// 请勿直接实例化此类
    /// </summary>
    /// <param name="logger"></param>
    /// <remarks>主程序从运行时读取生效配置，每个新会话单独冻结快照；未指定快照的工厂用于 CPU 单元测试。</remarks>
    public BgiOnnxFactory(ILogger<BgiOnnxFactory> logger)
        : this(logger, InferenceRuntimeSnapshot.CpuDefault())
    {
        // 未指定设置的临时工厂使用 CPU；主程序 DI 注入运行时，每次创建会话冻结当时的生效快照。
    }

    public BgiOnnxFactory(ILogger<BgiOnnxFactory> logger, InferenceRuntimeService runtime)
    {
        _logger = logger;
        _runtime = runtime;
        _providerTypes = [];
    }

    public BgiOnnxFactory(ILogger<BgiOnnxFactory> logger, InferenceRuntimeSnapshot snapshot, string? profilingDirectory = null)
    {
        _logger = logger;
        _profilingDirectory = profilingDirectory;
        _snapshot = snapshot;
        _providerTypes = GetProviderType(snapshot.Engine);
        _logger.LogInformation("[ONNX]当前引擎 {Engine}，设备 {Device}，OCR 强制 CPU：{CpuOcr}",
            snapshot.Engine, snapshot.Device.Name, snapshot.CpuOcr);
    }

    public InferenceRuntimeSnapshot Snapshot => _runtime?.Current ?? _snapshot!;

    /// <summary>一次模型操作固定使用同一个快照，等待或延后回退不能改变它的参数。</summary>
    public BgiOnnxFactory CaptureSnapshot() => _runtime == null ? this : new(_logger, Snapshot);

    // OCR 验证使用独立工厂与会话，必须实际执行所选 provider；不改变业务 OCR 的 CPU 设置。
    internal BgiOnnxFactory ForOcrValidation(string profilingDirectory)
        => new(_logger, Snapshot with { CpuOcr = false }, profilingDirectory);

    public ProviderType[] ProviderTypes => _runtime == null ? (ProviderType[])_providerTypes.Clone() : GetProviderType(Snapshot.Engine);
    public int DmlDeviceId => Snapshot.Device.Index;
    public int CudaDeviceId => Snapshot.Device.Index;
    public string OpenVinoDevice => Snapshot.Device.ProviderDevice;
    public bool EnableCache => Snapshot.EnableModelCache;
    public bool CpuOcr => Snapshot.CpuOcr;
    public bool OpenVinoCache => Snapshot.EnableModelCache;


    /// <summary>
    ///     根据InferenceDeviceType选择Provider
    /// </summary>
    /// <param name="inferenceDeviceType">InferenceDeviceType</param>
    /// <returns></returns>
    /// <remarks>历史上的自动 GPU 选项已移除，无法识别的设置回退到 CPU。</remarks>
    private ProviderType[] GetProviderType(InferenceDeviceType inferenceDeviceType)
    {
        // 无论如何都要加入 CPU，一些计算在纯 GPU 上不被支持或性能很差。
        switch (inferenceDeviceType)
        {
            case InferenceDeviceType.Cpu:
                return [ProviderType.Cpu];
            case InferenceDeviceType.DirectML:
                //只用dml不加cpu的话在很多场景下性能很差。
                // DirectML 的性能和稳定性取决于模型与设备，现在由用户独立选择，不与 TensorRT、CUDA 自动比较。
                return [ProviderType.Dml, ProviderType.Cpu];
            case InferenceDeviceType.OpenVINO:
                // OpenVino是英特尔的OpenVINO执行提供程序
                // 目前来看比Dml强
                // provider 注册统一在 CreateSessionOptions 中完成，避免重复创建原生选项探测。
                return [ProviderType.OpenVino, ProviderType.Cpu];
            case InferenceDeviceType.CUDA:
                // CUDA 的效果依赖具体模型，不再受旧自动选择策略中的低优先级限制。
                return [ProviderType.Cuda, ProviderType.Cpu];
            case InferenceDeviceType.TensorRT:
                // TensorRT 使用配置的设备编号，性能收益需要按模型和设备判断。
                // TensorRT 独立注册，未接管的算子由 CPU 兜底。
                return [ProviderType.TensorRt, ProviderType.Cpu];
            default:
                _logger.LogWarning("[ONNX]无法识别推理设备设置 {Device}，回退到 CPU", inferenceDeviceType);
                return [ProviderType.Cpu];
        }
    }

    /// <summary>
    ///     根据模型创建一个YoloPredictor
    /// </summary>
    /// <param name="model">模型</param>
    /// <returns>BgiYoloPredictor</returns>
    public BgiYoloPredictor CreateYoloPredictor(BgiOnnxModel model, string? profilingPrefix = null)
    {
        // 延后验证只影响后续新建会话，不修改或释放已经运行中的会话。
        if (_runtime != null) return new BgiOnnxFactory(_logger, Snapshot).CreateYoloPredictor(model, profilingPrefix);
        // logger.LogDebug("[Yolo]创建yolo预测器，模型: {ModelName}", model.Name);
        var options = CreateSessionOptions(model, EnableCache);
        try
        {
            if (profilingPrefix != null)
            {
                ConfigureProfiling(options, profilingPrefix);
            }
            // 延迟创建会话，配置的所有权转交给预测器。
            return new BgiYoloPredictor(model, model.ModalPath, options);
        }
        catch
        {
            options.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     根据模型创建一个onnx运行时的InferenceSession
    /// </summary>
    /// <param name="model">模型</param>
    /// <param name="ocr">是否是用于ocr的模型，默认false</param>
    /// <returns>InferenceSession</returns>
    public InferenceSession CreateInferenceSession(BgiOnnxModel model, bool ocr = false)
    {
        if (_runtime != null) return new BgiOnnxFactory(_logger, Snapshot).CreateInferenceSession(model, ocr);
        _logger.LogDebug("[ONNX]创建推理会话，模型: {ModelName}", model.Name);
        ProviderType[]? providerTypes = null;
        if (CpuOcr && ocr) providerTypes = [ProviderType.Cpu];

        using var options = CreateSessionOptions(model, EnableCache, providerTypes);
        if (_profilingDirectory != null)
        {
            ConfigureProfiling(options, Path.Combine(_profilingDirectory, model.Name));
        }
        return new InferenceSession(model.ModalPath, options);
    }

    /// <summary>启用 ONNX Runtime 性能记录，并将文件写入指定前缀。</summary>
    internal static void ConfigureProfiling(SessionOptions options, string profileOutputPathPrefix)
    {
        // EnableProfiling 的 setter 会立即把当时的 ProfileOutputPathPrefix 传给原生 ORT，顺序不能交换。
        options.ProfileOutputPathPrefix = profileOutputPathPrefix;
        options.EnableProfiling = true;
    }

    /// <summary>
    ///     获取普通缓存目录（TensorRT / OpenVINO）
    /// </summary>
    /// <param name="model">模型</param>
    /// <param name="forcedProvider">强制使用的 providerTypes</param>
    /// <returns>缓存目录绝对路径，null 表示当前 provider 不需要缓存</returns>
    private string? GetCached(BgiOnnxModel model, ProviderType[]? forcedProvider = null)
    {
        var providers = forcedProvider ?? ProviderTypes;
        if (!providers.Contains(ProviderType.TensorRt) && !providers.Contains(ProviderType.OpenVino)) return null;
        // 判断文件是否存在：这里只缓存目录，原始模型始终保持不变，不加载旧 _ctx.onnx。
        return _cachedModelPaths.GetOrAdd(model, _ => Snapshot.GetCachePath(model));
    }

    /// <summary>
    ///     通过模型路径生成SessionOptions <br />
    ///     会话始终加载原始模型，缓存只用于加速原生 provider 的初始化。
    /// </summary>
    /// <param name="model">模型路径</param>
    /// <param name="genCache">是否启用当前引擎的普通缓存。</param>
    /// <param name="forcedProvider">强制使用的Provider,为空或null则不强制</param>
    /// <returns></returns>
    /// <exception cref="InvalidEnumArgumentException"></exception>
    private SessionOptions CreateSessionOptions(BgiOnnxModel model, bool genCache,
        ProviderType[]? forcedProvider = null)
    {
        var sessionOptions = new SessionOptions();
        try
        {
            var cache = genCache ? GetCached(model, forcedProvider) : null;
            foreach (var type in forcedProvider is { Length: > 0 } ? forcedProvider : ProviderTypes)
            {
                switch (type)
                {
                    case ProviderType.Dml:
                        // DirectML 执行提供程序不支持在 onnxruntime 中使用内存模式优化或并行执行。在创建 InferenceSession 期间提供会话选项时，必须禁用这些选项，否则将返回错误。
                        sessionOptions.EnableMemoryPattern = false;
                        sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                        sessionOptions.AppendExecutionProvider_DML(DmlDeviceId);
                        break;
                    case ProviderType.Cpu:
                        sessionOptions.IntraOpNumThreads = Snapshot.CpuThreads;
                        sessionOptions.AppendExecutionProvider_CPU();
                        // if (model.Name.Contains("PpOcr") || model.Name.Contains("Yap"))
                        // {
                        //     sessionOptions.IntraOpNumThreads = 2;  // 限制算子内部并行线程数
                        //     sessionOptions.InterOpNumThreads = 1;  // 限制算子间并行线程数（顺序执行）
                        // }
                        break;
                    case ProviderType.OpenVino:
                        sessionOptions.AppendExecutionProvider("OpenVINO", GetOpenVinoProviderConfig(cache));
                        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL;
                        break;
                    case ProviderType.TensorRt:
                        using (var options = new OrtTensorRTProviderOptions())
                        {
                            options.UpdateOptions(GetTrtProviderConfig(cache));
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
            return sessionOptions;
        }
        catch
        {
            // 目标 provider 初始化失败必须向上报告，不能跳过后用 CPU 假装验证成功。
            sessionOptions.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     获取TensorRT的配置
    /// </summary>
    /// <param name="cacheFolder">缓存生成的目录</param>
    /// <returns>trt配置</returns>
    private Dictionary<string, string> GetTrtProviderConfig(string? cacheFolder)
    {
        var result = new Dictionary<string, string> { ["device_id"] = CudaDeviceId.ToString() };
        if (cacheFolder is null)
        {
            // 不使用缓存目录
            return result;
        }
        // 如果不存在就创建目录。使用普通 engine/timing 缓存，不导出或嵌入优化模型。
        var engine = Path.Combine(cacheFolder, "trt");
        var timing = Path.Combine(cacheFolder, "trt_timing");
        Directory.CreateDirectory(engine);
        Directory.CreateDirectory(timing);
        result["trt_engine_cache_enable"] = "1";
        result["trt_engine_cache_path"] = engine;
        result["trt_timing_cache_enable"] = "1";
        result["trt_timing_cache_path"] = timing;
        // ["trt_force_timing_cache"] = "1", // 不跨设备强制复用 timing cache。
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

        return result;
    }

    #region 历史路径探测说明（仅保留注释，依赖由独立安装目录管理）
    // 获取 硬件加速配置
    // 为了单元测试
    // 直接使用配置
    // 如果配置获取失败，使用默认配置
    //     自动嗅探并修改path以加载cuda
    // 例如: CUDNN\v9.8\lib\12.8\x64
    // 获取所有可能包含CUDA/cuDNN路径的环境变量
    // 环境变量下层文件夹
    // lib路径
    // cuda的版本
    // 体系架构
    //构建完了需要搜索的路径，去重。
    //确定路径是否真的存在
    //确定需要的文件是否存在
    //去重
    //     将附加的path应用进来
    // <param name="extraPath">附加的path字符串</param>
    #endregion
}
