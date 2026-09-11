using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.Service.Interface;

public interface IOnnxRuntimePluginManager
{
    string StorageRoot { get; }
    bool IsStorageWritable { get; }
    string StorageError { get; }

    IReadOnlyList<OnnxRuntimePluginInfo> GetCurrentPackages();
    Task<IReadOnlyList<OnnxRuntimePluginInfo>> RefreshCatalogAsync(CancellationToken cancellationToken = default);
    Task InstallAsync(OnnxRuntimePluginDescriptor descriptor, IProgress<PluginDownloadProgress>? progress,
        CancellationToken cancellationToken = default);
    Task UninstallAsync(string pluginId, CancellationToken cancellationToken = default);
    IReadOnlyList<OnnxRuntimePluginResolution> ResolveForStartup(InferenceDeviceType provider,
        HardwareAccelerationConfig.CudaRuntimeMajor cudaRuntime);
    void MarkActivationSucceeded(OnnxRuntimePluginResolution resolution);
    string GetPluginDirectory(string pluginId);
}
