using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     OpenVINO 后端的生效参数快照，供设置页草稿比较与装配时透传。
///     <see cref="UseOcr" /> / <see cref="UseYolo" /> 是逐用途开关（与 ncnn 后端语义一致），
///     <see cref="DeviceId" /> 是 OpenVINO 设备名（CPU / GPU.0 / NPU…），<see cref="EnableModelCache" /> 启用编译缓存。
///     <see cref="NpuRecWidth" /> / <see cref="NpuDetDevice" /> 是 NPU 静态形状策略参数（见 <see cref="IsNpuDevice" />）；
///     <see cref="GpuRecWidthBuckets" /> 是 GPU 识别宽度桶（见 <see cref="IsGpuDevice" />）。
///     相等性直接使用 record 值比较（字段均为值类型或字符串；宽度桶以规范化字符串存储，避免数组引用比较）。
/// </summary>
public sealed record OpenVinoBackendSettings(
    bool UseOcr,
    bool UseYolo,
    string DeviceId,
    bool EnableModelCache,
    // 默认值须与下方 DefaultNpuRecWidth / DefaultNpuDetDevice / DefaultGpuRecWidthBuckets 常量保持一致
    // （主构造函数默认值不能引用体内常量）。
    int NpuRecWidth = 320,
    string NpuDetDevice = "CPU",
    string GpuRecWidthBuckets = "320,640,960")
{
    public const int MaxDeviceIdLength = 64;

    /// <summary>NPU 识别固定宽度的出厂值（与 Intel PP-OCRv6 NPU 示例一致）。</summary>
    public const int DefaultNpuRecWidth = 320;

    /// <summary>静态识别宽度的合法区间（NPU 固定宽度与 GPU 宽度桶共用）：过小识别不全，过大浪费算力。</summary>
    public const int MinStaticRecWidth = 32;
    public const int MaxStaticRecWidth = 1024;

    /// <summary>NPU 时检测模型的回落设备出厂值（NPU 不支持 det 的动态形状）。</summary>
    public const string DefaultNpuDetDevice = "CPU";

    /// <summary>
    ///     GPU 识别宽度桶的出厂值。GPU 动态形状是预览特性：每个新宽度首次推理触发秒级 kernel 编译，
    ///     改为少量静态宽度桶（按行宽选最小够用桶补零）后一次编译永久复用。空串表示保持动态宽度。
    /// </summary>
    public const string DefaultGpuRecWidthBuckets = "320,640,960";

    public bool IsValid => !string.IsNullOrWhiteSpace(DeviceId) && DeviceId.Length <= MaxDeviceIdLength;

    /// <summary>设备名是否为 NPU（NPU 插件的设备名以 NPU 开头，与设备枚举的过滤规则一致）。</summary>
    public static bool IsNpuDevice(string? deviceId)
        => deviceId != null && deviceId.StartsWith("NPU", StringComparison.Ordinal);

    /// <summary>设备名是否为 GPU（GPU 插件的设备名以 GPU 开头，与设备枚举的过滤规则一致）。</summary>
    public static bool IsGpuDevice(string? deviceId)
        => deviceId != null && deviceId.StartsWith("GPU", StringComparison.Ordinal);

    /// <summary>返回深拷贝并修正越界值，避免草稿对象与已保存配置共享引用。</summary>
    public OpenVinoBackendSettings Normalize()
    {
        var deviceId = NormalizeDeviceId(DeviceId);
        // NPU 固定宽度越界回落出厂值；回落设备空值补 CPU（与 DeviceId 的兜底一致）。
        var recWidth = NpuRecWidth is >= MinStaticRecWidth and <= MaxStaticRecWidth ? NpuRecWidth : DefaultNpuRecWidth;
        var detDevice = NormalizeDeviceId(NpuDetDevice);
        return new OpenVinoBackendSettings(UseOcr, UseYolo, deviceId, EnableModelCache, recWidth, detDevice,
            NormalizeGpuBuckets(GpuRecWidthBuckets));
    }

    /// <summary>
    ///     宽度桶规范化：逗号分隔的整数去重、升序、夹取到合法区间后重新拼接（保证草稿比较的字符串稳定）。
    ///     空串保留为空（显式关闭静态桶、保持动态宽度）；非空但无合法值时回落出厂桶。
    /// </summary>
    private static string NormalizeGpuBuckets(string? buckets)
    {
        if (string.IsNullOrWhiteSpace(buckets)) return "";
        var widths = ParseTokens(buckets).Distinct().OrderBy(w => w).ToArray();
        return widths.Length > 0 ? string.Join(",", widths) : DefaultGpuRecWidthBuckets;
    }

    /// <summary>解析已规范化的宽度桶字符串为升序数组（装配时用；空串返回空数组表示动态宽度）。</summary>
    public static int[] ParseGpuRecWidthBuckets(string? buckets)
        => string.IsNullOrWhiteSpace(buckets) ? [] : ParseTokens(buckets).Distinct().OrderBy(w => w).ToArray();

    private static IEnumerable<int> ParseTokens(string buckets)
        => buckets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, out var width) ? width : -1)
            .Where(width => width is >= MinStaticRecWidth and <= MaxStaticRecWidth);

    /// <summary>设备名规范化：空值回落 CPU、去首尾空白、超长截断。</summary>
    private static string NormalizeDeviceId(string? deviceId)
    {
        var trimmed = string.IsNullOrWhiteSpace(deviceId) ? "CPU" : deviceId.Trim();
        return trimmed.Length > MaxDeviceIdLength ? trimmed[..MaxDeviceIdLength] : trimmed;
    }

    public static OpenVinoBackendSettings FromConfig(OpenVinoConfig config)
        => new OpenVinoBackendSettings(config.UseOcr, config.UseYolo, config.DeviceId, config.EnableModelCache,
            config.NpuRecWidth, config.NpuDetDevice, config.GpuRecWidthBuckets).Normalize();
}