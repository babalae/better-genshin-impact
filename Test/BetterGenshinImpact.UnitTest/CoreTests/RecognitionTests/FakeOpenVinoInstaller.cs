using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Recognition.OpenVino;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
///     IOpenVinoRuntimeInstaller 的空实现，供 HardwareAccelerationViewModel 相关测试构造使用。
/// </summary>
internal sealed class FakeOpenVinoInstaller : IOpenVinoRuntimeInstaller
{
    public string RuntimeDirectory => "test-openvino-plugins";

    public RuntimeInstallation Inspect()
        => new(InferenceInstallationState.Installed, "test", "内置");

    public Task<IReadOnlyList<RuntimeDownloadPackage>> GetDownloadPlanAsync(CancellationToken token)
        => Task.FromResult<IReadOnlyList<RuntimeDownloadPackage>>([]);

    public Task InstallAsync(IReadOnlyList<RuntimeDownloadPackage> packages,
        IProgress<RuntimeDownloadProgress>? progress, CancellationToken token) => Task.CompletedTask;

    public Task CleanupInstalledDownloadsAsync(CancellationToken token) => Task.CompletedTask;
}
