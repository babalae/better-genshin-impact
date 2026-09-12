using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public enum OnnxRuntimePluginSourceKind
{
    NuGet,
    BetterGi,
    GitHubRelease
}

public enum OnnxRuntimePluginStatusKind
{
    NotInstalled,
    Downloading,
    Installed,
    UpdateAvailable,
    MissingDependency,
    Incompatible,
    Broken,
    PendingRestart,
    NotPublished
}

public enum InferenceHardwareType
{
    Cpu,
    Gpu,
    Npu
}

/// <summary>
/// 可供用户选择的实际推理设备。StableId 用于在设备顺序变化后恢复选择。
/// </summary>
public sealed record InferenceDeviceDescriptor(
    InferenceDeviceType Provider,
    string StableId,
    string DisplayName,
    InferenceHardwareType HardwareType,
    string Vendor,
    uint DeviceId,
    string ProviderOption,
    bool IsAvailable = true,
    string UnavailableReason = "")
{
    public string ProviderDisplayName => Provider switch
    {
        InferenceDeviceType.GpuDirectMl => "DirectML",
        InferenceDeviceType.OpenVino => "OpenVINO",
        InferenceDeviceType.Cuda => "CUDA",
        _ => "CPU"
    };

    public string FullDisplayName => $"{ProviderDisplayName} / {DisplayName}";
}

/// <summary>
/// 一个可下载的 ONNX Runtime Plugin EP 包。
/// </summary>
public sealed class OnnxRuntimePluginDescriptor
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";
    public string SourceLatestVersion { get; set; } = "";
    public InferenceDeviceType Provider { get; set; }
    public int? CudaMajor { get; set; }
    public string Rid { get; set; } = "win-x64";
    public string MinimumOrtVersion { get; set; } = "1.24.4";
    public string EntryLibrary { get; set; } = "";
    public string EpName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FallbackDownloadUrl { get; set; } = "";
    public string ReleasePageUrl { get; set; } = "";
    public string ChecksumAlgorithm { get; set; } = "SHA256";
    public string Checksum { get; set; } = "";
    public long DownloadSize { get; set; }
    public OnnxRuntimePluginSourceKind Source { get; set; }
    public bool IsPublished { get; set; } = true;
    public string Description { get; set; } = "";
}

public sealed class OnnxRuntimePluginInfo
{
    public required OnnxRuntimePluginDescriptor Descriptor { get; init; }
    public string InstalledVersion { get; init; } = "";
    public OnnxRuntimePluginStatusKind Status { get; init; }
    public string StatusMessage { get; init; } = "";
    public bool CanDownload { get; init; }
    public bool CanUninstall { get; init; }
}

public sealed record PluginDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double Percentage => TotalBytes is > 0
        ? Math.Clamp(BytesReceived * 100d / TotalBytes.Value, 0, 100)
        : 0;
}

public sealed record OnnxRuntimePluginResolution(
    OnnxRuntimePluginDescriptor Descriptor,
    string Version,
    string LibraryPath,
    bool IsPendingVersion);

public sealed class OnnxRuntimePluginCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public List<OnnxRuntimePluginDescriptor> Packages { get; set; } = [];
}
