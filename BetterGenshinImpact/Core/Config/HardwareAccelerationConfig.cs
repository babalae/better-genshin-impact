using System;
using System.Linq;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Core.Recognition.ONNX;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
[JsonConverter(typeof(HardwareAccelerationConfigJsonConverter))]
public partial class HardwareAccelerationConfig : ObservableObject
{
    // 缺少新结构标识时不迁移旧参数，由启动准备流程使用 CPU 默认设置。
    [ObservableProperty] private int _formatVersion;
    [ObservableProperty] private InferenceEngineSettings _cpu = new() { DeviceId = "CPU" };
    [ObservableProperty] private PaddleOcrCpuConfig _paddleOcrCpu = new();
    [ObservableProperty] private InferenceEngineSettings _directML = new();
    [ObservableProperty] private InferenceEngineSettings _openVINO = new();
    [ObservableProperty] private InferenceEngineSettings _cuda = new();
    [ObservableProperty] private InferenceEngineSettings _tensorRT = new();
    [ObservableProperty] private InferenceValidationState _validationState;
    [ObservableProperty] private string _verifiedSignature = "";
    // 历史成功记录仅用于设置页展示，不能代替当前设备和参数的验证签名。
    [ObservableProperty] private InferenceDeviceType[] _validatedEngines = [];
    [ObservableProperty] private InferenceDeviceType? _lastFailedEngine;
    [ObservableProperty] private string _lastValidationError = "";

    public InferenceEngineSettings GetEngineSettings(InferenceDeviceType engine) => engine switch
    {
        InferenceDeviceType.Cpu => Cpu,
        InferenceDeviceType.DirectML => DirectML,
        InferenceDeviceType.OpenVINO => OpenVINO,
        InferenceDeviceType.CUDA => Cuda,
        InferenceDeviceType.TensorRT => TensorRT,
        _ => throw new ArgumentOutOfRangeException(nameof(engine))
    };

    public void RecordValidatedEngine(InferenceDeviceType engine)
    {
        if (engine != InferenceDeviceType.Cpu && !ValidatedEngines.Contains(engine))
            ValidatedEngines = [.. ValidatedEngines, engine];
    }

    [JsonIgnore]
    public bool IsExpectedConfiguration => FormatVersion == 1 && Enum.IsDefined(InferenceDevice)
        && Enum.IsDefined(ValidationState) && Cpu != null && DirectML != null && OpenVINO != null
        && Cuda != null && TensorRT != null && Cpu.CpuThreads >= 0
        && Cpu.CpuThreads <= Environment.ProcessorCount
        && !string.IsNullOrWhiteSpace(GetEngineSettings(InferenceDevice).DeviceId);

    /// <summary>配置结构损坏时恢复完整默认值。运行时回退不得调用此方法。</summary>
    public void ResetToDefaults()
    {
        FormatVersion = 1;
        Cpu = new() { DeviceId = "CPU" };
        PaddleOcrCpu = new();
        DirectML = new(); OpenVINO = new(); Cuda = new(); TensorRT = new();
        InferenceDevice = InferenceDeviceType.Cpu;
        CpuOcr = true;
        PreferredBackend = InferenceBackend.Onnx;
        UseNcnnOcr = false;
        UseNcnnYolo = false;
        NcnnComputeMode = NcnnComputeMode.Vulkan;
        NcnnGpuIndex = 0;
        NcnnThreads = NcnnBackendSettings.DefaultThreads;
        NcnnDetTuning = NcnnTuningSettings.CreateDefault();
        NcnnRecTuning = NcnnTuningSettings.CreateDefault();
        NcnnYoloTuning = NcnnTuningSettings.CreateDefault();
        OpenCvThreads = 0;
        OpenCvIdleSleep = false;
        ValidationState = InferenceValidationState.None;
        VerifiedSignature = "";
    }

    /// <summary>只把当前生效选择切换为 CPU，保留其他引擎、OCR 和线程设置。</summary>
    public void SwitchToCpu()
    {
        FormatVersion = 1;
        InferenceDevice = InferenceDeviceType.Cpu;
        Cpu.DeviceId = "CPU";
        ValidationState = InferenceValidationState.None;
        VerifiedSignature = "";
    }
    /// <summary>
    /// 推理使用的设备。默认CPU
    /// </summary>
    [ObservableProperty]
    private InferenceDeviceType _inferenceDevice = InferenceDeviceType.Cpu;

    partial void OnInferenceDeviceChanged(InferenceDeviceType value)
    {
        // 脚本或其他调用方写入历史编号、未知值时，也与配置读取保持一致。
        if (!Enum.IsDefined(value)) InferenceDevice = InferenceDeviceType.Cpu;
    }

    /// <summary>
    /// 是否强制OCR使用CPU推理。在某些环境上使用GPU进行OCR推理会导致性能下降(比如很多使用DirectML推理的情况下)。默认开启。
    /// </summary>
    [ObservableProperty]
    private bool _cpuOcr = true;

    /// <summary>
    ///     全局推理后端选择意图。界面顶部二选一即写入这里；实际生效仍以
    ///     <see cref="UseNcnnOcr" /> / <see cref="UseNcnnYolo" /> 为准，便于单项覆盖。
    /// </summary>
    [ObservableProperty]
    private InferenceBackend _preferredBackend = InferenceBackend.Onnx;

    /// <summary>
    ///     OCR 是否走 ncnn 后端。全局选择 ncnn 时默认开启，可在高级区单独覆盖回 ONNX。
    ///     开启后 Det/Rec 全部走 ncnn，需资产目录内提供 ncnn_shim.dll 与按桶宽转换的模型；
    ///     运行库或产物不可用时直接报错并阻止启动，不再静默回退所选推理引擎。
    /// </summary>
    [ObservableProperty]
    private bool _useNcnnOcr;

    /// <summary>
    ///     YOLO 检测是否走 ncnn 后端。全局选择 ncnn 时默认开启，可在高级区单独覆盖回 ONNX。
    ///     开启后所有检测模型都必须有同名 ncnn 产物；缺失或创建失败时直接报错，
    ///     需要自定义模型时请关闭该开关。
    /// </summary>
    [ObservableProperty]
    private bool _useNcnnYolo;

    /// <summary>
    ///     ncnn 的计算后端：Vulkan 显卡加速或 ncnn 原生 CPU 推理。
    /// </summary>
    [ObservableProperty]
    private NcnnComputeMode _ncnnComputeMode = NcnnComputeMode.Vulkan;

    /// <summary>
    ///     ncnn 使用的 Vulkan 设备索引。0 表示首个 Vulkan 设备（通常是独显）。
    /// </summary>
    [ObservableProperty]
    private int _ncnnGpuIndex;

    /// <summary>
    ///     ncnn 每条会话的 CPU 线程数（ncnn 侧 num_threads，Vulkan 计算仍由 GPU 执行）。
    ///     仅作为旧配置迁移的基线值：新配置的线程数存放在三套 <see cref="NcnnTuningSettings" /> 中。
    /// </summary>
    [ObservableProperty]
    private int _ncnnThreads = NcnnBackendSettings.DefaultThreads;

    /// <summary>OCR 文本检测的 ncnn 调优参数。</summary>
    [ObservableProperty]
    private NcnnTuningSettings _ncnnDetTuning = NcnnTuningSettings.CreateDefault();

    /// <summary>OCR 文字识别的 ncnn 调优参数。</summary>
    [ObservableProperty]
    private NcnnTuningSettings _ncnnRecTuning = NcnnTuningSettings.CreateDefault();

    /// <summary>YOLO 检测 / 分类的 ncnn 调优参数。</summary>
    [ObservableProperty]
    private NcnnTuningSettings _ncnnYoloTuning = NcnnTuningSettings.CreateDefault();

    /// <summary>
    ///     OpenCV 并行线程数。0 = 自动（全部逻辑核）。限制后可减少与游戏的抢核与线程池开销。
    /// </summary>
    [ObservableProperty]
    private int _openCvThreads;

    /// <summary>
    ///     OpenCV 并行池空闲时立即睡眠而非自旋等待。识别任务多为短并行区域，
    ///     自旋等待在游戏占核时会白烧 CPU（实测单线程可达 40s+/分钟级会话）。
    /// </summary>
    [ObservableProperty]
    private bool _openCvIdleSleep;

    #region 已移除参数的源注释（不读取、不保存，也不参与新推理设置）
    // 强制指定gpu设备,默认为0(使用默认设备)
    // 附加path，用;分割。默认为空。
    // 是否输出优化后的模型文件到缓存。注意:在不支持的执行器上使用会导致异常。默认关闭。
    // 强制指定cuda设备,默认为0(使用默认设备)
    // 自动附加cuda的path。一般情况下用这个就足够了。默认关闭。
    // 启用TensorRT缓存。默认开启。不开的话使用TensorRT每次加载模型会卡爆。
    // 嵌入式引擎缓存。将引擎缓存嵌入到模型中。默认开启。关闭它可能会提高性能(如果不爆炸的话)。
    // OpenVino 设备参数。
    // 启用 OpenVINO 缓存。默认关闭。
    #endregion
}
