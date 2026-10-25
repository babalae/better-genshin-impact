using System;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn 后端的启动准备：只修正越界值并按需对齐 shim 默认值，不重置用户开关。
///     从 <see cref="ONNX.InferenceRuntimeService" /> 独立，ncnn 的启动细节不再侵入 ONNX 运行时服务。
/// </summary>
public static class NcnnStartup
{
    /// <summary>
    ///     读取当前生效的 ncnn 配置；宿主环境（如单元测试）尚未初始化全局配置时返回 null，
    ///     OCR / YOLO 的 ncnn 分流按关闭处理，保持 ONNX-only 路径可用。
    /// </summary>
    public static NcnnConfig? TryGetActiveConfig()
    {
        try
        {
            return BetterGenshinImpact.GameTask.TaskContext.Instance().Config?.HardwareAccelerationConfig?.Ncnn;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     规范 ncnn 配置（enum 越界回落、调优参数空值补齐与 Normalize 写回）。
    ///     返回用户可读的问题提示（空串表示通过），由启动流程合并展示；只提示、不修复开关。
    /// </summary>
    public static string Prepare(NcnnConfig config)
    {
        // 调优参数被脚本等路径写空时补默认，线程基线沿用历史单一字段。
        config.DetTuning ??= NcnnTuningSettings.CreateDefault(config.Threads);
        config.RecTuning ??= NcnnTuningSettings.CreateDefault(config.Threads);
        config.YoloTuning ??= NcnnTuningSettings.CreateDefault(config.Threads);
        // 只在确实使用 ncnn 时校验运行库并读取 shim 的真实默认值：
        // 内置常量与 ncnn 版本漂移会改变着色器选择（bf16 packed 在部分设备上直接导致 Vulkan 崩溃）。
        var fallbackMessage = "";
        if (config.UseOcr || config.UseYolo)
        {
            var problem = ValidateRuntime();
            if (problem.Length > 0)
            {
                // 只提示、不改写用户开关：让用户进设置页自行修正（关掉 ncnn 或补齐产物）。
                fallbackMessage = problem;
            }
            else
            {
                try
                {
                    var shimDefaults = NcnnTuningSettings.FromNetOptions(
                        NcnnShim.QueryDefaultOptions(), config.Threads);
                    config.DetTuning.AlignWithDefaults(shimDefaults);
                    config.RecTuning.AlignWithDefaults(shimDefaults);
                    config.YoloTuning.AlignWithDefaults(shimDefaults);
                }
                catch (Exception)
                {
                    // 默认值对齐失败只影响内置常量的校正，不影响推理正确性，忽略即可。
                }
            }
        }
        var normalized = NcnnBackendSettings.FromConfig(config);
        config.ComputeMode = normalized.ComputeMode;
        config.GpuIndex = normalized.GpuIndex;
        config.Threads = normalized.Det.Threads;
        config.DetTuning = normalized.Det;
        config.RecTuning = normalized.Rec;
        config.YoloTuning = normalized.Yolo;
        return fallbackMessage;
    }

    /// <summary>
    ///     校验 ncnn 运行库是否可用；有问题时返回可直接展示给用户的错误，空字符串表示通过。
    ///     这里只检查不修复：ncnn 开关与模型产物都由用户在设置页决定。
    /// </summary>
    public static string ValidateRuntime()
    {
        if (!NcnnShim.IsSupported())
            return "ncnn 运行库缺失或版本过旧（缺少 shim_create_ex / shim_infer_ex）。\r\n\r\n"
                   + "运行库由 NuGet 包 BetterGI.NcnnShim 随构建部署；"
                   + "请在“硬件加速设置的 ncnn 卡片中”关闭 OCR / YOLO 的 ncnn 开关，或更新该包。";
        return "";
    }
}
