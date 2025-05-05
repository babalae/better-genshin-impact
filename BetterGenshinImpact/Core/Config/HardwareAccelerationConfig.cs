using System;
using BetterGenshinImpact.Core.Recognition.ONNX;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

[Serializable]
public partial class HardwareAccelerationConfig : ObservableObject
{
    /// <summary>
    /// 推理使用的设备。默认CPU
    /// </summary>
    [ObservableProperty]
    private InferenceDeviceType _inferenceDevice = InferenceDeviceType.Cpu;

    /// <summary>
    /// 是否强制OCR使用CPU推理。在某些环境上使用GPU进行OCR推理会导致性能下降(比如很多使用DirectML推理的情况下)。默认开。
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
    /// 自动附加cuda的path。一般情况下用这个就足够了。默认开启。
    /// </summary>
    [ObservableProperty]
    private bool _autoAppendCudaPath = true;

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
}