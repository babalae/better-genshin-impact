using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.ViewModel.Pages.View;

public partial class HardwareAccelerationViewModel : ViewModel
{
    private readonly IOnnxRuntimePluginManager _pluginManager;
    private readonly IInferenceDeviceDiscoveryService _deviceDiscoveryService;
    private CancellationTokenSource? _downloadCancellationTokenSource;
    private IReadOnlyList<InferenceDeviceDescriptor> _allDevices = [];
    private bool _isLoaded;
    private bool _suppressSelectionChange;

    public HardwareAccelerationViewModel(
        IConfigService configService,
        BgiOnnxFactory status,
        IOnnxRuntimePluginManager pluginManager,
        IInferenceDeviceDiscoveryService deviceDiscoveryService)
    {
        Config = configService.Get().HardwareAccelerationConfig;
        Status = status;
        _pluginManager = pluginManager;
        _deviceDiscoveryService = deviceDiscoveryService;
        _providerTypesText = string.Join(",", Status.ProviderTypes);
        _effectiveRuntimeText = $"{Status.EffectiveProvider} / {Status.EffectiveDevice}";
        _nextRuntimeText = GetProviderDisplayName(Config.InferenceDevice);
        _startupDiagnostic = Status.StartupDiagnostic;
        _ortVersion = OrtEnv.Instance().GetVersionString();
        _selectedCudaRuntime = Config.CudaRuntime;
        _openVinoDeviceText = Config.OpenVinoDevice ?? "AUTO";
        _restartRequired = Config.InferenceDevice != Status.ConfiguredProvider ||
                           (Config.InferenceDevice == InferenceDeviceType.Cuda &&
                            (Config.CudaRuntime != Status.CudaRuntime ||
                             Config.CudaDevice != Status.CudaDeviceId)) ||
                           (Config.InferenceDevice == InferenceDeviceType.GpuDirectMl &&
                            Config.GpuDevice != Status.DmlDeviceId);
        _storageStatus = pluginManager.IsStorageWritable
            ? pluginManager.StorageRoot
            : pluginManager.StorageError;
        ReplacePackages(pluginManager.GetCurrentPackages());
        RebuildProviderOptions();
    }

    public HardwareAccelerationConfig Config { get; }
    public BgiOnnxFactory Status { get; }
    public ObservableCollection<InferenceProviderOptionViewModel> ProviderOptions { get; } = [];
    public ObservableCollection<InferenceDeviceDescriptor> InferenceDevices { get; } = [];
    public ObservableCollection<OnnxRuntimePluginItemViewModel> PluginPackages { get; } = [];
    public HardwareAccelerationConfig.CudaRuntimeMajor[] CudaRuntimeMajors { get; } =
        Enum.GetValues<HardwareAccelerationConfig.CudaRuntimeMajor>();

    [ObservableProperty]
    private InferenceProviderOptionViewModel? _selectedProvider;

    [ObservableProperty]
    private InferenceDeviceDescriptor? _selectedInferenceDevice;

    [ObservableProperty]
    private string _providerTypesText;

    [ObservableProperty]
    private string _effectiveRuntimeText;

    [ObservableProperty]
    private string _nextRuntimeText;

    [ObservableProperty]
    private string _startupDiagnostic;

    [ObservableProperty]
    private string _ortVersion;

    [ObservableProperty]
    private string _storageStatus;

    [ObservableProperty]
    private string _pageMessage = "";

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _restartRequired;

    [ObservableProperty]
    private HardwareAccelerationConfig.CudaRuntimeMajor _selectedCudaRuntime;

    [ObservableProperty]
    private string _openVinoDeviceText;

    partial void OnSelectedCudaRuntimeChanged(HardwareAccelerationConfig.CudaRuntimeMajor value)
    {
        if (_suppressSelectionChange)
        {
            return;
        }

        Config.CudaRuntime = value;
        RestartRequired = true;
    }

    partial void OnOpenVinoDeviceTextChanged(string value)
    {
        if (_suppressSelectionChange)
        {
            return;
        }

        Config.OpenVinoDevice = string.IsNullOrWhiteSpace(value) ? "AUTO" : value.Trim();
        RestartRequired = true;
        NextRuntimeText = Config.InferenceDevice == InferenceDeviceType.OpenVino
            ? $"OpenVINO / {Config.OpenVinoDevice}"
            : NextRuntimeText;
    }

    partial void OnSelectedProviderChanged(InferenceProviderOptionViewModel? value)
    {
        if (_suppressSelectionChange || value is null)
        {
            return;
        }

        if (!value.IsAvailable)
        {
            PageMessage = value.UnavailableReason;
            RebuildProviderOptions();
            return;
        }

        Config.InferenceDevice = value.Type;
        RestartRequired |= Config.InferenceDevice != Status.ConfiguredProvider;
        FilterDevicesForSelectedProvider();
        UpdateNextRuntimeText();
    }

    partial void OnSelectedInferenceDeviceChanged(InferenceDeviceDescriptor? value)
    {
        if (_suppressSelectionChange || value is null || !value.IsAvailable)
        {
            return;
        }

        switch (value.Provider)
        {
            case InferenceDeviceType.GpuDirectMl:
                Config.GpuDevice = checked((int)value.DeviceId);
                Config.DirectMlAdapterLuid = value.StableId;
                break;
            case InferenceDeviceType.Cuda:
                Config.CudaDevice = checked((int)value.DeviceId);
                Config.CudaDeviceUuid = value.StableId.StartsWith("Cuda:", StringComparison.OrdinalIgnoreCase)
                    ? value.StableId["Cuda:".Length..]
                    : value.StableId;
                break;
            case InferenceDeviceType.OpenVino:
                OpenVinoDeviceText = value.ProviderOption;
                break;
        }

        RestartRequired = true;
        UpdateNextRuntimeText();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        PageMessage = "";
        try
        {
            try
            {
                ReplacePackages(await _pluginManager.RefreshCatalogAsync());
            }
            catch (Exception exception)
            {
                ReplacePackages(_pluginManager.GetCurrentPackages());
                PageMessage = $"在线包信息刷新失败，已显示本地状态：{exception.Message}";
            }

            _allDevices = await _deviceDiscoveryService.DiscoverAsync();
            RebuildProviderOptions();
            FilterDevicesForSelectedProvider();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task InstallPluginAsync(OnnxRuntimePluginItemViewModel? item)
    {
        if (item is null || item.IsBusy)
        {
            return;
        }

        _downloadCancellationTokenSource?.Dispose();
        _downloadCancellationTokenSource = new CancellationTokenSource();
        item.IsBusy = true;
        item.CanDownload = false;
        item.StatusMessage = "正在下载…";
        PageMessage = "";
        var progress = new Progress<PluginDownloadProgress>(value =>
        {
            item.DownloadProgress = value.Percentage;
            item.ProgressText = value.TotalBytes is > 0
                ? $"{FormatBytes(value.BytesReceived)} / {FormatBytes(value.TotalBytes.Value)}"
                : FormatBytes(value.BytesReceived);
        });

        try
        {
            await _pluginManager.InstallAsync(item.Descriptor, progress,
                _downloadCancellationTokenSource.Token);
            RestartRequired = true;
            PageMessage = $"{item.Descriptor.DisplayName} 已安装，重启程序后生效。";
            ReplacePackages(_pluginManager.GetCurrentPackages());
            _allDevices = await _deviceDiscoveryService.DiscoverAsync();
            RebuildProviderOptions();
            FilterDevicesForSelectedProvider();
        }
        catch (OperationCanceledException)
        {
            PageMessage = "下载已取消。";
        }
        catch (Exception exception)
        {
            item.StatusMessage = $"安装失败：{GetInnermostExceptionMessage(exception)}";
            PageMessage = item.StatusMessage;
        }
        finally
        {
            item.IsBusy = false;
            item.CanDownload = item.DownloadAllowed;
            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private async Task UninstallPluginAsync(OnnxRuntimePluginItemViewModel? item)
    {
        if (item is null || !item.CanUninstall)
        {
            return;
        }

        var result = await ThemedMessageBox.ShowAsync(
            $"确定卸载 {item.Descriptor.DisplayName}？如果它是当前运行时，下次启动将切换到 CPU。",
            "卸载 Plugin EP",
            MessageBoxButton.YesNo,
            ThemedMessageBox.MessageBoxIcon.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (Config.InferenceDevice == item.Descriptor.Provider)
            {
                Config.InferenceDevice = InferenceDeviceType.Cpu;
                UpdateNextRuntimeText();
            }

            await _pluginManager.UninstallAsync(item.Descriptor.Id);
            RestartRequired = true;
            PageMessage = "已记录卸载操作；被其他实例占用的文件将在后续启动时清理。";
            ReplacePackages(_pluginManager.GetCurrentPackages());
            RebuildProviderOptions();
            FilterDevicesForSelectedProvider();
        }
        catch (Exception exception)
        {
            PageMessage = $"卸载失败：{exception.Message}";
        }
    }

    [RelayCommand]
    private async Task SelectPluginAsync(OnnxRuntimePluginItemViewModel? item)
    {
        if (item is null || !item.CanSelect)
        {
            return;
        }

        if (item.Descriptor.Provider == InferenceDeviceType.Cuda)
        {
                SelectedCudaRuntime = item.Descriptor.CudaMajor switch
                {
                12 => HardwareAccelerationConfig.CudaRuntimeMajor.Cuda12,
                13 => HardwareAccelerationConfig.CudaRuntimeMajor.Cuda13,
                    _ => HardwareAccelerationConfig.CudaRuntimeMajor.Auto
                };
        }

        _allDevices = await _deviceDiscoveryService.DiscoverAsync();
        RebuildProviderOptions();
        var provider = ProviderOptions.FirstOrDefault(option => option.Type == item.Descriptor.Provider);
        if (provider is { IsAvailable: true })
        {
            SelectedProvider = provider;
            RestartRequired = true;
        }
        else
        {
            PageMessage = provider?.UnavailableReason ?? "Plugin EP 当前不可用。";
        }
    }

    [RelayCommand]
    private async Task ShowPluginDependenciesAsync(OnnxRuntimePluginItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await ThemedMessageBox.ShowAsync(
            $"{item.RequirementsText}\n\n{item.Descriptor.Description}\n\n当前状态：{item.StatusMessage}",
            $"{item.Descriptor.DisplayName} 依赖说明",
            MessageBoxButton.OK,
            ThemedMessageBox.MessageBoxIcon.Information,
            MessageBoxResult.OK);
    }

    [RelayCommand]
    private void OpenPluginFolder(OnnxRuntimePluginItemViewModel? item)
    {
        try
        {
            var path = item is null
                ? _pluginManager.StorageRoot
                : _pluginManager.GetPluginDirectory(item.Descriptor.Id);
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            PageMessage = $"打开目录失败：{exception.Message}";
        }
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        try
        {
            var path = Path.Combine(_pluginManager.StorageRoot, "cache");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            PageMessage = $"打开缓存目录失败：{exception.Message}";
        }
    }

    private void ReplacePackages(IReadOnlyList<OnnxRuntimePluginInfo> packages)
    {
        PluginPackages.Clear();
        foreach (var package in packages)
        {
            PluginPackages.Add(new OnnxRuntimePluginItemViewModel(package));
        }

        if (packages.Any(package => package.Status == OnnxRuntimePluginStatusKind.PendingRestart))
        {
            RestartRequired = true;
        }
    }

    private void RebuildProviderOptions()
    {
        var installedProviders = PluginPackages
            .Where(package => !string.IsNullOrWhiteSpace(package.InstalledVersion))
            .Select(package => package.Descriptor.Provider)
            .ToHashSet();
        var availableDeviceProviders = _allDevices
            .Where(device => device.IsAvailable)
            .Select(device => device.Provider)
            .ToHashSet();

        _suppressSelectionChange = true;
        try
        {
            ProviderOptions.Clear();
            ProviderOptions.Add(new(InferenceDeviceType.Cpu, "CPU", true, ""));
            ProviderOptions.Add(new(InferenceDeviceType.GpuDirectMl, "DirectML", true, ""));
            ProviderOptions.Add(new(InferenceDeviceType.Cuda, "CUDA",
                installedProviders.Contains(InferenceDeviceType.Cuda) &&
                availableDeviceProviders.Contains(InferenceDeviceType.Cuda),
                installedProviders.Contains(InferenceDeviceType.Cuda)
                    ? "未检测到可用 CUDA 设备或 CUDA/cuDNN 依赖。"
                    : "请先下载 CUDA Plugin EP。"));
            ProviderOptions.Add(new(InferenceDeviceType.OpenVino, "OpenVINO",
                installedProviders.Contains(InferenceDeviceType.OpenVino) &&
                availableDeviceProviders.Contains(InferenceDeviceType.OpenVino),
                installedProviders.Contains(InferenceDeviceType.OpenVino)
                    ? "OpenVINO Plugin EP 未发现可用的 Intel 设备。"
                    : "请先下载 OpenVINO Plugin EP。"));

            SelectedProvider = ProviderOptions.FirstOrDefault(option =>
                                   option.Type == Config.InferenceDevice)
                               ?? ProviderOptions[0];
        }
        finally
        {
            _suppressSelectionChange = false;
        }
    }

    private void FilterDevicesForSelectedProvider()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        _suppressSelectionChange = true;
        try
        {
            InferenceDevices.Clear();
            foreach (var device in _allDevices.Where(device => device.Provider == SelectedProvider.Type))
            {
                InferenceDevices.Add(device);
            }

            if (SelectedProvider.Type == InferenceDeviceType.OpenVino &&
                !string.IsNullOrWhiteSpace(Config.OpenVinoDevice) &&
                !Config.OpenVinoDevice.StartsWith("AUTO", StringComparison.OrdinalIgnoreCase) &&
                InferenceDevices.All(device =>
                    !device.ProviderOption.Equals(Config.OpenVinoDevice, StringComparison.OrdinalIgnoreCase)))
            {
                InferenceDevices.Add(new InferenceDeviceDescriptor(
                    InferenceDeviceType.OpenVino,
                    $"openvino:custom:{Config.OpenVinoDevice}",
                    $"自定义：{Config.OpenVinoDevice}",
                    InferenceHardwareType.Cpu,
                    "Intel",
                    0,
                    Config.OpenVinoDevice));
            }

            SelectedInferenceDevice = SelectedProvider.Type switch
            {
                InferenceDeviceType.Cpu => InferenceDevices.FirstOrDefault(),
                InferenceDeviceType.GpuDirectMl => InferenceDevices.FirstOrDefault(device =>
                    device.StableId.Equals(Config.DirectMlAdapterLuid, StringComparison.OrdinalIgnoreCase))
                    ?? InferenceDevices.FirstOrDefault(device => device.DeviceId == Config.GpuDevice),
                InferenceDeviceType.Cuda => InferenceDevices.FirstOrDefault(device =>
                    !string.IsNullOrWhiteSpace(Config.CudaDeviceUuid) &&
                    device.StableId.EndsWith(Config.CudaDeviceUuid, StringComparison.OrdinalIgnoreCase))
                    ?? InferenceDevices.FirstOrDefault(device => device.DeviceId == Config.CudaDevice),
                InferenceDeviceType.OpenVino => InferenceDevices.FirstOrDefault(device =>
                    Config.OpenVinoDevice?.StartsWith("AUTO", StringComparison.OrdinalIgnoreCase) == true
                        ? device.StableId.Equals("openvino:auto", StringComparison.OrdinalIgnoreCase)
                        : device.ProviderOption.Equals(Config.OpenVinoDevice, StringComparison.OrdinalIgnoreCase))
                    ?? InferenceDevices.FirstOrDefault(),
                _ => null
            };
            UpdateNextRuntimeText();
        }
        finally
        {
            _suppressSelectionChange = false;
        }
    }

    private void UpdateNextRuntimeText()
    {
        NextRuntimeText = SelectedInferenceDevice is null
            ? GetProviderDisplayName(Config.InferenceDevice)
            : SelectedInferenceDevice.FullDisplayName;
    }

    private static string GetProviderDisplayName(InferenceDeviceType provider)
    {
        return provider switch
        {
            InferenceDeviceType.GpuDirectMl => "DirectML",
            InferenceDeviceType.OpenVino => "OpenVINO",
            InferenceDeviceType.Cuda => "CUDA",
            _ => "CPU"
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string GetInnermostExceptionMessage(Exception exception)
    {
        while (exception.InnerException is not null)
        {
            exception = exception.InnerException;
        }

        return exception.Message;
    }
}

public sealed record InferenceProviderOptionViewModel(
    InferenceDeviceType Type,
    string DisplayName,
    bool IsAvailable,
    string UnavailableReason);

public partial class OnnxRuntimePluginItemViewModel : ObservableObject
{
    public OnnxRuntimePluginItemViewModel(OnnxRuntimePluginInfo info)
    {
        Descriptor = info.Descriptor;
        InstalledVersion = info.InstalledVersion;
        Status = info.Status;
        CanSelect = info.Status is OnnxRuntimePluginStatusKind.Installed or
            OnnxRuntimePluginStatusKind.UpdateAvailable or OnnxRuntimePluginStatusKind.PendingRestart;
        _statusMessage = info.StatusMessage;
        _canDownload = info.CanDownload;
        DownloadAllowed = info.CanDownload;
        _canUninstall = info.CanUninstall;
    }

    public OnnxRuntimePluginDescriptor Descriptor { get; }
    public string InstalledVersion { get; }
    public OnnxRuntimePluginStatusKind Status { get; }
    public bool DownloadAllowed { get; }
    public bool CanSelect { get; }
    public string SourceText => Descriptor.Source == OnnxRuntimePluginSourceKind.NuGet
        ? "官方 NuGet"
        : "BetterGI 下载源";
    public string VersionText => string.IsNullOrWhiteSpace(InstalledVersion)
        ? $"在线 {Descriptor.Version}"
        : $"已安装 {InstalledVersion} / 在线 {Descriptor.Version}";
    public string DownloadSizeText => Descriptor.DownloadSize > 0
        ? $"{Descriptor.DownloadSize / 1024d / 1024:0.#} MB"
        : "大小待发布";
    public string RequirementsText => Descriptor.Provider == InferenceDeviceType.Cuda
        ? $"最低 ORT {Descriptor.MinimumOrtVersion}；CUDA {Descriptor.CudaMajor}；cuDNN 9；NVIDIA 驱动"
        : $"最低 ORT {Descriptor.MinimumOrtVersion}；Intel CPU/GPU/NPU 驱动由 OpenVINO 运行时使用";

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private bool _canDownload;

    [ObservableProperty]
    private bool _canUninstall;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _progressText = "";
}
