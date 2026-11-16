using System;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     OpenVINO 后端的生效参数快照，供设置页草稿比较与装配时透传。
///     <see cref="UseOcr" /> / <see cref="UseYolo" /> 是逐用途开关（与 ncnn 后端语义一致），
///     <see cref="DeviceId" /> 是 OpenVINO 设备名（CPU / GPU.0 / NPU…），<see cref="EnableModelCache" /> 启用编译缓存。
/// </summary>
public sealed record OpenVinoBackendSettings(bool UseOcr, bool UseYolo, string DeviceId, bool EnableModelCache)
{
    public const int MaxDeviceIdLength = 64;

    public bool IsValid => !string.IsNullOrWhiteSpace(DeviceId) && DeviceId.Length <= MaxDeviceIdLength;

    /// <summary>只比较是否变化用的稳定签名，不参与验证指纹。</summary>
    public string SignatureSuffix => $"|ovino|{UseOcr}|{UseYolo}|{DeviceId}|{EnableModelCache}";

    /// <summary>返回深拷贝并修正越界值，避免草稿对象与已保存配置共享引用。</summary>
    public OpenVinoBackendSettings Normalize()
    {
        var deviceId = string.IsNullOrWhiteSpace(DeviceId) ? "CPU" : DeviceId.Trim();
        if (deviceId.Length > MaxDeviceIdLength) deviceId = deviceId[..MaxDeviceIdLength];
        return new OpenVinoBackendSettings(UseOcr, UseYolo, deviceId, EnableModelCache);
    }

    public static OpenVinoBackendSettings FromConfig(OpenVinoConfig config)
        => new OpenVinoBackendSettings(config.UseOcr, config.UseYolo, config.DeviceId, config.EnableModelCache).Normalize();

    public bool Equals(OpenVinoBackendSettings? other) => other != null && SignatureSuffix == other.SignatureSuffix;

    public override int GetHashCode() => SignatureSuffix.GetHashCode(StringComparison.Ordinal);
}
