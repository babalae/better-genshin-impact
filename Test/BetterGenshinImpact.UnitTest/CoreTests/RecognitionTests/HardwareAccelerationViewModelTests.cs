using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages.View;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public sealed class HardwareAccelerationViewModelTests
{
    [Fact]
    public async Task GameplayPriorityDraft_AppliesWithoutChangingCurrentSessionsAndSurvivesReload()
    {
        using var rig = await Rig.CreateAsync();
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(rig.ViewModel.CanApply);
        rig.ViewModel.PaddleOcrCpu.Mode = PaddleOcrCpuMode.GameplayPriority;
        Assert.True(rig.ViewModel.CanApply);
        Assert.Equal(PaddleOcrCpuMode.FollowGlobal, rig.Config.HardwareAccelerationConfig.PaddleOcrCpu.Mode);
        Assert.Equal(PaddleOcrCpuMode.FollowGlobal, rig.Runtime.Current.PaddleOcrCpu.Mode);
        await rig.ViewModel.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(PaddleOcrCpuMode.GameplayPriority, rig.Config.HardwareAccelerationConfig.PaddleOcrCpu.Mode);
        Assert.Equal(PaddleOcrCpuMode.FollowGlobal, rig.Runtime.Current.PaddleOcrCpu.Mode);
        Assert.True(rig.Runtime.HasPendingRestart);

        var saved = JsonSerializer.Deserialize<HardwareAccelerationConfig>(JsonSerializer.Serialize(rig.Config.HardwareAccelerationConfig))!;
        using var restarted = await Rig.CreateAsync(savedHardware: saved);
        await restarted.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(PaddleOcrCpuMode.GameplayPriority, restarted.ViewModel.PaddleOcrCpu.Mode);
        Assert.Equal(PaddleOcrCpuMode.GameplayPriority, restarted.Runtime.Current.PaddleOcrCpu.Mode);
        Assert.False(restarted.ViewModel.CanApply);
    }

    [Fact]
    public async Task RestoreDefaults_AlsoResetsDetailedOcrCpuParameters()
    {
        using var rig = await Rig.CreateAsync();
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        rig.ViewModel.PaddleOcrCpu.Mode = PaddleOcrCpuMode.Custom;
        rig.ViewModel.PaddleOcrCpu.DetectionThreads = 1;
        rig.ViewModel.PaddleOcrCpu.ParallelExecution = true;
        rig.ViewModel.PaddleOcrCpu.AllowSpinning = false;
        await rig.ViewModel.ApplyCommand.ExecuteAsync(null);
        await rig.ViewModel.UseCpuCommand.ExecuteAsync(null);
        Assert.Equal(new PaddleOcrCpuOptions(), rig.Config.HardwareAccelerationConfig.PaddleOcrCpu.ToOptions());
    }

    [Fact]
    public async Task DownloadThenApply_RefreshesAndOffersRestartWithoutReopeningSettings()
    {
        using var rig = await Rig.CreateAsync();
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        var card = rig.Card(InferenceDeviceType.CUDA);
        Assert.False(card.IsInstalled);
        var otherProbeCalls = rig.Devices.OpenVinoCalls;
        await rig.ViewModel.DownloadRuntimeCommand.ExecuteAsync(card);
        Assert.Equal(otherProbeCalls, rig.Devices.OpenVinoCalls);
        Assert.True(card.IsInstalled);
        Assert.NotNull(card.SelectedDevice);
        Assert.Same(card, rig.ViewModel.SelectedEngine);
        Assert.True(rig.ViewModel.CanApply);
        Assert.False(rig.ViewModel.IsBusy);
        await rig.ViewModel.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(1, rig.Interaction.Restarts);
        Assert.Equal(InferenceDeviceType.CUDA, rig.Config.HardwareAccelerationConfig.InferenceDevice);
        Assert.Equal(InferenceValidationState.PendingRestart, rig.Config.HardwareAccelerationConfig.ValidationState);
        Assert.Equal(InferenceDeviceType.Cpu, rig.Runtime.Current.Engine);
    }

    [Fact]
    public async Task Engines_DoNotOfferRetiredTensorRt()
    {
        using var rig = await Rig.CreateAsync();

        Assert.DoesNotContain(rig.ViewModel.Engines, card => card.Engine == InferenceDeviceType.TensorRT);
        Assert.Equal(InferenceDeviceType.Cpu, rig.ViewModel.SelectedEngine!.Engine);
    }

    [Fact]
    public async Task RetiredEngineInSavedConfiguration_OpensSettingsOnCpuCard()
    {
        // 启动准备之后不会再出现 TensorRT；设置页在历史配置上仍需稳健打开并允许一键回到 CPU。
        using var rig = await Rig.CreateAsync(InferenceDeviceType.TensorRT);
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(InferenceDeviceType.TensorRT, rig.Config.HardwareAccelerationConfig.InferenceDevice);
        Assert.Equal(InferenceDeviceType.Cpu, rig.ViewModel.SelectedEngine!.Engine);
        Assert.StartsWith("CPU", rig.ViewModel.SelectionText);
        Assert.True(rig.ViewModel.CanApply);
    }

    [Fact]
    public async Task InstalledRuntimeWithMissingDependencies_ExplainsBlockAndBecomesApplicableAfterCompletion()
    {
        using var rig = await Rig.CreateAsync();
        rig.Installer.State = InferenceInstallationState.MissingDependencies;
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        var card = rig.Card(InferenceDeviceType.CUDA);
        rig.ViewModel.SelectedEngine = card;
        Assert.False(rig.ViewModel.CanApply);
        Assert.True(card.CanCompleteDependencies);
        Assert.Contains("补齐配套依赖", rig.ViewModel.ApplyHint);
        await rig.ViewModel.CompleteDependenciesCommand.ExecuteAsync(card);
        Assert.True(rig.ViewModel.CanApply);
        Assert.False(card.CanCompleteDependencies);
    }

    [Fact]
    public async Task UpdatedLibraryWithUnchangedEngineAndDevice_CanApplyAndRestart()
    {
        using var rig = await Rig.CreateAsync(InferenceDeviceType.CUDA);
        rig.Installer.State = InferenceInstallationState.Installed;
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.False(rig.ViewModel.CanApply);
        rig.Installer.Directory = "runtime-v2";
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(rig.ViewModel.CanApply);
        await rig.ViewModel.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(1, rig.Interaction.Restarts);
        Assert.Equal("runtime-v1", rig.Runtime.Current.RuntimeDirectory);
    }

    [Fact]
    public async Task Refresh_PreservesDraftSelectionWhenComboBoxClearsOldItem()
    {
        using var rig = await Rig.CreateAsync();
        rig.Installer.State = InferenceInstallationState.Installed;
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        var card = rig.Card(InferenceDeviceType.CUDA);
        rig.ViewModel.SelectedEngine = card;
        card.SelectedDevice = card.Devices.Single(device => device.Id == "GPU-2");
        card.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(card.Devices)) card.SelectedDevice = null;
        };
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Same(card, rig.ViewModel.SelectedEngine);
        Assert.Equal("GPU-2", card.SelectedDevice!.Id);
        Assert.Equal("GPU-2", card.Settings.DeviceId);
        Assert.True(rig.ViewModel.CanApply);
    }

    [Fact]
    public async Task DeviceProbeFailure_CanRecoverByRefreshInSameProcess()
    {
        using var rig = await Rig.CreateAsync();
        rig.Installer.State = InferenceInstallationState.Installed;
        rig.Devices.FailCuda = true;
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        rig.ViewModel.SelectedEngine = rig.Card(InferenceDeviceType.CUDA);
        Assert.False(rig.ViewModel.CanApply);
        Assert.Contains("设备探测失败", rig.ViewModel.ApplyHint);
        rig.Devices.FailCuda = false;
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.True(rig.ViewModel.CanApply);
    }

    [Fact]
    public async Task ValidatedEngine_AfterSwitchingAndReloadingShowsHistoricalSuccess()
    {
        using var validated = await Rig.CreateAsync(InferenceDeviceType.CUDA);
        await validated.Runtime.ValidatePendingAsync();
        validated.Runtime.Apply(InferenceDeviceType.Cpu, new() { DeviceId = "CPU" }, true, 0);
        var saved = JsonSerializer.Deserialize<HardwareAccelerationConfig>(
            JsonSerializer.Serialize(validated.Config.HardwareAccelerationConfig))!;

        using var restarted = await Rig.CreateAsync(savedHardware: saved);
        restarted.Installer.State = InferenceInstallationState.Installed;
        await restarted.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("此前验证通过", restarted.Card(InferenceDeviceType.CUDA).StatusText);
        Assert.Equal("已安装 · 尚未验证", restarted.Card(InferenceDeviceType.DirectML).StatusText);
        Assert.Equal("内置 · 无需安装", restarted.Card(InferenceDeviceType.Cpu).StatusText);
    }

    [Fact]
    public async Task HistoricalSuccess_DoesNotHideLaterFailureOrMissingDependencies()
    {
        using var rig = await Rig.CreateAsync();
        var hardware = rig.Config.HardwareAccelerationConfig;
        hardware.RecordValidatedEngine(InferenceDeviceType.CUDA);
        rig.Installer.State = InferenceInstallationState.MissingDependencies;
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("配套依赖不完整", rig.Card(InferenceDeviceType.CUDA).StatusText);

        rig.Installer.State = InferenceInstallationState.Installed;
        hardware.LastFailedEngine = InferenceDeviceType.CUDA;
        hardware.LastValidationError = "最近一次模型验证失败";
        await rig.ViewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("上次验证未通过", rig.Card(InferenceDeviceType.CUDA).StatusText);
        Assert.Contains(hardware.LastValidationError, rig.Card(InferenceDeviceType.CUDA).Details);
    }

    [Fact]
    public void DirectMl_DeduplicatesPhysicalGpuAndPreservesOriginalOrdinal()
    {
        var devices = new InferenceDeviceInfo[]
        {
            new("PCI:1", "5070 Ti", 1, "1"), new("PCI:1", "5070 Ti", 3, "3"),
            new("PCI:2", "5070 Ti", 5, "5")
        };
        var result = InferenceDeviceDiscovery.NormalizeDirectMlDevices(devices);
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { 1, 5 }, result.Select(device => device.Index));
        Assert.Equal(new[] { "1", "5" }, result.Select(device => device.ProviderDevice));
        Assert.NotEqual(result[0].Name, result[1].Name);
    }

    private sealed class Rig : IDisposable
    {
        public AllConfig Config { get; } = new();
        public Installer Installer { get; } = new();
        public Devices Devices { get; } = new();
        public Interaction Interaction { get; } = new();
        public InferenceRuntimeService Runtime { get; private set; } = null!;
        public HardwareAccelerationViewModel ViewModel { get; private set; } = null!;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        public InferenceEngineCardViewModel Card(InferenceDeviceType type) => ViewModel.Engines.Single(card => card.Engine == type);

        public static async Task<Rig> CreateAsync(InferenceDeviceType engine = InferenceDeviceType.Cpu,
            HardwareAccelerationConfig? savedHardware = null)
        {
            var rig = new Rig();
            if (savedHardware != null) rig.Config.HardwareAccelerationConfig = savedHardware;
            var hardware = rig.Config.HardwareAccelerationConfig;
            hardware.FormatVersion = 1;
            hardware.InferenceDevice = engine;
            hardware.GetEngineSettings(engine).DeviceId = engine == InferenceDeviceType.Cpu ? "CPU" : "GPU-1";
            hardware.ValidationState = engine == InferenceDeviceType.Cpu ? InferenceValidationState.None : InferenceValidationState.Validating;
            var configuration = new Configuration(rig.Config);
            var diagnostic = new Diagnostics();
            rig.Runtime = new(configuration, new RuntimeSelection(engine), rig.Devices, diagnostic, NullLogger<BgiOnnxFactory>.Instance);
            await rig.Runtime.InitializeAsync();
            var factory = new HardwareAccelerationViewModelFactory(rig.Runtime, rig.Installer, rig.Devices, diagnostic,
                new BgiOnnxFactory(NullLogger<BgiOnnxFactory>.Instance, rig.Runtime.Current), configuration,
                new InferenceBenchmarkCoordinator(rig._semaphore, () => throw new NotSupportedException()), rig.Interaction);
            rig.ViewModel = factory.Create();
            return rig;
        }

        public void Dispose() { ViewModel.Dispose(); _semaphore.Dispose(); }
    }

    private sealed class Installer : IOnnxRuntimeInstaller
    {
        public InferenceInstallationState State { get; set; } = InferenceInstallationState.Missing;
        public string Directory { get; set; } = "runtime-v1";
        public string PluginsDirectory => "test-plugins";
        public RuntimeInstallation Inspect(InferenceDeviceType engine) => new(engine == InferenceDeviceType.Cpu
            ? InferenceInstallationState.Installed : State, Directory, "test");
        public Task<IReadOnlyList<RuntimeDownloadPackage>> GetDownloadPlanAsync(InferenceDeviceType engine, bool dependencies, CancellationToken token)
            => Task.FromResult<IReadOnlyList<RuntimeDownloadPackage>>([new("fake package", new("https://example.test"), 1, 1, null, "SHA256")]);
        public Task InstallAsync(InferenceDeviceType engine, IReadOnlyList<RuntimeDownloadPackage> packages,
            IProgress<RuntimeDownloadProgress>? progress, CancellationToken token)
        {
            State = InferenceInstallationState.Installed;
            return Task.CompletedTask;
        }
        public Task CleanupInstalledDownloadsAsync(CancellationToken token) => Task.CompletedTask;
    }

    private sealed class Devices : IInferenceDeviceDiscovery
    {
        public bool FailCuda { get; set; }
        public int OpenVinoCalls { get; private set; }
        public IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string directory)
        {
            if (engine == InferenceDeviceType.OpenVINO) OpenVinoCalls++;
            if (engine == InferenceDeviceType.CUDA && FailCuda) throw new InvalidOperationException("test probe failure");
            return engine == InferenceDeviceType.Cpu ? [InferenceRuntimeSnapshot.CpuDefault().Device]
                : [new("GPU-1", "GPU 1", 0, "0"), new("GPU-2", "GPU 2", 1, "1")];
        }
    }

    private sealed class Diagnostics : IInferenceModelDiagnostics
    {
        public bool HasDomainTreeImage => false;
        public Task<InferenceValidationResult> ValidateTreeAsync(BgiOnnxFactory factory, CancellationToken token)
            => Task.FromResult(InferenceValidationResult.Passed("古树检测"));
        public Task<InferenceValidationResult> ValidateOcrAsync(BgiOnnxFactory factory, CancellationToken token)
            => Task.FromResult(InferenceValidationResult.Passed("OCR"));
        public Task PreheatOcrAsync() => Task.CompletedTask;
        public Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxFactory factory,
            IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class Interaction : IInferenceSettingsInteraction
    {
        public int Restarts { get; private set; }
        public Task<bool> ConfirmDownloadAsync(IReadOnlyList<RuntimeDownloadPackage> packages, bool dependencies) => Task.FromResult(true);
        public Task<bool> ConfirmRestartAsync(string pending, string validationHint, string currentEngine, bool interruptsTask) => Task.FromResult(true);
        public void Restart() => Restarts++;
    }

    private sealed class RuntimeSelection(InferenceDeviceType engine) : IOnnxRuntimeSelection
    {
        public InferenceDeviceType InferenceDevice => engine;
        public string RuntimeDirectory => "runtime-v1";
        public string StatusText => "test";
        public string FailureReason => "";
    }

    private sealed class Configuration(AllConfig config) : IConfigService
    {
        public AllConfig Get() => config;
        public AllConfig Read() => config;
        public void Save() { }
        public void Flush() { }
        public void Write(AllConfig value) { }
    }
}
