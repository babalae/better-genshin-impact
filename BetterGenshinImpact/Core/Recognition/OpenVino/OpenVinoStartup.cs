using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

/// <summary>
///     OpenVINO（C# API）后端的启动准备：只规范配置并校验运行库，不重置用户开关。
///     从 <see cref="ONNX.InferenceRuntimeService" /> 独立，后端启动细节不侵入 ONNX 运行时服务。
/// </summary>
public static class OpenVinoStartup
{
    /// <summary>
    ///     读取当前生效的 OpenVINO 配置；宿主环境（如单元测试）尚未初始化全局配置时返回 null，
    ///     OCR / YOLO 的 OpenVINO 分流按关闭处理，保持 ONNX-only 路径可用。
    /// </summary>
    public static OpenVinoConfig? TryGetActiveConfig()
    {
        try
        {
            return Service.ConfigService.Config?.HardwareAccelerationConfig?.OpenVino;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>规范 OpenVINO 配置（设备名越界回落、空值补齐）。返回用户可读的问题提示（空串表示通过）。</summary>
    public static string Prepare(OpenVinoConfig config)
    {
        var settings = OpenVinoBackendSettings.FromConfig(config);
        if (!settings.IsValid) settings = settings with { DeviceId = "CPU" };
        config.DeviceId = settings.DeviceId;
        return ValidateRuntime();
    }

    /// <summary>
    ///     校验 OpenVINO 运行库是否可用；有问题时返回可直接展示给用户的错误，空字符串表示通过。
    ///     这里只检查不修复：用途开关与设备由用户在设置页决定。
    /// </summary>
    public static string ValidateRuntime()
    {
        var missing = OpenVinoRuntime.FirstMissingFile();
        if (missing != null)
            return $"OpenVINO 运行库缺失（{missing}）。\r\n\r\n"
                   + "请在硬件加速设置的 OpenVINO 卡片中下载运行库，或关闭对应用途的 OpenVINO 开关。";
        return "";
    }

    /// <summary>枚举可用推理设备（CPU / GPU / NPU），失败时返回空列表并记录日志。</summary>
    public static IReadOnlyList<OpenVinoDeviceInfo> DiscoverDevices(ILogger? logger)
    {
        try
        {
            return OpenVinoDeviceDiscovery.GetDevices(OpenVinoRuntime.RuntimeDirectory);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "[OpenVINO] 设备枚举失败");
            return [];
        }
    }
}

/// <summary>OpenVINO 设备信息（设置页展示与保存）。</summary>
public sealed record OpenVinoDeviceInfo(string DeviceId, string DisplayName);
