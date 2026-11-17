using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages.View;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
///     硬件加速设置页 ViewModel 的后端选择逻辑测试：三后端卡片、切换默认开关、
///     草稿合成与 Apply 落盘路由。不打开窗口、不做真实推理。
/// </summary>
public class HardwareAccelerationViewModelTests
{
    [Fact]
    public void BackendOptions_ContainsThreeBackends()
    {
        using var vm = CreateViewModel();
        Assert.Equal(3, vm.BackendOptions.Count);
        Assert.Contains(vm.BackendOptions, o => o.Backend == InferenceBackend.Onnx);
        Assert.Contains(vm.BackendOptions, o => o.Backend == InferenceBackend.OpenVino);
        Assert.Contains(vm.BackendOptions, o => o.Backend == InferenceBackend.Ncnn);
        Assert.Equal(InferenceBackend.Onnx, vm.SelectedBackendDraft);
        Assert.True(vm.IsOnnxBackend);
    }

    [Fact]
    public void SwitchBackend_UpdatesSubVmPurposeDefaults()
    {
        using var vm = CreateViewModel();
        vm.SelectedBackendDraft = InferenceBackend.Ncnn;
        Assert.True(vm.IsNcnnBackend);
        Assert.True(vm.NcnnSettings.UseNcnnOcr);
        Assert.True(vm.NcnnSettings.UseNcnnYolo);
        Assert.False(vm.OpenVinoSettings.UseOpenVinoOcr);

        vm.SelectedBackendDraft = InferenceBackend.OpenVino;
        Assert.True(vm.IsOpenVinoBackend);
        Assert.True(vm.OpenVinoSettings.UseOpenVinoOcr);
        Assert.True(vm.OpenVinoSettings.UseOpenVinoYolo);
        Assert.False(vm.NcnnSettings.UseNcnnOcr);

        vm.SelectedBackendDraft = InferenceBackend.Onnx;
        Assert.True(vm.IsOnnxBackend);
        Assert.False(vm.NcnnSettings.UseNcnnOcr);
        Assert.False(vm.OpenVinoSettings.UseOpenVinoYolo);
    }

    [Fact]
    public void PurposeOverrides_ProduceDrafts_RouterConsumes()
    {
        // 全局 ONNX + 单独开启 YOLO 走 OpenVINO：草稿与路由解析一致
        using var vm = CreateViewModel();
        vm.OpenVinoSettings.UseOpenVinoYolo = true;
        var draft = vm.OpenVinoSettings.BuildDraft();
        Assert.True(draft.UseYolo);
        Assert.False(draft.UseOcr);
        Assert.False(vm.NcnnSettings.HasDraftChanges);
        Assert.True(vm.OpenVinoSettings.HasDraftChanges);
    }

    [Fact]
    public async Task ApplyAsync_PersistsBackendSettingsAndIntent()
    {
        var config = new HardwareAccelerationConfig();
        var store = new TestConfigService(config);
        using var runtime = CreateRuntime(config, store);
        // 设置页在运行时发布后才可交互；Apply → SettingsChanged 会读取 Current。
        await runtime.InitializeAsync();
        using var vm = CreateViewModel(config, store, runtime);

        vm.SelectedBackendDraft = InferenceBackend.OpenVino;
        vm.OpenVinoSettings.UseOpenVinoYolo = true;
        vm.OpenVinoSettings.DeviceId = "GPU.0";
        vm.OpenVinoSettings.EnableModelCache = true;
        Assert.True(vm.OpenVinoSettings.HasDraftChanges);

        // CanApply 需要所选引擎已安装且有设备；测试直接装配卡片状态
        var card = vm.Engines.First(c => c.IsCpu);
        card.IsInstalled = true;
        card.UpdateDevices([ResolvedInferenceRuntime.CpuDevice], "CPU");
        vm.SelectedEngine = card;
        // OpenVinoReady 取决于 Plugins/OpenVINO；测试环境缺失时直接无法应用——先探测
        Assert.True(vm.CanApply, $"CanApply=false: hint={vm.ApplyHint}");
        vm.ApplyCommand.Execute(null);
        // RelayCommand 异步执行：轮询等待「落盘 + 草稿基线确认」全部完成（最长 5 秒）
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((config.PreferredBackend != InferenceBackend.OpenVino || vm.OpenVinoSettings.HasDraftChanges)
               && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        // 交互 fake 拒绝重启：设置已保存，等待重启
        Assert.Equal(InferenceBackend.OpenVino, config.PreferredBackend);
        Assert.True(config.OpenVino.UseYolo);
        Assert.Equal("GPU.0", config.OpenVino.DeviceId);
        Assert.True(config.OpenVino.EnableModelCache);
        Assert.False(vm.OpenVinoSettings.HasDraftChanges);
    }

    [Fact]
    public void PrepareStartup_MigratesLegacyOpenVinoEngineToStandaloneBackend()
    {
        // 旧版「OpenVINO 执行提供程序」用户：生效引擎 OpenVINO → 迁移为 OpenVINO 后端意图 + CPU 引擎
        var config = new HardwareAccelerationConfig
        {
            FormatVersion = 1,
            InferenceDevice = InferenceDeviceType.OpenVINO,
            OpenVINO = new() { DeviceId = "GPU.0" },
            CpuOcr = false,
            ValidationState = InferenceValidationState.None,
        };
        var status = InferenceRuntimeService.PrepareStartup(config);

        Assert.Equal(InferenceDeviceType.Cpu, config.InferenceDevice);
        Assert.Equal(InferenceBackend.OpenVino, config.PreferredBackend);
        Assert.True(config.OpenVino.UseYolo);
        Assert.True(config.OpenVino.UseOcr, "旧版未强制 OCR CPU 的用户应当默认 OpenVINO 跑 OCR");
        Assert.Equal("GPU.0", config.OpenVino.DeviceId);
        // 迁移必须告知用户（横幅），且迁移后开关已开启 → 运行库缺失提示会出现（本机未装运行库时）
        Assert.Contains("独立推理后端", status.FallbackMessage);
    }

    [Fact]
    public void UseCpuAsync_ResetsBackendDrafts()
    {
        var config = new HardwareAccelerationConfig();
        var store = new TestConfigService(config);
        using var runtime = CreateRuntime(config, store);
        var vm = CreateViewModel(config, store, runtime);
        vm.SelectedBackendDraft = InferenceBackend.Ncnn;
        vm.NcnnSettings.UseNcnnOcr = true;

        vm.UseCpuCommand.Execute(null);

        Assert.Equal(InferenceBackend.Onnx, vm.SelectedBackendDraft);
        Assert.False(vm.NcnnSettings.UseNcnnOcr);
        Assert.False(vm.OpenVinoSettings.UsesOpenVino);
    }

    // ---------------- 装配 ----------------

    private static HardwareAccelerationViewModel CreateViewModel()
    {
        var config = new HardwareAccelerationConfig();
        var store = new TestConfigService(config);
        var runtime = CreateRuntime(config, store);
        return CreateViewModel(config, store, runtime);
    }

    private static HardwareAccelerationViewModel CreateViewModel(HardwareAccelerationConfig config,
        TestConfigService store, InferenceRuntimeService runtime)
        => new(runtime, new Installer(), new Devices(), new Diagnostics(),
            new BgiOnnxFactory(NullLogger<BgiOnnxFactory>.Instance, runtime), store,
            new Leases(), new Interaction(), new FakeOpenVinoInstaller());

    private static InferenceRuntimeService CreateRuntime(HardwareAccelerationConfig config, TestConfigService store)
    {
        InferenceRuntimeService.PrepareStartup(config);
        return new InferenceRuntimeService(store, new Runtime(InferenceDeviceType.Cpu), new Devices(),
            new Diagnostics(), NullLogger<BgiOnnxFactory>.Instance);
    }

    private sealed class Runtime(InferenceDeviceType engine) : IOnnxRuntimeSelection
    {
        public InferenceDeviceType InferenceDevice => engine;
        public string RuntimeDirectory => "test-runtime";
        public string StatusText => "test";
        public string FailureReason => "";
    }

    private sealed class Devices : IInferenceDeviceDiscovery
    {
        public System.Collections.Generic.IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string directory)
            => [ResolvedInferenceRuntime.CpuDevice];
    }

    private sealed class Diagnostics : IInferenceModelDiagnostics
    {
        public bool HasDomainTreeImage => false;
        public Task ValidateTreeAsync(BgiOnnxSessionFactory factory, CancellationToken token) => Task.CompletedTask;
        public Task ValidateOcrAsync(BgiOnnxSessionFactory factory, CancellationToken token) => Task.CompletedTask;
        public Task PreheatOcrAsync() => Task.CompletedTask;
        public Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxSessionFactory factory,
            IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token)
            => Task.FromResult(new InferenceBenchmarkResult("OCR", "test", [1d]));
    }

    private sealed class Installer : IOnnxRuntimeInstaller
    {
        public string PluginsDirectory => "test-plugins";
        public RuntimeInstallation Inspect(InferenceDeviceType engine)
            => new(InferenceInstallationState.Installed, "test", "内置");
        public Task<IReadOnlyList<RuntimeDownloadPackage>> GetDownloadPlanAsync(InferenceDeviceType engine, bool dependencies, CancellationToken token)
            => Task.FromResult<IReadOnlyList<RuntimeDownloadPackage>>([]);
        public Task InstallAsync(InferenceDeviceType engine, IReadOnlyList<RuntimeDownloadPackage> packages,
            IProgress<RuntimeDownloadProgress>? progress, CancellationToken token) => Task.CompletedTask;
        public Task CleanupInstalledDownloadsAsync(CancellationToken token) => Task.CompletedTask;
    }

    private sealed class Leases : IInferenceBenchmarkLeaseProvider
    {
        public bool IsAvailable => true;
        public IDisposable Acquire() => new Disposable();
        private sealed class Disposable : IDisposable { public void Dispose() { } }
    }

    private sealed class Interaction : IInferenceSettingsInteraction
    {
        public Task<bool> ConfirmDownloadAsync(IReadOnlyList<RuntimeDownloadPackage> packages, bool dependencies) => Task.FromResult(false);
        public Task<bool> ConfirmRestartAsync(string pending, string validationHint, string currentEngine, bool interruptsTask) => Task.FromResult(false);
        public void Restart() { }
    }

    private sealed class TestConfigService(HardwareAccelerationConfig config) : IConfigService
    {
        private readonly AllConfig _all = new() { HardwareAccelerationConfig = config };
        public int SaveCount { get; private set; }
        public AllConfig Get() => _all;
        public AllConfig Read() => _all;
        public void Save() => SaveCount++;
        public void Flush() { }
        public void Write(AllConfig value) { }
    }
}
