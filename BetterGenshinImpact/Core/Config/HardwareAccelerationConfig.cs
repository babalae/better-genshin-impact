using System;
using BetterGenshinImpact.Core.Recognition.ONNX;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class HardwareAccelerationConfig : ObservableObject
{
    public enum CudaRuntimeMajor
    {
        Auto = 0,
        Cuda12 = 12,
        Cuda13 = 13
    }

    /// <summary>
    /// 推理使用的设备。默认CPU
    /// </summary>
    [ObservableProperty]
    private InferenceDeviceType _inferenceDevice = InferenceDeviceType.Cpu;

    /// <summary>
    /// 是否强制OCR使用CPU推理。在某些环境上使用GPU进行OCR推理会导致性能下降(比如很多使用DirectML推理的情况下)。默认开启。
    /// </summary>
    [ObservableProperty]
    private bool _cpuOcr = true;

    #region 一般GPU加速设置

    /// <summary>
    /// 强制指定gpu设备,默认为0(使用默认设备)
    /// </summary>
    [ObservableProperty]
    private int _gpuDevice = 0;

    /// <summary>
    /// DirectML 适配器 LUID。优先使用稳定的 LUID 恢复设备，无法匹配时回退到设备编号。
    /// </summary>
    [ObservableProperty]
    private string _directMlAdapterLuid = "";

    /// <summary>
    /// 附加path，用;分割。默认为空。
    /// </summary>
    [ObservableProperty]
    private string _additionalPath = "";

    /// <summary>
    /// 是否输出优化后的模型文件到缓存。注意:在不支持的执行器上使用会导致异常。默认关闭。
    /// </summary>
    [ObservableProperty]
    private bool _optimizedModel = false;

    #endregion

    #region cuda设置

    /// <summary>
    /// 强制指定cuda设备,默认为0(使用默认设备)
    /// </summary>
    [ObservableProperty]
    private int _cudaDevice = 0;

    /// <summary>
    /// CUDA GPU UUID。设备顺序变化时用于重新解析 device_id。
    /// </summary>
    [ObservableProperty]
    private string _cudaDeviceUuid = "";

    /// <summary>
    /// CUDA Plugin EP 主版本。Auto 会选择依赖可用的最高已安装版本。
    /// </summary>
    [ObservableProperty]
    private CudaRuntimeMajor _cudaRuntime = CudaRuntimeMajor.Auto;

    /// <summary>
    /// 自动附加cuda的path。一般情况下用这个就足够了。默认关闭。
    /// </summary>
    [ObservableProperty]
    private bool _autoAppendCudaPath = false;

    #endregion

    #region TensorRT缓存设置

    /// <summary>
    /// 启用TensorRT缓存。默认开启。不开的话使用TensorRT每次加载模型会卡爆。
    /// </summary>
    [ObservableProperty]
    private bool _enableTensorRtCache = true;

    /// <summary>
    /// 嵌入式引擎缓存。将引擎缓存嵌入到模型中。默认开启。关闭它可能会提高性能(如果不爆炸的话)。
    /// </summary>
    [ObservableProperty]
    private bool _embedTensorRtCache = true;

    #endregion

    #region OpenVINO设置
    /// <summary>
    /// OpenVino 设备参数。
    /// </summary>
    [ObservableProperty]
    private string _openVinoDevice = "AUTO";
    
    /// <summary>
    /// 启用 OpenVINO 缓存。默认关闭。
    /// </summary>
    [ObservableProperty]
    private bool _enableOpenVinoCache = false;

    #endregion

    /// <summary>
    /// 创建用于设置界面编辑和推理测试的独立副本，避免尚未验证的配置提前写入磁盘。
    /// </summary>
    public HardwareAccelerationConfig Clone()
    {
        var result = new HardwareAccelerationConfig();
        result.CopyFrom(this);
        return result;
    }

    /// <summary>
    /// 从已经验证的设置副本中复制全部字段。
    /// </summary>
    public void CopyFrom(HardwareAccelerationConfig source)
    {
        InferenceDevice = source.InferenceDevice;
        CpuOcr = source.CpuOcr;
        GpuDevice = source.GpuDevice;
        DirectMlAdapterLuid = source.DirectMlAdapterLuid;
        AdditionalPath = source.AdditionalPath;
        OptimizedModel = source.OptimizedModel;
        CudaDevice = source.CudaDevice;
        CudaDeviceUuid = source.CudaDeviceUuid;
        CudaRuntime = source.CudaRuntime;
        AutoAppendCudaPath = source.AutoAppendCudaPath;
        EnableTensorRtCache = source.EnableTensorRtCache;
        EmbedTensorRtCache = source.EmbedTensorRtCache;
        OpenVinoDevice = source.OpenVinoDevice;
        EnableOpenVinoCache = source.EnableOpenVinoCache;
    }

    /// <summary>
    /// 将旧版“自动 GPU”迁移为 DirectML。旧逻辑在正常 Windows 环境下会优先命中内置 DML。
    /// </summary>
    public void MigrateLegacyConfig()
    {
        if (InferenceDevice == InferenceDeviceType.LegacyAutoGpu)
        {
            InferenceDevice = InferenceDeviceType.GpuDirectMl;
        }
    }
}
